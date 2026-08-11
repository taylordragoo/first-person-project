using System.Collections;
using FirstPersonProject.Integrations.Kinemation.Presentation;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Movement;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Drives the unchanged CAS/Tactical player presentation from bot locomotion. The bot always
    /// uses the controller's remote-proxy path, so no player input, camera, cursor, or character
    /// motor is allowed to own the root transform.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(NavMeshAgent))]
    public sealed class BotCasPresentationAdapter : NetworkBehaviour
    {
        private const int GroundHitCapacity = 8;
        private static readonly Vector2[] GroundProbeOffsets =
        {
            Vector2.zero,
            Vector2.right,
            Vector2.left,
            Vector2.up,
            Vector2.down,
            new Vector2(0.7071068f, 0.7071068f),
            new Vector2(-0.7071068f, 0.7071068f),
            new Vector2(0.7071068f, -0.7071068f),
            new Vector2(-0.7071068f, -0.7071068f)
        };

        [Header("Presentation")]
        [SerializeField] private NetworkFPSExampleController controller;
        [SerializeField] private TacticalProceduralAnimation tacticalAnimation;
        [SerializeField] private NetworkTacticalShooterPlayer tacticalPlayer;

        [Header("World observation")]
        [SerializeField] private LayerMask groundMask = 1;
        [SerializeField, Min(0f)] private float groundProbeDistance = 0.25f;
        [SerializeField, Range(0.1f, 1f)] private float groundProbeRadiusFactor = 0.9f;
        [SerializeField, Range(0f, 89f)] private float maxStableSlopeAngle = 60f;
        [SerializeField, Min(0f)] private float fallDelay = 0.1f;
        [SerializeField, Min(0f)] private float movingThreshold = 0.1f;
        [SerializeField, Min(0f)] private float velocitySmoothing = 12f;

        private NavMeshAgent _agent;
        private CapsuleCollider _capsuleCollider;
        private NetworkHealth _networkHealth;
        private Animator _tacticalAnimator;
        private Coroutine _initialization;
        private Vector3 _previousPosition;
        private Vector3 _smoothedVelocity;
        private readonly RaycastHit[] _groundHits = new RaycastHit[GroundHitCapacity];
        private float _timeSincePhysicalGrounded;
        private bool _hasPreviousPosition;
        private bool _restoreAnimatorPending;
        private bool _restoreAnimatorEnabled;
        private bool _restoreGlobalsPending;
        private bool _restoreCursorVisible;
        private CursorLockMode _restoreCursorLock;
        private int _restoreTargetFrameRate;

        public NetworkFPSExampleController Controller => controller;
        public bool IsPresentationReady { get; private set; }

        private void Awake()
        {
            ResolveReferences();
            DisableOwnerOnlyComponents();

            // Vendor Start methods must not race the staged initialization below.
            if (tacticalAnimation != null) tacticalAnimation.enabled = false;
            if (tacticalPlayer != null) tacticalPlayer.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            ResolveReferences();
            DisableOwnerOnlyComponents();

            StopInitialization();
            if (!PrepareControllerForSpawn()) return;
            _previousPosition = transform.position;
            _hasPreviousPosition = true;

            _initialization = StartCoroutine(InitializeTacticalPresentation());
        }

        private bool PrepareControllerForSpawn()
        {
            if (controller == null)
            {
                Debug.LogError($"[{nameof(BotCasPresentationAdapter)}] Missing network CAS controller.", this);
                return false;
            }

            controller.InitializeAsProxy();
            // InitializeAsProxy is intentionally one-shot. A pooled/despawned bot therefore
            // needs its mode and edge detector restored explicitly on every later spawn.
            controller.SetSimulationMode(PlayerSimulationMode.RemoteProxy);
            controller.ResetProxyState();
            return true;
        }

        private void Update()
        {
            if (!IsSpawned || controller == null
                || controller.SimulationMode != PlayerSimulationMode.RemoteProxy)
            {
                return;
            }

            Vector3 velocity = ReadWorldVelocity();
            velocity.y = 0f;
            float alpha = 1f - Mathf.Exp(-velocitySmoothing * Time.deltaTime);
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, velocity, alpha);

            float speed = _smoothedVelocity.magnitude;
            bool isMoving = speed > movingThreshold;
            Vector3 localVelocity = transform.InverseTransformDirection(_smoothedVelocity);
            Vector2 move = isMoving && speed > Mathf.Epsilon
                ? new Vector2(localVelocity.x, localVelocity.z) / speed
                : Vector2.zero;

            bool isGrounded = ResolvePresentationGrounded(
                ProbePhysicalGrounded(), Time.deltaTime);
            bool isAlive = _networkHealth == null || !_networkHealth.IsDead;
            controller.ApplySharedPresentationWithoutRootMotion(new LocomotionPresentationInput
            {
                MoveAxes = move,
                GaitSource = GaitSource.ObservedPlanarSpeed,
                ObservedPlanarSpeed = speed,
                IsMoving = isMoving,
                IsGrounded = isGrounded,
                IsInAir = !isGrounded,
                IsCrouching = false,
                IsSprinting = false,
                IsAiming = false,
                IsAlive = isAlive,
                IsFirstPerson = false,
                ViewWeight = 0f,
                AimingWeight = 0f
            }, Time.deltaTime);
            _previousPosition = transform.position;
            _hasPreviousPosition = true;
        }

        private Vector3 ReadWorldVelocity()
        {
            if (IsServer && _agent != null && _agent.enabled && _agent.isOnNavMesh)
                return _agent.velocity;

            if (!_hasPreviousPosition || Time.deltaTime <= Mathf.Epsilon)
                return Vector3.zero;

            return (transform.position - _previousPosition) / Time.deltaTime;
        }

        private bool ProbePhysicalGrounded()
        {
            if (_capsuleCollider == null || !_capsuleCollider.enabled) return false;

            Bounds bounds = _capsuleCollider.bounds;
            float probeRadius = Mathf.Max(0.01f,
                Mathf.Min(bounds.extents.x, bounds.extents.z) * groundProbeRadiusFactor);
            Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Vector3 originBase = bottomCenter + Vector3.up * groundProbeDistance;
            float castDistance = groundProbeDistance * 2f;

            foreach (Vector2 offset in GroundProbeOffsets)
            {
                Vector3 origin = originBase + transform.right * (offset.x * probeRadius)
                    + transform.forward * (offset.y * probeRadius);
                int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits,
                    castDistance, groundMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = _groundHits[i];
                    if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                        continue;
                    if (Vector3.Angle(hit.normal, transform.up) <= maxStableSlopeAngle)
                        return true;
                }
            }

            return false;
        }

        private bool ResolvePresentationGrounded(bool hasPhysicalGround, float deltaTime)
        {
            if (hasPhysicalGround)
            {
                _timeSincePhysicalGrounded = 0f;
                return true;
            }

            _timeSincePhysicalGrounded += Mathf.Max(0f, deltaTime);
            return _timeSincePhysicalGrounded < fallDelay;
        }

        private IEnumerator InitializeTacticalPresentation()
        {
            IsPresentationReady = false;
            bool completed = false;
            try
            {
                if (tacticalAnimation == null || tacticalPlayer == null
                    || _tacticalAnimator == null)
                {
                    Debug.LogError($"[{nameof(BotCasPresentationAdapter)}] Tactical presentation is incomplete.", this);
                    yield break;
                }

                tacticalAnimation.enabled = false;
                tacticalPlayer.enabled = false;

                // Match NetworkCasPlayer staging: CAS evaluates first, then procedural playable,
                // then weapon creation/settings while Tactical Animator stays paused.
                yield return null;
                if (!IsSpawned) yield break;

                CaptureAnimatorState();
                _tacticalAnimator.enabled = false;

                tacticalAnimation.enabled = true;
                yield return null;
                if (!IsSpawned) yield break;

                CaptureGlobalDemoState();
                tacticalPlayer.enabled = true;
                yield return null;

                if (!IsSpawned) yield break;

                // Weapon prefab was instantiated during TacticalShooterPlayer.Start; neutralize
                // any camera/listener/input components carried by that prefab as well.
                DisableOwnerOnlyComponents();
                completed = true;
            }
            finally
            {
                RestoreTemporaryInitializationState();
                IsPresentationReady = completed;
                _initialization = null;
            }
        }

        private void CaptureAnimatorState()
        {
            if (_restoreAnimatorPending || _tacticalAnimator == null) return;
            _restoreAnimatorEnabled = _tacticalAnimator.enabled;
            _restoreAnimatorPending = true;
        }

        private void CaptureGlobalDemoState()
        {
            if (_restoreGlobalsPending) return;
            _restoreCursorVisible = Cursor.visible;
            _restoreCursorLock = Cursor.lockState;
            _restoreTargetFrameRate = Application.targetFrameRate;
            _restoreGlobalsPending = true;
        }

        private void RestoreTemporaryInitializationState()
        {
            if (_restoreGlobalsPending)
            {
                Cursor.visible = _restoreCursorVisible;
                Cursor.lockState = _restoreCursorLock;
                Application.targetFrameRate = _restoreTargetFrameRate;
                _restoreGlobalsPending = false;
            }

            if (_restoreAnimatorPending)
            {
                if (_tacticalAnimator != null)
                    _tacticalAnimator.enabled = _restoreAnimatorEnabled;
                _restoreAnimatorPending = false;
            }
        }

        private void StopInitialization()
        {
            if (_initialization != null)
            {
                StopCoroutine(_initialization);
                _initialization = null;
            }

            RestoreTemporaryInitializationState();
            IsPresentationReady = false;
        }

        private void ResolveReferences()
        {
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if (_capsuleCollider == null) _capsuleCollider = GetComponent<CapsuleCollider>();
            if (_networkHealth == null) _networkHealth = GetComponent<NetworkHealth>();
            if (controller == null) controller = GetComponent<NetworkFPSExampleController>();
            if (tacticalAnimation == null)
                tacticalAnimation = GetComponentInChildren<TacticalProceduralAnimation>(true);
            if (tacticalPlayer == null)
                tacticalPlayer = GetComponentInChildren<NetworkTacticalShooterPlayer>(true);
            if (_tacticalAnimator == null && tacticalPlayer != null)
                _tacticalAnimator = tacticalPlayer.GetComponentInChildren<Animator>(true);
        }

        private void DisableOwnerOnlyComponents()
        {
            foreach (PlayerInput input in GetComponentsInChildren<PlayerInput>(true))
            {
                input.DeactivateInput();
                input.enabled = false;
            }

            foreach (Camera cameraComponent in GetComponentsInChildren<Camera>(true))
                cameraComponent.enabled = false;
            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
            foreach (CharacterController characterController in
                     GetComponentsInChildren<CharacterController>(true))
            {
                characterController.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            StopInitialization();
            controller?.SetSimulationMode(PlayerSimulationMode.Disabled);
            controller?.ResetProxyState();
            _smoothedVelocity = Vector3.zero;
            _timeSincePhysicalGrounded = 0f;
            _hasPreviousPosition = false;
        }

        private void OnDisable()
        {
            StopInitialization();
            controller?.ResetProxyState();
            _smoothedVelocity = Vector3.zero;
            _timeSincePhysicalGrounded = 0f;
            _previousPosition = transform.position;
            _hasPreviousPosition = false;
            if (tacticalPlayer != null) tacticalPlayer.ReleaseExternalGait();
        }
    }
}
