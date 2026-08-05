using CAS_Demo.Scripts.FPS.Attachments;
using FPSProject.Combat.Runtime;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

namespace CAS_Demo.Scripts.FPS
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS/Weapon")]
    public class WeaponProp : CasProp
    {
        public RecoilAnimation RecoilComponent => _recoilAnimation;
        public bool IsFiring => _isFiring;

        [Header("Weapon")]
        public WeaponSettings weaponSettings;
        [SerializeField] protected FireMode fireMode = FireMode.Semi;
        public Transform defaultAimPoint;

        [Header("Combat")]
        [Tooltip("Muzzle point transform on the weapon prefab. Used as the shot origin.")]
        public Transform muzzlePoint;

        [Header("Attachments")]
        public WeaponAttachmentGroup<WeaponAttachment> muzzle = new WeaponAttachmentGroup<WeaponAttachment>();
        public WeaponAttachmentGroup<GripAttachment> grips = new WeaponAttachmentGroup<GripAttachment>();
        public WeaponAttachmentGroup<ScopeAttachment> scopes = new WeaponAttachmentGroup<ScopeAttachment>();
        public AnimationAsset inspectStart;
        public AnimationAsset inspectEnd;
        
        protected RecoilAnimation _recoilAnimation;
        protected CharacterAnimationComponent _characterAnimation;
        protected ProceduralAnimationComponent _proceduralAnimation;
        protected SimpleAnimationPlayer _animationPlayer;
        protected CharacterCamera _characterCamera;
        protected int _burstCount;
        protected bool _isFiring;

        protected AudioSource _audioSource;
        protected WeaponCombatRuntime _combatRuntime;
        protected Camera _combatAimCamera;
        protected IWeaponMuzzleProvider _combatMuzzleProvider;

        // Network shot routing. When a shot router is present on the player root, shots are
        // routed through it instead of resolved directly on the local WeaponCombatRuntime.
        // The router decides offline (local resolve), network owner (send command to host), or
        // proxy (drop). These fields are set by the network adapter (NetworkWeaponPropBridge).
        protected FPSProject.Multiplayer.Core.Weapons.IWeaponShotRouter _shotRouter;
        protected ushort _networkWeaponId;
        protected int _networkTick;
        protected bool _isAimingForRouter;

#if UNITY_EDITOR
        public static readonly string WeaponSettingsName = nameof(weaponSettings);
        public static readonly string GetScopeDataName = nameof(GetScopeData);
        public static readonly string GetGripDataName = nameof(GetGripData);
#endif

        public void PlaySoundByEvent(int index)
        {
            if (_audioSource == null)
            {
                Debug.LogWarning($"Failed to play weapon sound: invalid Audio Source!");
                return;
            }

            int length = weaponSettings.animEventSounds.Count;
            if (index < 0 || index > length - 1) return;

            var audioClip = weaponSettings.animEventSounds[index];
            _audioSource.PlayOneShot(audioClip);
        }
        
        protected void PlaySound(AudioClip clip, float pitch = 1f, float volume = 1f)
        {
            if (_audioSource == null)
            {
                Debug.LogWarning($"Failed to play weapon sound: invalid Audio Source!");
                return;
            }

            _audioSource.pitch = pitch;
            _audioSource.volume = volume;
            _audioSource.PlayOneShot(clip);
        }

        public virtual ScopeAttachmentData GetScopeData()
        {
            ScopeAttachment activeScope = scopes.GetActiveAttachment();
            if (activeScope == null)
            {
                return new ScopeAttachmentData() { aimPoint = defaultAimPoint };
            }

            return activeScope.GetScopeData();
        }

        public virtual GripAttachmentData GetGripData()
        {
            GripAttachment activeGrip = grips.GetActiveAttachment();
            if (activeGrip == null)
            {
                return new GripAttachmentData() {pose = animationSettings.overlayPose};
            }

            return activeGrip.GripData;
        }
        
        protected void Fire()
        {
            if (!_isFiring) return;

            if (weaponSettings.fireSounds.Count > 0)
            {
                PlaySound(weaponSettings.fireSounds[Random.Range(0, weaponSettings.fireSounds.Count)]);
            }

            if(_recoilAnimation != null) _recoilAnimation.Play();
            if(_characterCamera != null) _characterCamera.PlayCameraShake(weaponSettings.recoilShake);

            PlayCharacterWeaponAnimation(weaponSettings.fire);
            
            // Submit combat shot after presentation but before burst decrement
            SubmitCombatShot();

            if (fireMode == FireMode.Burst) _burstCount--;

            if (fireMode == FireMode.Semi || fireMode == FireMode.Burst && _burstCount == 0)
            {
                StopUsingItem();
                return;
            }
            
            Invoke(nameof(Fire), 60f / weaponSettings.fireRate);
        }

        protected void StopRecoil()
        {
            if (_recoilAnimation != null) _recoilAnimation.Stop();
        }

        public virtual void ChangeFireMode()
        {
            if (fireMode == FireMode.Semi)
            {
                fireMode = weaponSettings.supportsBurst ? FireMode.Burst :
                    weaponSettings.supportsAuto ? FireMode.Auto : FireMode.Semi;
            }
            else if (fireMode == FireMode.Burst)
            {
                fireMode = weaponSettings.supportsAuto ? FireMode.Auto : FireMode.Semi;
            }
            else
            {
                fireMode = FireMode.Semi;
            }

            _recoilAnimation.fireMode = fireMode;
        }

        public override void StopUsingItem()
        {
            _isFiring = false;
            CancelInvoke(nameof(Fire));
            Invoke(nameof(StopRecoil), 0.04f);
        }

        public override void UseItem()
        {
            _isFiring = true;
            
            if (fireMode == FireMode.Burst) _burstCount = weaponSettings.burstRounds;
            
            CancelInvoke(nameof(StopRecoil));
            Fire();
        }

        public override void OnEquipped()
        {
            if (weaponSettings.equip.character != null)
            {
                PlayCharacterWeaponAnimation(weaponSettings.equip);
                return;
            }
            
            if (_recoilAnimation != null)
            {
                _recoilAnimation.Init(weaponSettings.recoilData, weaponSettings.fireRate, fireMode);
            }
            
            _proceduralAnimation.UpdateAnimationModifier(weaponSettings.equipMotion);
        }

        public override float OnUnEquipped()
        {
            if (weaponSettings.unEquip.character != null)
            {
                PlayCharacterWeaponAnimation(weaponSettings.unEquip);
                return weaponSettings.unEquip.character.GetPlayLength();
            }
            
            _proceduralAnimation.UpdateAnimationModifier(weaponSettings.unEquipMotion);
            
            return weaponSettings.unEquipMotion.GetLength();
        }

        public override void OnAim(bool isAiming)
        {
            _recoilAnimation.isAiming = isAiming;
            UpdateCameraFOV();
        }

        public virtual void OnAlternativeAim()
        {
            var scope = scopes.GetActiveAttachment();
            if (scope != null) scope.CycleScopes();
            UpdateCameraFOV();
        }

        public virtual void Reload()
        {
            PlayCharacterWeaponAnimation(weaponSettings.reload);
        }

        public virtual void ToggleAttachmentEditing(bool editingAttachments)
        {
            _characterAnimation.PlayAnimation(editingAttachments ? inspectStart : inspectEnd);
        }

        public virtual void CycleAttachments(int index)
        {
            if (index == 1)
            {
                muzzle.CycleAttachments(true);
                return;
            }

            if (index == 2)
            {
                grips.CycleAttachments(true);
                return;
            }

            
            scopes.CycleAttachments(true);
            UpdateCameraFOV();
        }

        protected void UpdateCameraFOV()
        {
            float targetFov = _recoilAnimation.isAiming ? GetScopeData().aimFov : _characterCamera.DefaultFov;
            _characterCamera.UpdateTargetFOV(targetFov, 8f);
        }

        protected virtual void PlayCharacterWeaponAnimation(CharacterWeaponAnimation newAnimation)
        {
            if (_characterAnimation != null) _characterAnimation.PlayAnimation(newAnimation.character);
            if (_animationPlayer != null) _animationPlayer.PlayAnimation(newAnimation.weapon);
        }

        protected virtual void Awake()
        {
            Transform root = transform.root;

            _recoilAnimation = root.GetComponentInChildren<RecoilAnimation>();
            _characterAnimation = root.GetComponentInChildren<CharacterAnimationComponent>();
            _proceduralAnimation = root.GetComponentInChildren<ProceduralAnimationComponent>();
            _characterCamera = root.GetComponentInChildren<CharacterCamera>();
            _animationPlayer = GetComponentInChildren<SimpleAnimationPlayer>();
            _audioSource = GetComponent<AudioSource>();

            // Cache combat runtime from root/parents
            _combatRuntime = root.GetComponentInChildren<WeaponCombatRuntime>();
            if (_combatRuntime != null)
            {
                _combatAimCamera = _combatRuntime.AimCamera;
            }
            _combatMuzzleProvider = root.GetComponent<IWeaponMuzzleProvider>();

            muzzle.GetActiveAttachment()?.EnableAttachment();
            scopes.GetActiveAttachment()?.EnableAttachment();
            grips.GetActiveAttachment()?.EnableAttachment();
        }

        /// <summary>
        /// Set the network shot router used by <see cref="SubmitCombatShot"/>. When set, shots
        /// are routed through the router instead of resolved directly on the local
        /// <see cref="WeaponCombatRuntime"/>. The network adapter calls this on the owner.
        /// </summary>
        public void SetShotRouter(FPSProject.Multiplayer.Core.Weapons.IWeaponShotRouter router)
        {
            _shotRouter = router;
        }

        /// <summary>
        /// Set the stable catalog weapon ID for this weapon prop. The network adapter calls this
        /// so the owner's shot commands carry the correct weapon ID for host validation.
        /// </summary>
        public void SetNetworkWeaponId(ushort weaponId)
        {
            _networkWeaponId = weaponId;
        }

        /// <summary>
        /// Set the synchronized network tick at fire time. The network adapter updates this so
        /// shot commands carry a tick the host can map to its pose history for lag compensation.
        /// </summary>
        public void SetNetworkTick(int tick)
        {
            _networkTick = tick;
        }

        /// <summary>
        /// Set the aiming flag used by the shot router. The network adapter updates this so the
        /// host receives the correct ADS/hip flag for spread selection.
        /// </summary>
        public void SetNetworkAiming(bool isAiming)
        {
            _isAimingForRouter = isAiming;
        }

        /// <summary>
        /// Submits a combat shot through the shared WeaponCombatRuntime.
        /// Does nothing if combat is disabled, runtime is missing, or camera is missing.
        /// </summary>
        protected virtual void SubmitCombatShot()
        {
            if (_combatRuntime == null) return;
            if (!weaponSettings.ballistics.combatEnabled) return;
            if (_combatAimCamera == null)
            {
                Debug.LogWarning($"[{nameof(WeaponProp)}] Cannot submit combat shot: no aim camera found on {nameof(WeaponCombatRuntime)}.");
                return;
            }

            Vector3 muzzlePos;
            Quaternion muzzleRot;

            if (_combatMuzzleProvider != null
                && _combatMuzzleProvider.TryGetMuzzle(out muzzlePos, out muzzleRot))
            {
                // Hybrid rigs render a separate presentation weapon. Its muzzle
                // must win so tracers originate from the barrel visible on screen.
            }
            else if (muzzlePoint != null)
            {
                muzzlePos = muzzlePoint.position;
                muzzleRot = muzzlePoint.rotation;
            }
            else
            {
                muzzlePos = transform.position;
                muzzleRot = transform.rotation;
            }

            var request = new WeaponShotRequest(
                weaponSettings.ballistics,
                transform.root.gameObject,
                gameObject,
                muzzlePos,
                muzzleRot,
                _combatAimCamera.transform.position,
                _combatAimCamera.transform.forward);

            // Route through the shot router if one is available on the player root. The router
            // decides whether to resolve locally (offline), send a network command (owner), or
            // drop the shot (proxy). When no router is present, fall back to direct local
            // resolution so the offline CAS/Tactical rig remains functional without networking.
            if (_shotRouter != null)
            {
                ushort weaponId = _networkWeaponId;
                _shotRouter.SubmitShot(
                    request,
                    weaponId,
                    shotSequence: 0,
                    networkTick: _networkTick,
                    aimYaw: _combatAimCamera.transform.eulerAngles.y,
                    aimPitch: _combatAimCamera.transform.eulerAngles.x,
                    isAiming: _isAimingForRouter);
            }
            else
            {
                _combatRuntime.SubmitShot(request);
            }
        }
    }
}
