using System;
using System.Collections.Generic;
using CAS_Demo.Scripts.FPS;
using FPSProject.Combat.Runtime;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FirstPersonProject.Integrations.Kinemation
{
    /// <summary>
    /// Copies the finished CAS leg pose, camera pitch, and resolved locomotion gait onto the
    /// Tactical presentation rig and mirrors discrete weapon actions. Tactical keeps ownership
    /// of its upper-body pose and hand IK, while CAS remains the sole owner of input, movement,
    /// look, and camera control.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CasTacticalPlayerBridge : MonoBehaviour, IWeaponMuzzleProvider
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

        private sealed class LocalTransformBindPose
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
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

        private static readonly string[] DebugLeftLegPaths =
        {
            "root",
            "root/pelvis",
            "root/pelvis/thigh_l",
            "root/pelvis/thigh_l/calf_l",
            "root/pelvis/thigh_l/calf_l/foot_l",
            "root/pelvis/thigh_l/calf_l/foot_l/ball_l"
        };

        private static readonly string[] DebugRightLegPaths =
        {
            "root",
            "root/pelvis",
            "root/pelvis/thigh_r",
            "root/pelvis/thigh_r/calf_r",
            "root/pelvis/thigh_r/calf_r/foot_r",
            "root/pelvis/thigh_r/calf_r/foot_r/ball_r"
        };

        private static readonly string[] DebugComparisonPaths =
        {
            "root",
            "root/pelvis",
            "root/pelvis/thigh_l",
            "root/pelvis/thigh_l/calf_l",
            "root/pelvis/thigh_l/calf_l/foot_l",
            "root/pelvis/thigh_l/calf_l/foot_l/ball_l",
            "root/pelvis/thigh_r",
            "root/pelvis/thigh_r/calf_r",
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

        [Header("Presentation response")]
        [SerializeField, Min(0f)] private float tacticalGaitBlendSpeed = 6f;
        [SerializeField, Min(0f)] private float tacticalAimSpeedMultiplier = 1f;

        [Header("Skeleton comparison debug")]
        [SerializeField] private bool drawSkeletonComparison;
        [SerializeField] private bool drawSkeletonLabels = true;
        [SerializeField, Min(0.001f)] private float debugJointRadius = 0.015f;
        [SerializeField, Min(0f)] private float debugMismatchTolerance = 0.015f;
        [SerializeField] private Color casDebugColor = Color.cyan;
        [SerializeField] private Color tacticalDebugColor = Color.magenta;
        [SerializeField] private Color mismatchDebugColor = new Color(1f, 0.55f, 0f, 1f);

        private readonly List<BoneLink> _boneLinks = new List<BoneLink>();
        private readonly List<LocalTransformBindPose> _tacticalIkBindPose
            = new List<LocalTransformBindPose>();

        private LowerBodyFrameLink _rootLink;
        private LowerBodyFrameLink _pelvisLink;
        private Transform _tacticalSpine;
        private Transform _tacticalWeaponIkRoot;
        private float _lowerBodyTranslationScale = 1f;
        private bool _tacticalAimState;
        private bool _reportedInvalidPresentationState;

        private void Awake()
        {
            ResolveReferences();
            BuildBoneLinks();

            if (tacticalSkeleton != null)
            {
                _tacticalSpine = tacticalSkeleton.Find("root/pelvis/spine_01");
            }

            Transform ikHandGun = tacticalAnimation == null ? null : tacticalAnimation.bones.ikHandGun;
            _tacticalWeaponIkRoot = ikHandGun == null ? null : ikHandGun.parent;
            CacheTacticalIkBindPose(_tacticalWeaponIkRoot);
            CacheTacticalIkBindPose(ikHandGun);
            if (tacticalAnimation != null)
            {
                CacheTacticalIkBindPose(tacticalAnimation.bones.ikRightHand);
                CacheTacticalIkBindPose(tacticalAnimation.bones.ikLeftHand);
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

        private void OnDrawGizmos()
        {
            if (!drawSkeletonComparison || casSkeleton == null || tacticalSkeleton == null) return;

            float radius = Mathf.Max(0.001f, debugJointRadius);
            DrawDebugSkeleton(casSkeleton, casDebugColor, radius);
            DrawDebugSkeleton(tacticalSkeleton, tacticalDebugColor, radius);
            DrawPoseMismatchLines(radius);
            DrawFootWidthComparison(radius);
        }

        private static void DrawDebugSkeleton(Transform skeleton, Color color, float radius)
        {
            DrawDebugChain(skeleton, DebugLeftLegPaths, color, radius);
            DrawDebugChain(skeleton, DebugRightLegPaths, color, radius);
        }

        private static void DrawDebugChain(Transform skeleton, string[] paths, Color color,
            float radius)
        {
            Transform previous = null;
            Gizmos.color = color;
            foreach (string path in paths)
            {
                Transform current = skeleton.Find(path);
                if (current == null)
                {
                    previous = null;
                    continue;
                }

                Gizmos.DrawSphere(current.position, radius);
                if (previous != null) Gizmos.DrawLine(previous.position, current.position);
                previous = current;
            }
        }

        private void DrawPoseMismatchLines(float radius)
        {
            float tolerance = Mathf.Max(0f, debugMismatchTolerance);
            Gizmos.color = mismatchDebugColor;
            foreach (string path in DebugComparisonPaths)
            {
                Transform source = casSkeleton.Find(path);
                Transform target = tacticalSkeleton.Find(path);
                if (source == null || target == null
                    || Vector3.Distance(source.position, target.position) <= tolerance)
                {
                    continue;
                }

                Gizmos.DrawLine(source.position, target.position);
                Gizmos.DrawWireSphere(target.position, radius * 1.5f);
            }
        }

        private void DrawFootWidthComparison(float radius)
        {
            Transform casLeft = casSkeleton.Find("root/pelvis/thigh_l/calf_l/foot_l");
            Transform casRight = casSkeleton.Find("root/pelvis/thigh_r/calf_r/foot_r");
            Transform tacticalLeft = tacticalSkeleton.Find("root/pelvis/thigh_l/calf_l/foot_l");
            Transform tacticalRight = tacticalSkeleton.Find("root/pelvis/thigh_r/calf_r/foot_r");
            if (casLeft == null || casRight == null || tacticalLeft == null || tacticalRight == null)
            {
                return;
            }

            Gizmos.color = casDebugColor;
            Gizmos.DrawLine(casLeft.position, casRight.position);
            Gizmos.DrawWireSphere(casLeft.position, radius * 2f);
            Gizmos.DrawWireSphere(casRight.position, radius * 2f);

            Gizmos.color = tacticalDebugColor;
            Gizmos.DrawLine(tacticalLeft.position, tacticalRight.position);
            Gizmos.DrawWireSphere(tacticalLeft.position, radius * 2f);
            Gizmos.DrawWireSphere(tacticalRight.position, radius * 2f);

#if UNITY_EDITOR
            if (!drawSkeletonLabels) return;

            Vector3 lateralAxis = transform.right;
            float casWidth = Mathf.Abs(Vector3.Dot(casRight.position - casLeft.position,
                lateralAxis));
            float tacticalWidth = Mathf.Abs(Vector3.Dot(
                tacticalRight.position - tacticalLeft.position, lateralAxis));
            Vector3 labelPosition = (casLeft.position + casRight.position
                + tacticalLeft.position + tacticalRight.position) * 0.25f;
            labelPosition += Vector3.up * Mathf.Max(0.08f, radius * 5f);
            float gait = casController == null ? 0f : casController.Gait;

            Handles.color = Color.white;
            Handles.Label(labelPosition,
                $"CAS {casWidth:F3} m | Tactical {tacticalWidth:F3} m | "
                + $"Delta {tacticalWidth - casWidth:+0.000;-0.000;0.000} m | Gait {gait:F2}");
#endif
        }

        private void CacheTacticalIkBindPose(Transform target)
        {
            if (target == null) return;

            _tacticalIkBindPose.Add(new LocalTransformBindPose
            {
                transform = target,
                localPosition = target.localPosition,
                localRotation = target.localRotation
            });
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
                tacticalPlayer.SetExternalAimSpeedMultiplier(tacticalAimSpeedMultiplier);
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
                    MapTacticalGait(casController.Gait,
                        casController.IsAiming && !isSprinting, isSprinting),
                    tacticalGaitBlendSpeed);
            }
        }

        private static float MapTacticalGait(float casGait, bool isAiming, bool isSprinting)
        {
            casGait = Mathf.Clamp(casGait, 0f, 3f);
            float tacticalGait = casGait <= 2f ? casGait * 0.5f : casGait - 1f;

            // Physical velocity still decays through CAS, but Tactical values above one are
            // presentation-only sprint poses. Exit that pose immediately with sprint intent.
            return isAiming || !isSprinting ? Mathf.Min(tacticalGait, 1f) : tacticalGait;
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

        /// <summary>
        /// Uses the active Tactical presentation weapon's muzzle so combat VFX
        /// originate from the barrel the player actually sees. The CAS weapon
        /// prop remains the fallback when no Tactical muzzle is available.
        /// </summary>
        public bool TryGetMuzzle(out Vector3 position, out Quaternion rotation)
        {
            TacticalShooterWeapon weapon = TryGetTacticalWeapon();
            Transform muzzleTransform = weapon == null ? null : weapon.MuzzleTransform;
            if (muzzleTransform == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            position = muzzleTransform.position;
            rotation = muzzleTransform.rotation;
            return IsFinite(position) && IsFinite(rotation);
        }

        private void LateUpdate()
        {
            if (!HasFinitePresentationInputs())
            {
                RecoverInvalidTacticalIkPose(
                    "Tactical IK or CAS retarget input became non-finite before presentation.");
                return;
            }

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

            if (!RetargetLowerBodyFrame(_rootLink) || !RetargetLowerBodyFrame(_pelvisLink))
            {
                RecoverInvalidTacticalIkPose("Lower-body retarget calculation became non-finite.");
                return;
            }

            foreach (BoneLink link in _boneLinks)
            {
                if (!RetargetRotation(link))
                {
                    RecoverInvalidTacticalIkPose("Lower-body rotation retarget became non-finite.");
                    return;
                }
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
                Vector3 restoredIkPosition = tacticalWeaponIkRootPosition + upperBodyTranslation;
                if (!IsFinite(upperBodyTranslation) || !IsFinite(restoredIkPosition))
                {
                    RecoverInvalidTacticalIkPose(
                        "Tactical upper-body translation became non-finite.");
                    return;
                }

                _tacticalWeaponIkRoot.position = restoredIkPosition;
                _tacticalWeaponIkRoot.rotation = tacticalWeaponIkRootRotation;
            }

            _reportedInvalidPresentationState = false;
        }

        private bool HasFinitePresentationInputs()
        {
            if (!float.IsFinite(_lowerBodyTranslationScale) || !HasFiniteTacticalIkPose())
            {
                return false;
            }

            if (_tacticalSpine != null
                && (!IsFinite(_tacticalSpine.position) || !IsFinite(_tacticalSpine.rotation)))
            {
                return false;
            }

            if (!HasFiniteLowerBodyFrame(_rootLink) || !HasFiniteLowerBodyFrame(_pelvisLink))
            {
                return false;
            }

            foreach (BoneLink link in _boneLinks)
            {
                if (link == null || link.source == null || link.target == null
                    || !IsFinite(link.source.localRotation)
                    || !IsFinite(link.sourceBindLocalRotation)
                    || !IsFinite(link.targetBindLocalRotation))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasFiniteLowerBodyFrame(LowerBodyFrameLink link)
        {
            return link == null || link.source != null && link.target != null
                && IsFinite(link.source.localPosition)
                && IsFinite(link.source.localRotation)
                && IsFinite(link.sourceBindLocalPosition)
                && IsFinite(link.sourceBindLocalRotation)
                && IsFinite(link.targetBindLocalPosition)
                && IsFinite(link.targetBindLocalRotation);
        }

        private bool HasFiniteTacticalIkPose()
        {
            foreach (LocalTransformBindPose bindPose in _tacticalIkBindPose)
            {
                if (bindPose.transform == null
                    || !IsFinite(bindPose.transform.localPosition)
                    || !IsFinite(bindPose.transform.localRotation)
                    || !IsFinite(bindPose.transform.position)
                    || !IsFinite(bindPose.transform.rotation))
                {
                    return false;
                }
            }

            return true;
        }

        private void RecoverInvalidTacticalIkPose(string reason)
        {
            foreach (LocalTransformBindPose bindPose in _tacticalIkBindPose)
            {
                if (bindPose.transform == null) continue;
                bindPose.transform.localPosition = bindPose.localPosition;
                bindPose.transform.localRotation = bindPose.localRotation;
            }

            if (_reportedInvalidPresentationState) return;

            Debug.LogError($"[{nameof(CasTacticalPlayerBridge)}] {reason} "
                + "Restored the Tactical IK bind pose and skipped this presentation frame.", this);
            _reportedInvalidPresentationState = true;
        }

        private bool RetargetLowerBodyFrame(LowerBodyFrameLink link)
        {
            if (link == null) return true;

            Vector3 sourcePositionDelta = link.source.localPosition - link.sourceBindLocalPosition;
            Vector3 targetPosition = link.targetBindLocalPosition
                + sourcePositionDelta * _lowerBodyTranslationScale;

            Quaternion localAnimationDelta = Quaternion.Inverse(link.sourceBindLocalRotation)
                * link.source.localRotation;
            Quaternion targetRotation = link.targetBindLocalRotation * localAnimationDelta;
            if (!IsFinite(targetPosition) || !IsFinite(targetRotation)) return false;

            link.target.localPosition = targetPosition;
            link.target.localRotation = targetRotation;
            return true;
        }

        private bool RetargetRotation(BoneLink link)
        {
            // Transfer each joint's animation relative to its own bind pose. Folding pelvis yaw
            // into every thigh independently makes the legs rotate around separate hip sockets,
            // which crosses and twists the feet during crouched turn-in-place animations.
            Quaternion localAnimationDelta = Quaternion.Inverse(link.sourceBindLocalRotation)
                * link.source.localRotation;
            Quaternion targetRotation = link.targetBindLocalRotation * localAnimationDelta;
            if (!IsFinite(targetRotation)) return false;

            link.target.localRotation = targetRotation;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y)
                || !float.IsFinite(value.z) || !float.IsFinite(value.w))
            {
                return false;
            }

            float magnitudeSquared = value.x * value.x + value.y * value.y
                + value.z * value.z + value.w * value.w;
            return magnitudeSquared > Mathf.Epsilon;
        }

        private void OnDisable()
        {
            if (tacticalPlayer != null) tacticalPlayer.ReleaseExternalGait();

            StopTacticalFiring();
        }
    }
}
