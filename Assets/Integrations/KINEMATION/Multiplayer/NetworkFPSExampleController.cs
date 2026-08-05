using CAS_Demo.Scripts.FPS;
using FPSProject.Multiplayer.Core.Movement;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
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

        // Cached owner-only components so we can disable/re-enable them on spawn/despawn/death.
        private PlayerInput _cachedPlayerInput;
        private Camera _cachedCamera;
        private AudioListener _cachedAudioListener;
        private CharacterCamera _cachedCharacterCamera;
        private RecoilAnimation _cachedRecoilAnimation;
        private bool _didCacheOwnerComponents;

        // Previous-frame state for edge detection in ApplyRemotePresentationState.
        private bool _proxyWasMoving;
        private bool _proxyWasInAir;
        private bool _proxyWasCrouching;
        private bool _proxyWasAiming;
        private bool _proxyWasSprinting;
        private bool _proxyWasGrounded;
        private float _proxyPrevGait;
        private bool _proxyFirstApply = true;

        // The host writes the accepted pose here for the NetworkCasPlayer to broadcast.
        private ProxyPresentationState _acceptedProxyState;
        private bool _hasAcceptedProxyState;

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
            SetSimulationMode(PlayerSimulationMode.RemoteProxy);
        }

        /// <summary>
        /// Switch the simulation mode. LocalOwner runs the full CAS Update path; RemoteProxy
        /// applies accepted presentation state; Disabled performs no work.
        /// </summary>
        public void SetSimulationMode(PlayerSimulationMode mode)
        {
            if (_simulationMode == mode) return;

            PlayerSimulationMode previous = _simulationMode;
            _simulationMode = mode;

            switch (mode)
            {
                case PlayerSimulationMode.LocalOwner:
                    EnsureOwnerComponentsEnabled();
                    if (_controller != null) _controller.enabled = true;
                    break;
                case PlayerSimulationMode.RemoteProxy:
                    EnsureOwnerComponentsDisabled();
                    if (_controller != null) _controller.enabled = false;
                    _proxyFirstApply = true;
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

        /// <summary>
        /// Proxy path: apply a sampled, interpolated remote presentation state to the hidden CAS
        /// source rig. Writes animator parameters directly instead of calling the full base
        /// controller update, and detects movement/crouch/jump/landing/aim state edges so the
        /// existing CAS procedural transition modifiers still fire once.
        /// </summary>
        public void ApplyRemotePresentationState(in ProxyInterpolationBuffer.SampledState state)
        {
            if (_simulationMode != PlayerSimulationMode.RemoteProxy) return;
            if (_animator == null) return;

            // Drive the body transform to the interpolated position. The CharacterController is
            // disabled on proxies, so we set the transform directly.
            transform.position = state.Position;

            // Body yaw rotates the whole character. Aim yaw/pitch feed the camera bone and the
            // bridge's Tactical pitch input.
            transform.rotation = Quaternion.Euler(0f, state.BodyYaw, 0f);

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
            _moveInput = new Vector2(state.MoveX, state.MoveY);
            _gait = state.Gait;
            _animatorGait = state.Gait;

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

            // Edge detection so CAS procedural transition modifiers fire once on state changes.
            bool isMoving = state.IsMoving;
            bool isGrounded = state.IsGrounded;
            bool isInAir = state.IsInAir;
            bool isCrouching = state.IsCrouching;
            bool isAiming = state.IsAiming;
            bool isSprinting = state.IsSprinting;

            if (_proxyFirstApply)
            {
                _proxyWasMoving = !isMoving;
                _proxyWasInAir = !isInAir;
                _proxyWasCrouching = !isCrouching;
                _proxyWasAiming = !isAiming;
                _proxyWasSprinting = !isSprinting;
                _proxyWasGrounded = !isGrounded;
                _proxyPrevGait = state.Gait;
                _proxyFirstApply = false;
            }

            // Crouch transition fires the crouch modifier once.
            if (isCrouching != _proxyWasCrouching)
            {
                _animator.SetBool(Animator_Crouch, isCrouching);
                if (_proceduralAnimation != null && isGrounded && !isMoving)
                {
                    _proceduralAnimation.UpdateAnimationModifier(isCrouching ? stepCrouch : stepUncrouch);
                }
            }

            // Jump trigger fires once when we transition from grounded to in-air.
            if (isInAir && !_proxyWasInAir)
            {
                _animator.SetTrigger(Animator_Jumped);
            }

            // Landing fires the landing momentum path once when we return to ground from air.
            if (isGrounded && !_proxyWasGrounded && _proxyWasInAir)
            {
                // ApplyLandingMomentum is protected on the base class; we cannot call it from
                // a proxy state sample because the proxy does not own velocity integration.
                // The animator IsInAir flag below handles the visual landing transition.
            }

            // Moving state edge fires the start/stop moving modifier once.
            if (isMoving != _proxyWasMoving)
            {
                if (_proceduralAnimation != null && isGrounded)
                {
                    _proceduralAnimation.UpdateAnimationModifier(isMoving ? startMoving : stopMoving);
                }
            }

            // Write animator parameters directly. This mirrors UpdateAnimatorParameters but
            // without the local-input-dependent move-input remap.
            Vector2 proxyMoveInput = new Vector2(state.MoveX, state.MoveY);
            // When not aiming and orient-to-movement is on, the base controller remaps move
            // input to a forward-only magnitude. Proxies receive the already-resolved body yaw,
            // so we feed the raw input and let the animator blend tree handle it.
            float moveAlpha = KMath.ExpDecayAlpha(animatorMoveInterpSpeed, Time.deltaTime);
            Vector2 animatorMove = Vector2.Lerp(
                new Vector2(_animator.GetFloat(Animator_Move_X), _animator.GetFloat(Animator_Move_Y)),
                proxyMoveInput, moveAlpha);
            _animator.SetFloat(Animator_Move_X, animatorMove.x);
            _animator.SetFloat(Animator_Move_Y, animatorMove.y);
            _animator.SetFloat(Animator_Gait, _animatorGait);
            _animator.SetFloat(Animator_ViewWeight, _characterCamera != null ? _characterCamera.ViewWeight : 0f);
            _animator.SetFloat(Animator_AimingWeight, isAiming ? 1f : 0f);
            _animator.SetBool(Animator_IsFirstPerson, false);
            _animator.SetBool(Animator_IsInAir, isInAir);
            _animator.SetBool(Animator_IsMoving, isMoving);

            _proxyWasMoving = isMoving;
            _proxyWasInAir = isInAir;
            _proxyWasCrouching = isCrouching;
            _proxyWasAiming = isAiming;
            _proxyWasSprinting = isSprinting;
            _proxyWasGrounded = isGrounded;
            _proxyPrevGait = state.Gait;
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
            _proxyFirstApply = true;
            _proxyWasMoving = false;
            _proxyWasInAir = false;
            _proxyWasCrouching = false;
            _proxyWasAiming = false;
            _proxyWasSprinting = false;
            _proxyWasGrounded = false;
            _proxyPrevGait = 0f;
        }
    }
}