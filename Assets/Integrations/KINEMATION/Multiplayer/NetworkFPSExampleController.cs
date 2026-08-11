using CAS_Demo.Scripts.FPS;
using FPSProject.Multiplayer.Core.Movement;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using FirstPersonProject.Integrations.Kinemation.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Networked FPS controller adapter. Derives from <see cref="FPSExampleController"/> so the
    /// existing CAS movement implementation is preserved unchanged on the owning client, and adds
    /// three explicit simulation modes so remote proxies and dead players do not run input,
    /// CharacterController.Move, local look, or a gameplay camera.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkFPSExampleController : FPSExampleController
    {
        public PlayerSimulationMode SimulationMode => _simulationMode;
        public bool IsOwnerInitialized => _isOwnerInitialized;

        private PlayerSimulationMode _simulationMode = PlayerSimulationMode.Disabled;
        private bool _isOwnerInitialized;
        private readonly LocomotionPresentationEvaluator _presentationEvaluator
            = new LocomotionPresentationEvaluator();

        // Cached owner-only components so we can disable/re-enable them on spawn/despawn/death.
        private PlayerInput _cachedPlayerInput;
        private Camera _cachedCamera;
        private AudioListener _cachedAudioListener;
        private CharacterCamera _cachedCharacterCamera;
        private RecoilAnimation _cachedRecoilAnimation;
        private bool _didCacheOwnerComponents;

        // The host writes the accepted pose here for the NetworkCasPlayer to broadcast.
        private ProxyPresentationState _acceptedProxyState;
        private bool _hasAcceptedProxyState;

        protected override bool UseExternalLocomotionPresentation => true;
        protected override bool UseExternalAimPresentation => true;

        /// <summary>
        /// Override Awake so the base input-activation path does not run before we know whether
        /// this instance is the owner. We cache the PlayerInput but leave it inactive.
        /// </summary>
        protected override void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            // Intentionally do NOT call _playerInput.ActivateInput() here. NetworkCasPlayer
            // enables input only when IsOwner is true.
        }

        /// <summary>
        /// Override Start so the base controller initialization (which assumes a single-player
        /// scene with a live CharacterController, camera, and cursor lock) does not run before
        /// the network spawn path has decided who we are. NetworkCasPlayer calls
        /// <see cref="InitializeAsOwner"/> or <see cref="InitializeAsProxy"/> instead.
        /// </summary>
        protected override void Start()
        {
            // Intentionally empty. The network spawn path drives initialization.
        }

        /// <summary>
        /// Called by NetworkCasPlayer.OnNetworkSpawn when IsOwner is true. Runs the base Start
        /// path that wires up the CharacterController, Animator, camera, items, and cursor,
        /// then activates input.
        /// </summary>
        public void InitializeAsOwner()
        {
            if (_isOwnerInitialized) return;

            CacheOwnerOnlyComponents();
            EnsureOwnerComponentsEnabled();

            // Run the base Start initialization. This resolves the CharacterController,
            // Animator, CharacterAnimationComponent, ProceduralAnimationComponent, items,
            // CharacterCamera, and cursor state.
            base.Start();
            _presentationEvaluator.Reset();

            // Activate input now that the controller is wired up.
            if (_playerInput != null) _playerInput.ActivateInput();

            _isOwnerInitialized = true;
            SetSimulationMode(PlayerSimulationMode.LocalOwner);
        }

        /// <summary>
        /// Called by NetworkCasPlayer.OnNetworkSpawn for remote proxies. Resolves the Animator
        /// and animation components without activating input, a gameplay camera, or cursor lock.
        /// </summary>
        public void InitializeAsProxy()
        {
            if (_isOwnerInitialized) return;

            CacheOwnerOnlyComponents();
            EnsureOwnerComponentsDisabled();

            // Resolve only the animation-side components the proxy needs. We do NOT call
            // base.Start() because it locks the cursor, activates frame-rate targeting, and
            // assumes the CharacterController is the local player's.
            _animator = GetComponentInChildren<Animator>();
            _characterAnimation = GetComponentInChildren<CharacterAnimationComponent>();
            _proceduralAnimation = GetComponentInChildren<ProceduralAnimationComponent>();
            _controller = GetComponentInChildren<CharacterController>();
            InitializeProxyCasPoseSettings();
            if (_controller != null)
            {
                _originalCenter = _controller.center;
                _originalHeight = _controller.height;
            }

            // The CharacterCamera is kept alive for CAS animation/bridge dependencies, but its
            // Unity Camera and AudioListener are disabled. Proxies never feed it look input.
            _characterCamera = GetComponentInChildren<CharacterCamera>(true);
            if (_characterCamera != null)
            {
                _characterCamera.isFirstPerson = false;
                _characterCamera.pitchInput = 0f;
                _characterCamera.yawInput = transform.eulerAngles.y;
            }

            // Disable the CharacterController on proxies so it does not fight the interpolated
            // transform. The proxy's transform is driven by the interpolation buffer.
            if (_controller != null) _controller.enabled = false;

            _aimRotation = transform.rotation;
            _isOwnerInitialized = true;
            _presentationEvaluator.Reset();
            SetSimulationMode(PlayerSimulationMode.RemoteProxy);
        }

        private void InitializeProxyCasPoseSettings()
        {
            // Local players get this setup from CharacterExampleController.Start. Proxies skip
            // that method to avoid owner-only input/camera/cursor effects, but their CAS graph
            // still needs the same active prop's base pose, overlay, and procedural settings.
            _items.Clear();
            _items.AddRange(GetComponentsInChildren<CasProp>());
            foreach (CasProp item in _items) item.SetVisibility(false);

            CasProp activeItem = GetActiveItem();
            if (activeItem == null) return;

            activeItem.SetVisibility(true);
            if (_characterAnimation != null && activeItem.animationSettings != null)
            {
                _characterAnimation.UpdateAnimationSettings(activeItem.animationSettings);
            }
        }

        /// <summary>
        /// Switch the simulation mode. LocalOwner runs the full CAS Update path; RemoteProxy
        /// applies accepted presentation state; Disabled performs no work.
        /// </summary>
        public void SetSimulationMode(PlayerSimulationMode mode)
        {
            if (_simulationMode == mode) return;

            _simulationMode = mode;
            _presentationEvaluator.Reset();

            switch (mode)
            {
                case PlayerSimulationMode.LocalOwner:
                    EnsureOwnerComponentsEnabled();
                    if (_controller != null) _controller.enabled = true;
                    break;
                case PlayerSimulationMode.RemoteProxy:
                    EnsureOwnerComponentsDisabled();
                    if (_controller != null) _controller.enabled = false;
                    break;
                case PlayerSimulationMode.Disabled:
                    EnsureOwnerComponentsDisabled();
                    StopAllCoroutines();
                    CancelInvoke();
                    break;
            }
        }

        /// <summary>
        /// Owner path: capture the current CAS motor state into an OwnerMotionSample for
        /// submission to the host at the configured send rate.
        /// </summary>
        public OwnerMotionSample CaptureOwnerMotionSample(uint sequence, int networkTick)
        {
            return new OwnerMotionSample
            {
                Sequence = sequence,
                NetworkTick = networkTick,
                Position = transform.position,
                Velocity = _velocity,
                BodyYaw = transform.eulerAngles.y,
                AimYaw = _aimRotation.eulerAngles.y,
                AimPitch = _lookInput.y,
                MoveX = _moveInput.x,
                MoveY = _moveInput.y,
                Gait = _gait,
                IsGrounded = IsGrounded,
                IsInAir = _isInAir,
                IsCrouching = _isCrouching,
                IsSprinting = IsSprinting,
                IsAiming = _isAiming,
                IsMoving = HasMoveInputs()
            };
        }

        /// <summary>
        /// Host path: build a ProxyPresentationState from the locally-simulated host copy of
        /// this player so NetworkCasPlayer can broadcast it to remote clients.
        /// </summary>
        public ProxyPresentationState BuildProxyPresentationState(uint acceptedSequence, int networkTick, bool isAlive)
        {
            return new ProxyPresentationState
            {
                Sequence = acceptedSequence,
                NetworkTick = networkTick,
                Position = transform.position,
                Velocity = _velocity,
                BodyYaw = transform.eulerAngles.y,
                AimYaw = _aimRotation.eulerAngles.y,
                AimPitch = _lookInput.y,
                MoveX = _moveInput.x,
                MoveY = _moveInput.y,
                Gait = _gait,
                IsGrounded = IsGrounded,
                IsInAir = _isInAir,
                IsCrouching = _isCrouching,
                IsSprinting = IsSprinting,
                IsAiming = _isAiming,
                IsMoving = HasMoveInputs(),
                IsAlive = isAlive
            };
        }

        /// <summary>
        /// Host path: record the accepted proxy state so NetworkCasPlayer can read it for
        /// broadcasting. Called after the host validates and accepts an owner motion sample,
        /// or after a host-side reconciliation.
        /// </summary>
        public void RecordAcceptedProxyState(in ProxyPresentationState state)
        {
            _acceptedProxyState = state;
            _hasAcceptedProxyState = true;
        }

        public bool TryGetAcceptedProxyState(out ProxyPresentationState state)
        {
            state = _acceptedProxyState;
            return _hasAcceptedProxyState;
        }

        /// <summary>Apply a sampled remote state and preserve the human proxy root wrapper.</summary>
        public void ApplyRemotePresentationState(in ProxyInterpolationBuffer.SampledState state)
        {
            ApplyRemotePresentationStateInternal(state, applyRootTransform: true);
        }

        /// <summary>
        /// Apply remote animation and procedural state without writing this GameObject's root
        /// transform. Use when another component, such as a NavMeshAgent or NetworkTransform,
        /// owns root motion.
        /// </summary>
        public void ApplyRemotePresentationStateWithoutRootMotion(
            in ProxyInterpolationBuffer.SampledState state)
        {
            ApplyRemotePresentationStateInternal(state, applyRootTransform: false);
        }

        private void ApplyRemotePresentationStateInternal(
            in ProxyInterpolationBuffer.SampledState state, bool applyRootTransform)
        {
            if (_simulationMode != PlayerSimulationMode.RemoteProxy) return;
            if (_animator == null) return;

            if (applyRootTransform)
            {
                // Human remote proxies have no active motor; interpolation owns their root.
                transform.position = state.Position;
                transform.rotation = Quaternion.Euler(0f, state.BodyYaw, 0f);
            }

            if (_characterCamera != null)
            {
                _characterCamera.pitchInput = state.AimPitch;
                _characterCamera.yawInput = state.AimYaw;
                _characterCamera.isFirstPerson = false;
                _characterCamera.isAiming = state.IsAiming;
                _characterCamera.isCrouching = state.IsCrouching;
            }

            _aimRotation = Quaternion.Euler(0f, state.AimYaw, 0f);
            _lookInput.y = state.AimPitch;
            _isAiming = state.IsAiming;
            _isCrouching = state.IsCrouching;
            _isInAir = state.IsInAir;
            _isGrounded = state.IsGrounded;
            _moveInput = new Vector2(state.MoveX, state.MoveY);
            _gait = state.Gait;

            // The base class derives IsSprinting from _movementState, not from a dedicated
            // sprint flag. The bridge reads IsSprinting to drive the Tactical sprint pose
            // and to suppress firing, so the proxy must keep _movementState in sync with the
            // replicated sprint state. Map the replicated flags back to the base enum.
            if (state.IsSprinting)
                _movementState = CharacterMovementState.Sprint;
            else if (state.IsCrouching)
                _movementState = CharacterMovementState.CrouchWalk;
            else if (state.IsMoving)
                _movementState = CharacterMovementState.Jog;
            else
                _movementState = CharacterMovementState.Idle;

            ApplySharedPresentation(new LocomotionPresentationInput
            {
                MoveAxes = _moveInput,
                GaitSource = GaitSource.ResolvedRawGait,
                RawGait = state.Gait,
                IsMoving = state.IsMoving,
                IsGrounded = state.IsGrounded,
                IsInAir = state.IsInAir,
                IsCrouching = state.IsCrouching,
                IsSprinting = state.IsSprinting,
                IsAiming = state.IsAiming,
                IsAlive = state.IsAlive,
                IsFirstPerson = false,
                ViewWeight = _characterCamera != null ? _characterCamera.ViewWeight : 0f,
                AimingWeight = state.IsAiming ? 1f : 0f
            }, Time.deltaTime);
        }

        /// <summary>
        /// Applies shared CAS presentation without touching the controller's root transform.
        /// Bot adapters use this path while NavMeshAgent/NetworkTransform owns the root.
        /// </summary>
        public void ApplySharedPresentationWithoutRootMotion(
            in LocomotionPresentationInput input)
        {
            if (_simulationMode != PlayerSimulationMode.RemoteProxy) return;
            ApplySharedPresentationWithoutRootMotion(input, Time.deltaTime);
        }

        public void ApplySharedPresentationWithoutRootMotion(
            in LocomotionPresentationInput input, float deltaTime)
        {
            if (_simulationMode != PlayerSimulationMode.RemoteProxy) return;
            _moveInput = input.MoveAxes;
            _isGrounded = input.IsGrounded;
            _isInAir = input.IsInAir;
            _isCrouching = input.IsCrouching;
            _isAiming = input.IsAiming;
            _movementState = input.IsSprinting
                ? CharacterMovementState.Sprint
                : input.IsCrouching
                    ? CharacterMovementState.CrouchWalk
                    : input.IsMoving ? CharacterMovementState.Jog : CharacterMovementState.Idle;
            ApplySharedPresentation(input, deltaTime);
        }

        private void ApplySharedPresentation(
            LocomotionPresentationInput input, float deltaTime)
        {
            if (_animator == null) return;

            LocomotionPresentationOutput output = _presentationEvaluator.Evaluate(
                input, BuildPresentationSettings(), deltaTime);
            _gait = output.RawPresentationGait;
            _animatorGait = output.SmoothedAnimatorGait;
            _animator.SetFloat(Animator_Move_X, output.SmoothedAnimatorMoveAxes.x);
            _animator.SetFloat(Animator_Move_Y, output.SmoothedAnimatorMoveAxes.y);
            _animator.SetFloat(Animator_Gait, output.SmoothedAnimatorGait);
            _animator.SetFloat(Animator_ViewWeight, output.AnimatorViewWeight);
            _animator.SetFloat(Animator_AimingWeight, output.AnimatorAimingWeight);
            _animator.SetBool(Animator_IsFirstPerson, output.AnimatorIsFirstPerson);
            _animator.SetBool(Animator_IsInAir, output.AnimatorIsInAir);
            _animator.SetBool(Animator_IsMoving, output.AnimatorIsMoving);
            _animator.SetBool(Animator_Crouch, output.AnimatorIsCrouching);
            if (_motionWarping != null)
                _animator.SetBool(Animator_IsTraversing, _motionWarping.IsActive());

            if (output.MovementStarted) OnMovementChange(true);
            if (output.MovementStopped) OnMovementChange(false);
            if (output.CrouchStarted && _isGrounded && !output.AnimatorIsMoving
                && _proceduralAnimation != null)
            {
                _proceduralAnimation.UpdateAnimationModifier(stepCrouch);
            }
            if (output.CrouchStopped && _isGrounded && !output.AnimatorIsMoving
                && _proceduralAnimation != null)
            {
                _proceduralAnimation.UpdateAnimationModifier(stepUncrouch);
            }
            if (output.Jumped) _animator.SetTrigger(Animator_Jumped);
            if ((output.AimStarted || output.AimStopped) && _proceduralAnimation != null)
            {
                _proceduralAnimation.UpdateAnimationModifier(aimingMotion);
            }
        }

        private LocomotionPresentationSettings BuildPresentationSettings()
        {
            return new LocomotionPresentationSettings
            {
                WalkSpeed = walkGait.velocity,
                JogSpeed = jogGait.velocity,
                SprintSpeed = sprintGait.velocity,
                AnimGaitSmoothing = animGaitSmoothing,
                AnimatorMoveInterpSpeed = animatorMoveInterpSpeed,
                OrientRotationToMovement = orientRotationToMovement
            };
        }

        /// <summary>
        /// The owning client runs the existing CAS Update path unchanged. Remote proxies and
        /// disabled players skip it entirely; their state is driven by
        /// <see cref="ApplyRemotePresentationState"/> or remains frozen.
        /// </summary>
        protected override void Update()
        {
            if (_simulationMode != PlayerSimulationMode.LocalOwner) return;

            // The base Update path runs input, grounding, gait, rotation, movement, and
            // animator parameters exactly as in the offline single-player rig.
            base.Update();
        }

        protected override void ApplyFinalPresentation()
        {
            if (_simulationMode != PlayerSimulationMode.LocalOwner) return;

            ApplySharedPresentation(new LocomotionPresentationInput
            {
                MoveAxes = _moveInput,
                GaitSource = GaitSource.ResolvedRawGait,
                RawGait = _gait,
                IsMoving = HasMoveInputs(),
                IsGrounded = IsGrounded,
                IsInAir = _isInAir,
                IsCrouching = _isCrouching,
                IsSprinting = IsSprinting,
                IsAiming = _isAiming,
                IsAlive = true,
                IsFirstPerson = isFirstPerson,
                ViewWeight = _characterCamera != null ? _characterCamera.ViewWeight : 0f,
                AimingWeight = AimingWeight
            }, Time.deltaTime);
        }

        /// <summary>
        /// On a hosting player, the host owns the local simulation. On a remote owner, the
        /// owner runs the simulation and submits samples. In both cases the owning client uses
        /// the LocalOwner path. The host's local copy of a remote owner uses RemoteProxy.
        /// </summary>
        private void CacheOwnerOnlyComponents()
        {
            if (_didCacheOwnerComponents) return;
            _cachedPlayerInput = GetComponent<PlayerInput>();
            _cachedCharacterCamera = GetComponentInChildren<CharacterCamera>(true);
            if (_cachedCharacterCamera != null)
            {
                _cachedCamera = _cachedCharacterCamera.GetComponent<Camera>();
                _cachedAudioListener = _cachedCharacterCamera.GetComponent<AudioListener>();
            }
            _cachedRecoilAnimation = GetComponent<RecoilAnimation>();
            _didCacheOwnerComponents = true;
        }

        private void EnsureOwnerComponentsEnabled()
        {
            if (_cachedPlayerInput != null) _cachedPlayerInput.enabled = true;
            if (_cachedCamera != null) _cachedCamera.enabled = true;
            if (_cachedAudioListener != null) _cachedAudioListener.enabled = true;
            if (_cachedRecoilAnimation != null) _cachedRecoilAnimation.enabled = true;
        }

        private void EnsureOwnerComponentsDisabled()
        {
            if (_cachedPlayerInput != null) _cachedPlayerInput.enabled = false;
            if (_cachedCamera != null) _cachedCamera.enabled = false;
            if (_cachedAudioListener != null) _cachedAudioListener.enabled = false;
            // Keep RecoilAnimation alive on proxies only if the bridge needs it for Tactical
            // presentation; for now disable it since proxies do not fire locally.
            if (_cachedRecoilAnimation != null) _cachedRecoilAnimation.enabled = false;
        }

        /// <summary>
        /// Reset the proxy edge-detection state so the next ApplyRemotePresentationState call
        /// treats its first frame as a fresh transition. Called on respawn and hard correction.
        /// </summary>
        public void ResetProxyState()
        {
            _presentationEvaluator.Reset();
        }

        private void OnDisable()
        {
            _presentationEvaluator.Reset();
        }
    }
}
