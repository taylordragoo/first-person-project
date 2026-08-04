using System;
using System.Collections.Generic;
using CAS_Demo.Scripts.FPS;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonProject.Integrations.Kinemation
{
    /// <summary>
    /// Copies the finished CAS leg pose, camera pitch, and resolved locomotion gait onto the
    /// Tactical presentation rig and mirrors discrete weapon actions. Tactical keeps ownership
    /// of its upper-body pose and hand IK, while CAS remains the sole owner of input, movement,
    /// look, and camera control.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CasTacticalPlayerBridge : MonoBehaviour
    {
        [Serializable]
        private sealed class BoneLink
        {
            public Transform source;
            public Transform target;
            public Quaternion sourceBindLocalRotation;
            public Quaternion targetBindLocalRotation;
        }

        private sealed class LowerBodyFrameLink
        {
            public Transform source;
            public Transform target;
            public Vector3 sourceBindLocalPosition;
            public Vector3 targetBindLocalPosition;
            public Quaternion sourceBindLocalRotation;
            public Quaternion targetBindLocalRotation;
        }

        private static readonly string[] PoseBonePaths =
        {
            "root/pelvis/thigh_l",
            "root/pelvis/thigh_l/thigh_twist_01_l",
            "root/pelvis/thigh_l/calf_l",
            "root/pelvis/thigh_l/calf_l/calf_twist_01_l",
            "root/pelvis/thigh_l/calf_l/foot_l",
            "root/pelvis/thigh_l/calf_l/foot_l/ball_l",
            "root/pelvis/thigh_r",
            "root/pelvis/thigh_r/thigh_twist_01_r",
            "root/pelvis/thigh_r/calf_r",
            "root/pelvis/thigh_r/calf_r/calf_twist_01_r",
            "root/pelvis/thigh_r/calf_r/foot_r",
            "root/pelvis/thigh_r/calf_r/foot_r/ball_r"
        };

        [Header("CAS pose source")]
        [SerializeField] private FPSExampleController casController;
        [SerializeField] private CharacterCamera casCamera;
        [SerializeField] private Transform casSkeleton;

        [Header("Tactical presentation")]
        [SerializeField] private TacticalShooterPlayer tacticalPlayer;
        [SerializeField] private TacticalProceduralAnimation tacticalAnimation;
        [SerializeField] private Transform tacticalSkeleton;

        [Header("Crouch presentation")]
        [SerializeField, Min(0.01f)] private float crouchHeightSmoothTime = 0.06f;

        private readonly List<BoneLink> _boneLinks = new List<BoneLink>();

        private LowerBodyFrameLink _rootLink;
        private LowerBodyFrameLink _pelvisLink;
        private Transform _tacticalPresentationRoot;
        private Transform _tacticalSpine;
        private Transform _tacticalWeaponIkRoot;
        private Transform _tacticalLeftFoot;
        private Transform _tacticalRightFoot;
        private Vector3 _tacticalPresentationBindPosition;
        private float _lowerBodyTranslationScale = 1f;
        private float _standingTacticalFootHeight;
        private float _presentationHeightOffset;
        private float _presentationHeightVelocity;
        private bool _hasStandingTacticalFootHeight;
        private bool _tacticalAimState;

        private void Awake()
        {
            ResolveReferences();
            BuildBoneLinks();

            _tacticalPresentationRoot = tacticalPlayer == null ? null : tacticalPlayer.transform;
            if (tacticalSkeleton != null)
            {
                _tacticalSpine = tacticalSkeleton.Find("root/pelvis/spine_01");
                _tacticalLeftFoot = tacticalSkeleton.Find("root/pelvis/thigh_l/calf_l/foot_l");
                _tacticalRightFoot = tacticalSkeleton.Find("root/pelvis/thigh_r/calf_r/foot_r");
            }

            Transform ikHandGun = tacticalAnimation == null ? null : tacticalAnimation.bones.ikHandGun;
            _tacticalWeaponIkRoot = ikHandGun == null ? null : ikHandGun.parent;

            if (_tacticalPresentationRoot != null)
            {
                _tacticalPresentationBindPosition = _tacticalPresentationRoot.localPosition;
            }
        }

        private void ResolveReferences()
        {
            if (casController == null) casController = GetComponent<FPSExampleController>();
            if (casCamera == null) casCamera = GetComponentInChildren<CharacterCamera>(true);
            if (tacticalPlayer == null) tacticalPlayer = GetComponentInChildren<TacticalShooterPlayer>(true);
            if (tacticalAnimation == null)
            {
                tacticalAnimation = GetComponentInChildren<TacticalProceduralAnimation>(true);
            }
        }

        private void Update()
        {
            if (casCamera != null && tacticalAnimation != null)
            {
                // CAS has already decided the view direction. This value only poses Tactical's
                // upper body so its weapon follows that CAS-controlled camera; it never drives
                // CAS input, character rotation, or the camera itself.
                tacticalAnimation.pitchInput = casCamera.pitchInput;
            }

            if (casController != null && tacticalPlayer != null)
            {
                bool isSprinting = casController.IsSprinting;
                if (isSprinting)
                {
                    SetTacticalAim(false);
                    StopTacticalFiring();
                }

                // Drive only Tactical's presentation blend tree from CAS's resolved movement
                // speed. Raw MoveInput is intentionally not mirrored, and Tactical never moves
                // or rotates the character. CAS's normal jog tops out at gait 2, which must map
                // to Tactical gait 1; Tactical uses values above 1 to lower the weapon into its
                // sprint pose. ADS never receives that sprint-pose range. Tactical receives the
                // desired presentation gait through its external-gait path so only one system
                // smooths and writes the Animator parameter each frame.
                tacticalPlayer.SetExternalGait(
                    MapTacticalGait(casController.Gait, casController.IsAiming && !isSprinting));
            }
        }

        private static float MapTacticalGait(float casGait, bool isAiming)
        {
            casGait = Mathf.Clamp(casGait, 0f, 3f);
            float tacticalGait = casGait <= 2f ? casGait * 0.5f : casGait - 1f;
            return isAiming ? Mathf.Min(tacticalGait, 1f) : tacticalGait;
        }

        private void BuildBoneLinks()
        {
            _boneLinks.Clear();
            if (casSkeleton == null || tacticalSkeleton == null)
            {
                Debug.LogWarning("CAS/Tactical bridge is missing one of its skeleton roots.", this);
                return;
            }

            _rootLink = CreateLowerBodyFrameLink("root");
            _pelvisLink = CreateLowerBodyFrameLink("root/pelvis");
            _lowerBodyTranslationScale = CalculateLowerBodyTranslationScale();

            foreach (string path in PoseBonePaths)
            {
                BoneLink link = CreateBoneLink(path);
                if (link != null) _boneLinks.Add(link);
            }
        }

        private BoneLink CreateBoneLink(string path)
        {
            Transform source = casSkeleton.Find(path);
            Transform target = tacticalSkeleton.Find(path);
            if (source == null || target == null)
            {
                Debug.LogWarning($"CAS/Tactical bridge could not map pose bone '{path}'.", this);
                return null;
            }

            return new BoneLink
            {
                source = source,
                target = target,
                sourceBindLocalRotation = source.localRotation,
                targetBindLocalRotation = target.localRotation
            };
        }

        private LowerBodyFrameLink CreateLowerBodyFrameLink(string path)
        {
            Transform source = casSkeleton.Find(path);
            Transform target = tacticalSkeleton.Find(path);
            if (source == null || target == null)
            {
                Debug.LogWarning($"CAS/Tactical bridge could not map lower-body frame '{path}'.", this);
                return null;
            }

            return new LowerBodyFrameLink
            {
                source = source,
                target = target,
                sourceBindLocalPosition = source.localPosition,
                targetBindLocalPosition = target.localPosition,
                sourceBindLocalRotation = source.localRotation,
                targetBindLocalRotation = target.localRotation
            };
        }

        private float CalculateLowerBodyTranslationScale()
        {
            Transform sourceCalf = casSkeleton.Find("root/pelvis/thigh_l/calf_l");
            Transform sourceFoot = casSkeleton.Find("root/pelvis/thigh_l/calf_l/foot_l");
            Transform targetCalf = tacticalSkeleton.Find("root/pelvis/thigh_l/calf_l");
            Transform targetFoot = tacticalSkeleton.Find("root/pelvis/thigh_l/calf_l/foot_l");
            if (sourceCalf == null || sourceFoot == null || targetCalf == null || targetFoot == null)
            {
                return 1f;
            }

            float sourceLegLength = sourceCalf.localPosition.magnitude + sourceFoot.localPosition.magnitude;
            float targetLegLength = targetCalf.localPosition.magnitude + targetFoot.localPosition.magnitude;
            return sourceLegLength > Mathf.Epsilon ? targetLegLength / sourceLegLength : 1f;
        }

        public void OnAim(InputValue value)
        {
            bool wantsToAim = value.isPressed
                && (casController == null || !casController.IsSprinting);
            SetTacticalAim(wantsToAim);
        }

        private void SetTacticalAim(bool wantsToAim)
        {
            if (wantsToAim == _tacticalAimState) return;

            if (tacticalPlayer != null) tacticalPlayer.OnAim();
            _tacticalAimState = wantsToAim;
        }

        // These callbacks are delivered by the existing CAS PlayerInput in Send Messages mode.
        // They mirror CAS actions into the Tactical presentation without introducing another
        // PlayerInput or movement/camera controller.
        public void OnUseItem(InputValue value)
        {
            if (casController == null) return;
            if (!casController.isFirstPerson && !casController.IsAiming) return;

            TacticalShooterWeapon weapon = TryGetTacticalWeapon();
            if (casController.IsSprinting)
            {
                if (weapon != null && weapon.IsFiring) weapon.StopFiring();
                return;
            }

            if (weapon == null || value.isPressed == weapon.IsFiring) return;

            if (value.isPressed) weapon.StartFiring();
            else weapon.StopFiring();
        }

        private void StopTacticalFiring()
        {
            TacticalShooterWeapon weapon = TryGetTacticalWeapon();
            if (weapon != null && weapon.IsFiring) weapon.StopFiring();
        }

        public void OnReload()
        {
            if (tacticalPlayer != null) tacticalPlayer.OnReload();
        }

        public void OnChangeItem()
        {
            if (tacticalPlayer != null) tacticalPlayer.OnChangeWeapon();
        }

        public void OnChangeFiremode()
        {
            if (tacticalPlayer != null) tacticalPlayer.OnChangeFireMode();
        }

        private TacticalShooterWeapon TryGetTacticalWeapon()
        {
            if (tacticalPlayer == null) return null;

            try
            {
                return tacticalPlayer.GetActiveWeapon();
            }
            catch (NullReferenceException)
            {
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private void LateUpdate()
        {
            // CAS root/pelvis motion contains directional blend, locomotion lean, stride bob, and
            // turn-in-place compensation. Keep Tactical's finished upper-body orientation, then
            // shift its separate weapon/IK frame by the same translation the pelvis gives the
            // spine. This keeps the hands and gun together without pinning the spine position and
            // feeding distortion back into the lower-body humanoid pose.
            Vector3 tacticalSpinePosition = _tacticalSpine == null
                ? Vector3.zero
                : _tacticalSpine.position;
            Quaternion tacticalSpineRotation = _tacticalSpine == null
                ? Quaternion.identity
                : _tacticalSpine.rotation;
            Vector3 tacticalWeaponIkRootPosition = _tacticalWeaponIkRoot == null
                ? Vector3.zero
                : _tacticalWeaponIkRoot.position;
            Quaternion tacticalWeaponIkRootRotation = _tacticalWeaponIkRoot == null
                ? Quaternion.identity
                : _tacticalWeaponIkRoot.rotation;

            RetargetLowerBodyFrame(_rootLink);
            RetargetLowerBodyFrame(_pelvisLink);

            foreach (BoneLink link in _boneLinks)
            {
                RetargetRotation(link);
                // Local positions intentionally remain Tactical-authored so its bone lengths
                // are never replaced by the differently proportioned CAS skeleton.
            }

            if (_tacticalSpine != null)
            {
                _tacticalSpine.rotation = tacticalSpineRotation;
            }

            if (_tacticalWeaponIkRoot != null)
            {
                Vector3 upperBodyTranslation = _tacticalSpine == null
                    ? Vector3.zero
                    : _tacticalSpine.position - tacticalSpinePosition;
                _tacticalWeaponIkRoot.position = tacticalWeaponIkRootPosition + upperBodyTranslation;
                _tacticalWeaponIkRoot.rotation = tacticalWeaponIkRootRotation;
            }

            UpdateCrouchPresentationHeight();
        }

        private void RetargetLowerBodyFrame(LowerBodyFrameLink link)
        {
            if (link == null) return;

            Vector3 sourcePositionDelta = link.source.localPosition - link.sourceBindLocalPosition;
            link.target.localPosition = link.targetBindLocalPosition
                + sourcePositionDelta * _lowerBodyTranslationScale;

            Quaternion localAnimationDelta = Quaternion.Inverse(link.sourceBindLocalRotation)
                * link.source.localRotation;
            link.target.localRotation = link.targetBindLocalRotation * localAnimationDelta;
        }

        private void UpdateCrouchPresentationHeight()
        {
            if (casController == null || _tacticalPresentationRoot == null
                || !TryGetLowestTacticalFootHeight(out float currentFootHeight)) return;

            // Measure the authored standing contact once the Animator has evaluated. Bone pivots
            // are not at the boot soles, but matching the same foot-pivot height in both poses
            // reproduces the standing mesh's known-good ground contact.
            bool canCalibrateStandingContact = !casController.IsCrouching
                && casController.Gait <= 0.01f
                && Mathf.Abs(_presentationHeightOffset) <= 0.005f;
            if (canCalibrateStandingContact && (!_hasStandingTacticalFootHeight
                || currentFootHeight < _standingTacticalFootHeight))
            {
                _standingTacticalFootHeight = currentFootHeight;
                _hasStandingTacticalFootHeight = true;
            }

            if (!_hasStandingTacticalFootHeight) return;

            // Remove the root offset already applied on the previous frame before calculating the
            // next target; otherwise the correction feeds back into itself. The lower of the two
            // feet is the planted foot during crouch locomotion and turn-in-place animation.
            float uncorrectedFootHeight = currentFootHeight - _presentationHeightOffset;
            float targetOffset = casController.IsCrouching
                ? Mathf.Min(0f, _standingTacticalFootHeight - uncorrectedFootHeight)
                : 0f;
            _presentationHeightOffset = Mathf.SmoothDamp(_presentationHeightOffset, targetOffset,
                ref _presentationHeightVelocity, crouchHeightSmoothTime);

            Vector3 localPosition = _tacticalPresentationBindPosition;
            localPosition.y += _presentationHeightOffset;
            _tacticalPresentationRoot.localPosition = localPosition;
        }

        private bool TryGetLowestTacticalFootHeight(out float height)
        {
            height = 0f;
            if (_tacticalLeftFoot == null || _tacticalRightFoot == null) return false;

            float leftHeight = casController.transform
                .InverseTransformPoint(_tacticalLeftFoot.position).y;
            float rightHeight = casController.transform
                .InverseTransformPoint(_tacticalRightFoot.position).y;
            height = Mathf.Min(leftHeight, rightHeight);
            return true;
        }

        private void RetargetRotation(BoneLink link)
        {
            // Transfer each joint's animation relative to its own bind pose. Folding pelvis yaw
            // into every thigh independently makes the legs rotate around separate hip sockets,
            // which crosses and twists the feet during crouched turn-in-place animations.
            Quaternion localAnimationDelta = Quaternion.Inverse(link.sourceBindLocalRotation)
                * link.source.localRotation;
            link.target.localRotation = link.targetBindLocalRotation * localAnimationDelta;
        }

        private void OnDisable()
        {
            if (tacticalPlayer != null) tacticalPlayer.ReleaseExternalGait();

            if (_tacticalPresentationRoot != null)
            {
                _tacticalPresentationRoot.localPosition = _tacticalPresentationBindPosition;
            }

            StopTacticalFiring();
        }
    }
}
