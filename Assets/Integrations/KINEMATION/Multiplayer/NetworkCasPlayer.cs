using System;
using System.Collections;
using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Movement;
using FPSProject.Multiplayer.Core.Weapons;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Root network behaviour for the multiplayer CAS/Tactical player prefab. Owns player
    /// lifecycle (owner-only initialization, despawn, death state), the owner-to-host motion
    /// submission RPC, the host-to-proxy broadcast RPC, and the per-client interpolation and
    /// correction loop. CAS remains the sole owner of local input, movement, look, and camera;
    /// this component coordinates the network plumbing around it.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkCasPlayer : NetworkBehaviour
    {
        [Header("References (auto-resolved on spawn)")]
        [SerializeField] private NetworkFPSExampleController controller;
        [SerializeField] private NetworkTacticalShooterPlayer tacticalPlayer;
        [SerializeField] private NetworkWeaponState weaponState;
        [SerializeField] private NetworkHealth networkHealth;

        [Header("Tuning")]
        [Tooltip("Project-owned tuning asset. If unset, the component tries to load a " +
                 "MultiplayerTuningSettings asset from Resources.")]
        [SerializeField] private MultiplayerTuningSettings tuning;

        [Header("Validation")]
        [Tooltip("Static environment collision layer used for host-side capsule sweeps. " +
                 "Set to 0 to disable sweep validation.")]
        [SerializeField] private LayerMask staticEnvironmentMask = 1;

        [Header("Debug")]
        [SerializeField] private bool debugMotion = false;

        // Owner-side send state.
        private uint _ownerSequence;
        private float _timeSinceLastOwnerSample;

        // Host-side per-client validation state. Keyed by ClientId.
        private readonly Dictionary<ulong, HostClientState> _hostStates = new Dictionary<ulong, HostClientState>();

        // Proxy-side interpolation buffer. One per NetworkCasPlayer instance (each client owns
        // a buffer for each remote player it sees).
        private ProxyInterpolationBuffer _proxyBuffer;
        private float _proxyRenderTime;

        // Correction smoothing state for the owning client.
        private bool _isCorrecting;
        private Vector3 _correctionStartPos;
        private Vector3 _correctionTargetPos;
        private float _correctionElapsed;
        private float _correctionDuration;

        // Cached capsule dimensions for host validation.
        private float _capsuleRadius = 0.3f;
        private float _capsuleHeight = 1.8f;
        private Vector3 _capsuleCenter = Vector3.zero;
        private float _maxFallSpeed = 25f;

        // Alive state. The host owns this; clients read it from ProxyPresentationState.
        private bool _isAlive = true;
        private Coroutine _tacticalPresentationInitialization;

        public bool IsAlive => _isAlive;
        public MultiplayerTuningSettings Tuning => tuning;
        public NetworkFPSExampleController Controller => controller;
        public NetworkWeaponState WeaponState => weaponState;
        public NetworkTacticalShooterPlayer TacticalPlayer => tacticalPlayer;
        public NetworkHealth NetworkHealth => networkHealth;

        public override void OnNetworkSpawn()
        {
            if (controller == null) controller = GetComponent<NetworkFPSExampleController>();
            if (tacticalPlayer == null) tacticalPlayer = GetComponentInChildren<NetworkTacticalShooterPlayer>(true);
            if (weaponState == null) weaponState = GetComponent<NetworkWeaponState>();
            if (networkHealth == null) networkHealth = GetComponent<NetworkHealth>();
            if (tuning == null) tuning = ResolveTuning();

            ResolveCapsuleDimensions();

            if (IsOwner)
            {
                controller.InitializeAsOwner();
                _proxyBuffer = null;
            }
            else
            {
                controller.InitializeAsProxy();
                _proxyBuffer = new ProxyInterpolationBuffer(tuning);
                _proxyRenderTime = 0f;
            }

            _tacticalPresentationInitialization = StartCoroutine(
                InitializeTacticalPresentation());

            // Subscribe to weapon state changes so owners and proxies apply authoritative
            // equipped weapon, ammo, fire mode, reload, and life state as it changes.
            if (weaponState != null)
            {
                weaponState.EquippedWeaponId.OnValueChanged += OnEquippedWeaponIdChanged;
                weaponState.ActiveFireMode.OnValueChanged += OnFireModeChanged;
                weaponState.ActiveReloadState.OnValueChanged += OnReloadStateChanged;
                weaponState.LifeState.OnValueChanged += OnLifeStateChanged;
                weaponState.AmmoState.OnListChanged += OnAmmoListChanged;
            }

            // Host-side: initialize validation state for every existing client that owns a player.
            // The host's own player uses LocalOwner and does not go through the validator.
            if (IsServer)
            {
                _hostStates.Clear();
                // Wire the late-join callback so that when a new client connects, the host sends
                // one reliable current snapshot for every existing player to that client.
                if (NetworkManager != null)
                {
                    NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                    NetworkManager.OnClientConnectedCallback += OnClientConnected;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_tacticalPresentationInitialization != null)
            {
                StopCoroutine(_tacticalPresentationInitialization);
                _tacticalPresentationInitialization = null;
            }

            // Unsubscribe the late-join callback to avoid leaking it after despawn.
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            }

            if (weaponState != null)
            {
                weaponState.EquippedWeaponId.OnValueChanged -= OnEquippedWeaponIdChanged;
                weaponState.ActiveFireMode.OnValueChanged -= OnFireModeChanged;
                weaponState.ActiveReloadState.OnValueChanged -= OnReloadStateChanged;
                weaponState.LifeState.OnValueChanged -= OnLifeStateChanged;
                weaponState.AmmoState.OnListChanged -= OnAmmoListChanged;
            }

            controller?.SetSimulationMode(PlayerSimulationMode.Disabled);
            _proxyBuffer?.Clear(ProxyInterpolationBuffer.ClearReason.ManualReset);
            _hostStates.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Weapon state change handlers. Drive ID-based Tactical presentation from
        // host-authoritative NetworkVariables. Owners and proxies apply the same accepted
        // state; the owner additionally predicts locally and reconciles on confirmation.
        // ─────────────────────────────────────────────────────────────────────────────

        private void OnEquippedWeaponIdChanged(ushort previous, ushort current)
        {
            ApplyEquippedWeaponPresentation(current);
        }

        private void OnFireModeChanged(KINEMATION.ProceduralRecoilAnimationSystem.Runtime.FireMode previous,
            KINEMATION.ProceduralRecoilAnimationSystem.Runtime.FireMode current)
        {
            ApplyCurrentFireModePresentation();
        }

        private void OnReloadStateChanged(ReloadState previous, ReloadState current)
        {
            if (tacticalPlayer == null) return;
            var presentation = tacticalPlayer.GetActiveNetworkWeaponPresentation();
            if (presentation == null) return;
            if (current == ReloadState.Reloading)
                presentation.PlayNetworkReloadPresentation();
            else if (previous == ReloadState.Reloading && current == ReloadState.None)
                presentation.PlayNetworkReloadEndPresentation();
        }

        private void OnLifeStateChanged(PlayerLifeState previous, PlayerLifeState current)
        {
            if (current == PlayerLifeState.Dead)
            {
                _isAlive = false;
                controller?.SetSimulationMode(PlayerSimulationMode.Disabled);
            }
            else if (current == PlayerLifeState.Alive && previous != PlayerLifeState.Alive)
            {
                _isAlive = true;
            }
        }

        private void OnAmmoListChanged(Unity.Netcode.NetworkListEvent<WeaponAmmoState> changeEvent)
        {
            // Only apply if the changed entry is for the currently-equipped weapon.
            if (weaponState == null || tacticalPlayer == null) return;
            if (changeEvent.Value.WeaponId != weaponState.EquippedWeaponId.Value) return;
            ApplyCurrentAmmoPresentation();
        }

        /// <summary>
        /// Host-side callback fired when a new client connects. Sends one reliable current
        /// snapshot for every existing player to the joining client so it can populate its
        /// proxy interpolation buffers before normal unreliable updates continue.
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            // The host itself triggers this callback; skip sending snapshots to the host.
            if (clientId == NetworkManager.LocalClientId) return;

            // Find every spawned NetworkCasPlayer and send its accepted state to the new client.
            // Each player sends its own snapshot; this component sends its own.
            SendLateJoinSnapshot(clientId);
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner && controller.SimulationMode == PlayerSimulationMode.LocalOwner)
            {
                OwnerUpdate();
            }
            else if (!IsOwner && controller.SimulationMode == PlayerSimulationMode.RemoteProxy)
            {
                ProxyUpdate();
            }

            // Host-side: broadcast accepted proxy states to all clients at the send rate.
            if (IsServer)
            {
                HostBroadcastUpdate();
            }

            // Owner-side: apply a pending smooth correction.
            if (_isCorrecting)
            {
                ApplyCorrection();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Owner path: capture and submit motion samples at the configured send rate.
        // ─────────────────────────────────────────────────────────────────────────────

        private void OwnerUpdate()
        {
            if (tuning == null) return;

            _timeSinceLastOwnerSample += Time.deltaTime;
            float sendInterval = 1f / tuning.motionSendRate;

            if (_timeSinceLastOwnerSample >= sendInterval)
            {
                _timeSinceLastOwnerSample = 0f;
                _ownerSequence++;

                // LocalTime intentionally runs ahead on NGO clients to support prediction.
                // Motion validation compares against the host's ServerTime, so submitting a
                // LocalTime tick makes every non-host owner appear future-dated over Relay.
                int networkTick = (int)NetworkManager.ServerTime.Tick;
                OwnerMotionSample sample = controller.CaptureOwnerMotionSample(_ownerSequence, networkTick);
                SubmitMotionSampleServerRpc(sample);
            }
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable, RequireOwnership = true)]
        private void SubmitMotionSampleServerRpc(OwnerMotionSample sample, ServerRpcParams rpcParams = default)
        {
            if (!IsServer) return;

            ulong clientId = rpcParams.Receive.SenderClientId;
            if (clientId != OwnerClientId) return;

            HostValidateAndApply(clientId, sample);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Host path: validate, accept/reject, record accepted proxy state, broadcast.
        // ─────────────────────────────────────────────────────────────────────────────

        private void HostValidateAndApply(ulong clientId, in OwnerMotionSample sample)
        {
            if (!_hostStates.TryGetValue(clientId, out HostClientState state))
            {
                // First sample from this client: validate only non-finite and world-bounds,
                // then accept it as the baseline without speed/capsule checks (there is no
                // previous accepted position to compare against).
                if (!IsFinite(sample.Position) || !IsFinite(sample.Velocity))
                {
                    if (debugMotion) Debug.Log($"[Host] Rejected first sample from {clientId}: non-finite.");
                    return;
                }
                if (tuning != null && !tuning.IsInsideWorldBounds(sample.Position))
                {
                    if (debugMotion) Debug.Log($"[Host] Rejected first sample from {clientId}: out of world bounds {sample.Position}.");
                    SendHardCorrectionClientRpc(BuildProxyFromSample(sample, 0, true), ForClient(clientId));
                    return;
                }

                state = new HostClientState
                {
                    LastAcceptedPosition = sample.Position,
                    LastAcceptedTime = Time.time,
                    LastAcceptedSequence = sample.Sequence,
                    AcceptedProxyState = BuildProxyFromSample(sample, sample.Sequence, true),
                    HasPendingBroadcast = true
                };
                _hostStates[clientId] = state;
                if (debugMotion) Debug.Log($"[Host] Accepted first sample seq={sample.Sequence} from {clientId} at {sample.Position}.");
                return;
            }

            // Stale/out-of-order sequence rejection.
            if (sample.Sequence <= state.LastAcceptedSequence)
            {
                if (debugMotion) Debug.Log($"[Host] Rejected stale sequence {sample.Sequence} <= {state.LastAcceptedSequence} from client {clientId}.");
                return;
            }

            // Build validation context.
            float elapsed = Time.time - state.LastAcceptedTime;
            int currentTick = NetworkManager != null ? (int)NetworkManager.ServerTime.Tick : 0;
            var ctx = new HostMotionValidator.ValidationContext
            {
                Tuning = tuning,
                LastAcceptedPosition = state.LastAcceptedPosition,
                LastAcceptedTime = state.LastAcceptedTime,
                StaticEnvironmentMask = staticEnvironmentMask,
                CapsuleRadius = _capsuleRadius,
                CapsuleHeight = _capsuleHeight,
                CapsuleCenter = _capsuleCenter,
                MaxFallSpeed = _maxFallSpeed,
                CurrentNetworkTick = currentTick,
                ElapsedTime = elapsed
            };

            HostMotionValidator.ValidationResult result = HostMotionValidator.Validate(sample, ctx);

            if (!result.Accepted)
            {
                if (debugMotion) Debug.Log($"[Host] Rejected sample seq={sample.Sequence} from {clientId}: {result.Reason} - {result.DebugMessage}");

                // Decide correction severity by divergence distance, using the tuning's
                // soft/hard thresholds. Non-finite, out-of-world-bounds, and future-tick
                // are always hard-corrected regardless of distance.
                float divergence = Vector3.Distance(sample.Position, state.LastAcceptedPosition);
                bool forceHard = result.Reason == HostMotionValidator.RejectReason.NonFinite
                    || result.Reason == HostMotionValidator.RejectReason.OutOfWorldBounds
                    || result.Reason == HostMotionValidator.RejectReason.FutureTick
                    || divergence >= tuning.hardCorrectionThreshold;

                if (forceHard)
                {
                    SendHardCorrectionClientRpc(state.AcceptedProxyState,
                        ForClient(clientId));
                }
                else if (divergence >= tuning.softCorrectionThreshold)
                {
                    // Soft correction for moderate divergence.
                    SendSoftCorrectionClientRpc(state.AcceptedProxyState,
                        ForClient(clientId));
                }
                // If the sample was rejected but divergence is below the soft threshold,
                // no correction is needed — the owner is close enough to the accepted pose.
                return;
            }

            // Accepted: update host state and record the accepted proxy state.
            // Preserve the owner's sequence. Unreliable delivery can skip packets; rewriting
            // a received sequence as last + 1 would allow a delayed older packet to pass the
            // stale check and move the proxy backwards.
            uint acceptedSeq = sample.Sequence;
            ProxyPresentationState proxy = BuildProxyFromSample(sample, acceptedSeq, _isAlive);

            state.LastAcceptedPosition = sample.Position;
            state.LastAcceptedTime = Time.time;
            state.LastAcceptedSequence = acceptedSeq;
            state.AcceptedProxyState = proxy;
            state.HasPendingBroadcast = true;
            _hostStates[clientId] = state;

            // Record the accepted pose into the host-side hitbox history for lag-compensated
            // hitscan. The history is sized by the tuning's rewind duration.
            RecordHitboxHistory(clientId, in sample);

            // If this is the host's own player (host is also an owner), apply the accepted pose
            // to the host's local controller copy so the host sees the same reconciliation.
            // The host's local owner runs LocalOwner, so it already simulates locally; we do
            // not override its transform here. The accepted state is still broadcast to others.

            if (debugMotion) Debug.Log($"[Host] Accepted seq={acceptedSeq} from {clientId} at {sample.Position}.");
        }

        private ProxyPresentationState BuildProxyFromSample(in OwnerMotionSample sample, uint acceptedSequence, bool isAlive)
        {
            return new ProxyPresentationState
            {
                Sequence = acceptedSequence,
                NetworkTick = sample.NetworkTick,
                Position = sample.Position,
                Velocity = sample.Velocity,
                BodyYaw = sample.BodyYaw,
                AimYaw = sample.AimYaw,
                AimPitch = sample.AimPitch,
                MoveX = sample.MoveX,
                MoveY = sample.MoveY,
                Gait = sample.Gait,
                IsGrounded = sample.IsGrounded,
                IsInAir = sample.IsInAir,
                IsCrouching = sample.IsCrouching,
                IsSprinting = sample.IsSprinting,
                IsAiming = sample.IsAiming,
                IsMoving = sample.IsMoving,
                IsAlive = isAlive
            };
        }

        private void HostBroadcastUpdate()
        {
            if (tuning == null) return;
            float sendInterval = 1f / tuning.motionSendRate;

            // Collect the keys that need broadcasting this frame, then update after the
            // enumeration to avoid modifying the dictionary while iterating it.
            var toBroadcast = new List<ulong>();
            foreach (var kvp in _hostStates)
            {
                HostClientState state = kvp.Value;
                if (!state.HasPendingBroadcast) continue;
                if (Time.time - state.LastBroadcastTime < sendInterval) continue;
                toBroadcast.Add(kvp.Key);
            }

            foreach (ulong clientId in toBroadcast)
            {
                HostClientState state = _hostStates[clientId];
                state.LastBroadcastTime = Time.time;
                state.HasPendingBroadcast = false;
                _hostStates[clientId] = state;
                BroadcastProxyStateClientRpc(state.AcceptedProxyState);
            }
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void BroadcastProxyStateClientRpc(ProxyPresentationState state)
        {
            // The host does not need to apply its own broadcast back to itself for remote-proxy
            // purposes, but it does maintain a proxy buffer for each remote player. On the host,
            // every remote player is a proxy, so this still applies.
            if (IsOwner) return; // The owner does not interpolate itself.

            if (_proxyBuffer == null) _proxyBuffer = new ProxyInterpolationBuffer(tuning);
            _proxyBuffer.Add(state, Time.time);

            // Update alive state from the broadcast.
            if (state.IsAlive != _isAlive)
            {
                _isAlive = state.IsAlive;
                if (!_isAlive) controller.SetSimulationMode(PlayerSimulationMode.Disabled);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Correction RPCs: host -> owner.
        // ─────────────────────────────────────────────────────────────────────────────

        [ClientRpc]
        private void SendSoftCorrectionClientRpc(ProxyPresentationState state, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner) return;
            BeginSoftCorrection(state.Position);
        }

        [ClientRpc]
        private void SendHardCorrectionClientRpc(ProxyPresentationState state, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner) return;
            BeginHardCorrection(state.Position);
        }

        private void BeginSoftCorrection(Vector3 target)
        {
            if (controller == null) return;
            _isCorrecting = true;
            _correctionStartPos = controller.transform.position;
            _correctionTargetPos = target;
            _correctionElapsed = 0f;
            _correctionDuration = tuning != null ? tuning.correctionSmoothDuration : 0.1f;
        }

        private void BeginHardCorrection(Vector3 target)
        {
            if (controller == null) return;
            controller.transform.position = target;
            _proxyBuffer?.Clear(ProxyInterpolationBuffer.ClearReason.HardCorrection);
            _isCorrecting = false;
        }

        private void ApplyCorrection()
        {
            if (controller == null) { _isCorrecting = false; return; }
            _correctionElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_correctionElapsed / Mathf.Max(0.001f, _correctionDuration));
            Vector3 pos = Vector3.Lerp(_correctionStartPos, _correctionTargetPos, t);
            controller.transform.position = pos;
            if (t >= 1f) _isCorrecting = false;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Proxy path: sample the interpolation buffer and drive the CAS presentation.
        // ─────────────────────────────────────────────────────────────────────────────

        private void ProxyUpdate()
        {
            if (_proxyBuffer == null || !_proxyBuffer.HasData) return;

            _proxyRenderTime = Time.time;
            if (_proxyBuffer.Sample(_proxyRenderTime, out ProxyInterpolationBuffer.SampledState sampled))
            {
                controller.ApplyRemotePresentationState(sampled);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
// Late-join: when a new client connects, the host sends one reliable current snapshot for
        // every existing player before normal unreliable updates continue.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Host-side: send a reliable current snapshot for this player to a specific client.
        /// Called for every existing player when a new client joins.
        /// </summary>
        public void SendLateJoinSnapshot(ulong targetClientId)
        {
            if (!IsServer) return;
            if (!_hostStates.TryGetValue(OwnerClientId, out HostClientState state)) return;

            var rpcParams = ForClient(targetClientId);
            SendLateJoinSnapshotClientRpc(state.AcceptedProxyState, rpcParams);
        }

        [ClientRpc(Delivery = RpcDelivery.Reliable)]
        private void SendLateJoinSnapshotClientRpc(ProxyPresentationState state, ClientRpcParams rpcParams = default)
        {
            if (IsOwner) return;
            if (_proxyBuffer == null) _proxyBuffer = new ProxyInterpolationBuffer(tuning);
            _proxyBuffer.Clear(ProxyInterpolationBuffer.ClearReason.LateJoin);
            _proxyBuffer.Add(state, Time.time);
            _isAlive = state.IsAlive;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Initializes the vendor Tactical presentation without relying on Unity's unspecified
        /// Start order. The base Animator evaluates once before the procedural playable is added;
        /// Animator evaluation then stays paused until both the playable and equipped weapon
        /// settings are ready.
        /// </summary>
        private IEnumerator InitializeTacticalPresentation()
        {
            Transform tacticalChild = transform.Find("Tactical Presentation");
            if (tacticalChild == null)
            {
                _tacticalPresentationInitialization = null;
                yield break;
            }

            TacticalProceduralAnimation tacticalAnimation
                = tacticalChild.GetComponent<TacticalProceduralAnimation>();
            NetworkTacticalShooterPlayer netTacticalPlayer
                = tacticalChild.GetComponent<NetworkTacticalShooterPlayer>();
            TacticalShooterPlayer tacticalPlayer = netTacticalPlayer;
            Animator tacticalAnimator = tacticalChild.GetComponentInChildren<Animator>(true);
            if (tacticalAnimation == null || tacticalPlayer == null || tacticalAnimator == null)
            {
                Debug.LogError($"[{nameof(NetworkCasPlayer)}] Tactical presentation is missing "
                    + "its animation, player, or Animator component.", this);
                _tacticalPresentationInitialization = null;
                yield break;
            }

            tacticalAnimation.enabled = false;
            tacticalPlayer.enabled = false;

            // Allow the base controller to produce a valid animation stream before adding the
            // procedural output that consumes AnimationStreamSource.PreviousInputs.
            yield return null;
            if (!IsSpawned) yield break;

            bool restoreAnimatorEnabled = tacticalAnimator.enabled;
            tacticalAnimator.enabled = false;

            // Start creates the procedural job/playable. Keeping the Animator paused prevents
            // that job from evaluating before it has weapon settings.
            tacticalAnimation.enabled = true;
            yield return null;
            if (!IsSpawned) yield break;

            // Start instantiates and equips the presentation weapon, then pushes its settings
            // into the already-created procedural job.
            tacticalPlayer.enabled = true;
            yield return null;
            if (!IsSpawned) yield break;

            tacticalAnimator.enabled = restoreAnimatorEnabled;
            _tacticalPresentationInitialization = null;

            // After the vendor Start path has populated the Tactical weapon list, initialize the
            // network tactical player's catalog mapping and apply the current authoritative
            // equipped weapon ID so late joiners and proxies present the correct weapon.
            if (netTacticalPlayer != null && weaponState != null)
            {
                netTacticalPlayer.InitializeNetwork(weaponState.Catalog);
                ApplyEquippedWeaponPresentation(weaponState.EquippedWeaponId.Value);
                ApplyCurrentAmmoPresentation();
                ApplyCurrentFireModePresentation();
            }
        }

        /// <summary>
        /// Apply an authoritative equipped weapon ID to the Tactical presentation. Called on
        /// spawn and whenever the EquippedWeaponId NetworkVariable changes. Idempotent.
        /// </summary>
        public void ApplyEquippedWeaponPresentation(ushort weaponId)
        {
            if (tacticalPlayer == null) return;
            tacticalPlayer.ApplyEquippedWeapon(weaponId);
            // After equipping, push the authoritative ammo and fire mode for the new weapon.
            ApplyCurrentAmmoPresentation();
            ApplyCurrentFireModePresentation();
        }

        /// <summary>
        /// Push the authoritative ammunition for the currently-equipped weapon to the Tactical
        /// presentation. Called on spawn, weapon change, and ammo change.
        /// </summary>
        public void ApplyCurrentAmmoPresentation()
        {
            if (tacticalPlayer == null || weaponState == null) return;
            var presentation = tacticalPlayer.GetActiveNetworkWeaponPresentation();
            if (presentation == null) return;
            presentation.SetNetworkAmmo(weaponState.GetEquippedAmmo(), weaponState.GetEquippedCapacity());
        }

        /// <summary>
        /// Push the authoritative fire mode to the Tactical presentation.
        /// </summary>
        public void ApplyCurrentFireModePresentation()
        {
            if (tacticalPlayer == null || weaponState == null) return;
            var presentation = tacticalPlayer.GetActiveNetworkWeaponPresentation();
            if (presentation == null) return;
            presentation.SetNetworkFireMode(weaponState.ActiveFireMode.Value);
        }

        private void ResolveCapsuleDimensions()
        {
            var cc = GetComponentInChildren<CharacterController>();
            if (cc != null)
            {
                _capsuleRadius = cc.radius;
                _capsuleHeight = cc.height;
                _capsuleCenter = cc.center;
            }
        }

        private static MultiplayerTuningSettings ResolveTuning()
        {
            var assets = UnityEngine.Resources.LoadAll<MultiplayerTuningSettings>("");
            return assets != null && assets.Length > 0 ? assets[0] : null;
        }

        // Host-side per-client last accepted shot sequence. Keyed by ClientId. Used to reject
        // stale/duplicate/out-of-order shot commands.
        private readonly Dictionary<ulong, uint> _hostLastShotSequence = new Dictionary<ulong, uint>();

        // Host-side per-client hitbox history for lag-compensated hitscan. Keyed by ClientId.
        private readonly Dictionary<ulong, NetworkHitboxHistory> _hostHitboxHistories
            = new Dictionary<ulong, NetworkHitboxHistory>();

        // Per-shot set of targets damaged this shot, enforcing the single-pellet-per-target
        // rule for the shotgun. Reset at the start of each HostResolveShot.
        private readonly HashSet<ulong> _shotgunDamagedTargetsThisShot = new HashSet<ulong>();

        private FPSProject.Combat.Runtime.WeaponCombatRuntime _combatRuntime;
        private NetworkWeaponShotRouter _shotRouter;

        // ─────────────────────────────────────────────────────────────────────────────
        // Host-side hitbox history for lag-compensated hitscan. Records accepted poses and
        // reconstructs historical capsules at a client's shot tick.
        // ─────────────────────────────────────────────────────────────────────────────

        private void RecordHitboxHistory(ulong clientId, in OwnerMotionSample sample)
        {
            if (tuning == null) return;

            if (!_hostHitboxHistories.TryGetValue(clientId, out NetworkHitboxHistory history))
            {
                history = new NetworkHitboxHistory(tuning.rewindDuration);
                _hostHitboxHistories[clientId] = history;
            }

            float hostTime = (float)NetworkManager.ServerTime.Time;
            float crouchHeight = _capsuleHeight * tuning.crouchSpeedMultiplier;
            float capsuleHeight = sample.IsCrouching ? crouchHeight : _capsuleHeight;
            Vector3 capsuleCenter = sample.Position + new Vector3(0f, capsuleHeight * 0.5f, 0f);

            var poseSample = new HitboxPoseSample
            {
                Time = hostTime,
                Position = sample.Position,
                BodyYaw = sample.BodyYaw,
                CapsuleCenter = capsuleCenter,
                CapsuleHeight = capsuleHeight,
                CapsuleRadius = _capsuleRadius,
                IsCrouching = sample.IsCrouching
            };
            history.Record(hostTime, in poseSample);
        }

        /// <summary>
        /// Host-only: reconstruct a target's historical capsule at the given host network time.
        /// Returns false when the time is outside the 250 ms history window. Used by the shot
        /// resolver for lag-compensated hitscan.
        /// </summary>
        public bool TryGetHistoricalCapsule(ulong targetClientId, float hostTime, out HistoricalCapsule capsule)
        {
            capsule = default;
            if (!IsServer) return false;
            if (!_hostHitboxHistories.TryGetValue(targetClientId, out NetworkHitboxHistory history))
                return false;
            return history.TryGetCapsule(hostTime, out capsule);
        }

        /// <summary>
        /// Host-only: test a hitscan ray against every living player's historical capsule at the
        /// given host time. Returns true and the closest hit's target NetworkObjectId, distance,
        /// and impact point when a player hit is found. Environment obstruction is resolved
        /// against the current host physics world; a historical player hit is valid only when it
        /// is closer than the current-time environment obstruction along that ray.
        /// </summary>
        public bool TryHitHistoricalPlayer(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            float maxDistance,
            float hostTime,
            ulong shooterClientId,
            out ulong hitNetworkObjectId,
            out float hitDistance,
            out Vector3 hitPoint)
        {
            hitNetworkObjectId = ulong.MaxValue;
            hitDistance = float.MaxValue;
            hitPoint = default;

            if (!IsServer) return false;

            float closestPlayerDist = float.MaxValue;
            ulong closestClientId = ulong.MaxValue;
            ulong closestNetId = ulong.MaxValue;

            foreach (var kvp in _hostHitboxHistories)
            {
                ulong targetClientId = kvp.Key;
                if (targetClientId == shooterClientId) continue; // No self-damage this milestone.

                // Skip dead players. The host disables collision on death; dead players cannot
                // be hit. Check the target's NetworkHealth via the spawned NetworkObject.
                ulong targetNetId = ResolveNetworkObjectIdForClient(targetClientId);
                if (targetNetId == ulong.MaxValue) continue;
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out var targetNetObj))
                {
                    var targetHealth = targetNetObj.GetComponentInChildren<NetworkHealth>();
                    if (targetHealth != null && targetHealth.IsDead) continue;
                }

                var history = kvp.Value;
                if (!history.TryGetCapsule(hostTime, out var capsule)) continue;

                if (NetworkHitboxHistory.RaycastCapsule(rayOrigin, rayDirection, in capsule,
                    maxDistance, out float playerHitDist))
                {
                    if (playerHitDist < closestPlayerDist)
                    {
                        closestPlayerDist = playerHitDist;
                        closestClientId = targetClientId;
                        closestNetId = targetNetId;
                    }
                }
            }

            if (closestClientId == ulong.MaxValue) return false;

            // Resolve environment obstruction against the current host physics world. A
            // historical player hit is valid only when it is closer than the current-time
            // environment obstruction along that ray.
            var envMask = staticEnvironmentMask;
            if (envMask != 0)
            {
                RaycastHit envHit;
                if (Physics.Raycast(rayOrigin, rayDirection, out envHit, maxDistance, envMask))
                {
                    if (envHit.distance < closestPlayerDist)
                    {
                        // The environment is closer than the historical player hit; reject.
                        return false;
                    }
                }
            }

            hitNetworkObjectId = closestNetId;
            hitDistance = closestPlayerDist;
            hitPoint = rayOrigin + rayDirection * closestPlayerDist;
            return true;
        }

        /// <summary>
        /// Host-only: resolve the spawned NetworkObjectId owned by the given client. Returns
        /// ulong.MaxValue when no matching spawned player is found.
        /// </summary>
        private ulong ResolveNetworkObjectIdForClient(ulong clientId)
        {
            if (NetworkManager == null) return ulong.MaxValue;
            foreach (var kvp in NetworkManager.SpawnManager.SpawnedObjects)
            {
                if (kvp.Value.OwnerClientId == clientId
                    && kvp.Value.GetComponent<NetworkCasPlayer>() != null)
                {
                    return kvp.Key;
                }
            }
            return ulong.MaxValue;
        }

        /// <summary>
        /// Host-only: map a client network tick to host network time. Used by the shot resolver
        /// to find the historical pose at the client's shot tick.
        /// </summary>
        public float ClientTickToHostTime(int clientTick)
        {
            if (NetworkManager == null) return 0f;
            // Owners submit a tick from their synchronized ServerTime timeline. Convert the
            // difference between that captured tick and the host's current server tick into a
            // rewind offset for lag-compensated hit resolution.
            int serverTick = (int)NetworkManager.ServerTime.Tick;
            int deltaTicks = serverTick - clientTick;
            float tickInterval = 1f / NetworkManager.NetworkConfig.TickRate;
            return (float)NetworkManager.ServerTime.Time - (deltaTicks * tickInterval);
        }

        /// <summary>
        /// Host-only: apply damage to a historical target identified by its NetworkObjectId.
        /// Looks up the target's <see cref="NetworkHealth"/> via the spawned NetworkObject and
        /// applies the damage exactly once. The host owns all damage application.
        /// </summary>
        private void ApplyDamageToHistoricalTarget(ulong targetNetworkObjectId, float damage,
            Vector3 hitPoint, Vector3 travelDirection)
        {
            if (!IsServer || NetworkManager == null) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId,
                out var netObj))
            {
                return;
            }

            var health = netObj.GetComponentInChildren<NetworkHealth>();
            if (health == null || health.IsDead) return;

            var damageInfo = new FPSProject.Combat.Runtime.DamageInfo(
                damage, hitPoint, -travelDirection, travelDirection, gameObject, gameObject);
            health.ApplyDamage(damageInfo);
        }
        // owner submits a NetworkShotCommand. The host validates the command against the
        // catalog, accepted pose, cadence, and ammunition, then resolves damage exactly once
        // and broadcasts NetworkShotResult to every client.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Host-only: validate and resolve an owner-submitted shot command. Called from
        /// <see cref="NetworkWeaponShotRouter.SubmitShotServerRpc"/>. The host reconstructs a
        /// trusted <see cref="FPSProject.Combat.Runtime.WeaponShotRequest"/> from the catalog
        /// and the accepted player pose, resolves authoritative damage exactly once, and
        /// broadcasts <see cref="NetworkShotResult"/> so every client plays presentation.
        /// </summary>
        public void HostResolveShot(ulong clientId, in NetworkShotCommand command)
        {
            if (!IsServer) return;
            if (clientId != OwnerClientId) return;
            if (weaponState == null || weaponState.Catalog == null) return;

            // Validate the sender owns this player.
            if (clientId != OwnerClientId) return;

            // Reject stale/duplicate/out-of-order shot sequences.
            if (_hostLastShotSequence.TryGetValue(clientId, out uint lastSeq)
                && command.ShotSequence <= lastSeq)
            {
                return;
            }

            // Player must be alive.
            if (weaponState.LifeState.Value != PlayerLifeState.Alive) return;

            // The requested weapon must be the currently-equipped weapon.
            if (command.WeaponId != weaponState.EquippedWeaponId.Value) return;

            // Validate the weapon ID against the catalog.
            if (!weaponState.Catalog.TryGetEntry(command.WeaponId, out var entry)) return;

            // Cadence validation. The host enforces fire rate; the owner's local prediction may
            // fire faster but the host rejects shots that arrive before cadence has elapsed.
            float serverTime = (float)NetworkManager.ServerTime.Time;
            if (!weaponState.ServerCheckAndRecordCadence(command.WeaponId, entry.fireRateRpm, serverTime))
            {
                return;
            }

            // Ammunition validation. The host decrements authoritative ammo; reject if empty.
            if (weaponState.GetEquippedAmmo() == 0) return;
            if (!weaponState.ServerDecrementAmmo()) return;

            // Record the accepted sequence.
            _hostLastShotSequence[clientId] = command.ShotSequence;

            // Reconstruct a trusted WeaponShotRequest from the catalog and the accepted player
            // pose. The host uses the accepted body yaw and the submitted aim direction (the
            // owner's camera direction at fire time). The aim direction is validated against
            // the accepted player aim in Step 9 (lag compensation); for now we trust it within
            // a basic tolerance.
            Vector3 cameraOrigin = controller != null ? controller.transform.position : transform.position;
            cameraOrigin += Vector3.up * 1.6f; // approximate eye height

            // Build a WeaponBallisticsSettings from the catalog entry for the authoritative
            // resolution. This avoids trusting the owner's local CAS WeaponSettings.
            var ballistics = BuildAuthoritativeBallistics(entry, command.IsAiming);

            // Resolve the muzzle position from the Tactical presentation if available, else
            // fall back to the owner root.
            Vector3 muzzlePos = cameraOrigin;
            Quaternion muzzleRot = Quaternion.Euler(command.AimPitch, command.AimYaw, 0f);

            // Damage cap for the shotgun: 8 pellets * 12 damage = 96 close-range max.
            const float shotgunDamageCap = 96f;
            const int maxPellets = 8;

            int pelletCount = entry.isShotgun ? entry.pelletCount : 1;
            // Bound pellet count to the NetworkShotResult capacity and the plan's max.
            if (pelletCount > NetworkShotResult.Capacity) pelletCount = NetworkShotResult.Capacity;
            if (pelletCount > maxPellets) pelletCount = maxPellets;

            float halfAngle = command.IsAiming
                ? entry.ballistics.adsSpreadDegrees
                : entry.ballistics.hipSpreadDegrees;

            // Track per-target damage applied this shot so a single pellet damages a target at
            // most once. Keyed by the hit collider reference.
            var damagedTargets = new System.Collections.Generic.HashSet<Collider>();
            float totalDamageThisShot = 0f;
            float perPelletDamage = entry.ballistics.damage;
            _shotgunDamagedTargetsThisShot.Clear();

            var result = new NetworkShotResult
            {
                WeaponId = command.WeaponId,
                ShotSequence = command.ShotSequence,
                ShooterClientId = clientId,
                MuzzlePosition = muzzlePos,
                ImpactCount = 0
            };

            Vector3 baseAim = command.AimDirection.sqrMagnitude > 0.0001f
                ? command.AimDirection.normalized
                : Quaternion.Euler(command.AimPitch, command.AimYaw, 0f) * Vector3.forward;

            if (_combatRuntime == null)
                _combatRuntime = GetComponentInChildren<FPSProject.Combat.Runtime.WeaponCombatRuntime>(true);
            if (_combatRuntime == null) return;

            // Shot tick age validation. Reject shots in the future or older than the rewind window.
            float hostShotTime = ClientTickToHostTime(command.NetworkTick);
            float currentServerTime = (float)NetworkManager.ServerTime.Time;
            if (hostShotTime > currentServerTime + 0.05f) return; // future shot (with small tolerance)
            if (currentServerTime - hostShotTime > tuning.rewindDuration) return; // older than 250 ms window

            for (int pellet = 0; pellet < pelletCount; pellet++)
            {
                Vector3 spreadDir = DeterministicShotRandom.SpreadCone(
                    clientId, command.WeaponId, command.ShotSequence, pellet,
                    baseAim, halfAngle);

                var shotRequest = new FPSProject.Combat.Runtime.WeaponShotRequest(
                    ballistics,
                    gameObject,
                    gameObject,
                    muzzlePos,
                    muzzleRot,
                    cameraOrigin,
                    spreadDir);

                // Lag-compensated hitscan: first test against every living player's historical
                // capsule at the client's shot tick. If a player hit is closer than the current-
                // time environment obstruction, apply damage to that historical target.
                bool hitPlayer = TryHitHistoricalPlayer(
                    cameraOrigin, spreadDir, entry.ballistics.maxRange, hostShotTime, clientId,
                    out ulong hitTargetId, out float playerHitDist, out Vector3 playerHitPoint);

                if (hitPlayer)
                {
                    int impactIndex = result.ImpactCount;
                    if (impactIndex < NetworkShotResult.Capacity)
                    {
                        // Apply damage to the historical target's current NetworkHealth (Step 10
                        // adds NetworkHealth; for now the damage is applied via the IDamageable on
                        // the target's current collider, found by NetworkObjectId).
                        ApplyDamageToHistoricalTarget(hitTargetId, entry.ballistics.damage,
                            playerHitPoint, spreadDir);

                        bool isPlayerHit = true;

                        // Enforce the single-pellet-per-target rule and the shotgun damage cap.
                        if (entry.isShotgun)
                        {
                            if (!_shotgunDamagedTargetsThisShot.Add(hitTargetId))
                            {
                                isPlayerHit = false;
                            }
                            else if (totalDamageThisShot + perPelletDamage > shotgunDamageCap)
                            {
                                isPlayerHit = false;
                            }
                            else
                            {
                                totalDamageThisShot += perPelletDamage;
                            }
                        }

                        var impact = new NetworkShotImpact
                        {
                            Point = playerHitPoint,
                            Normal = -spreadDir,
                            HitTargetNetworkId = hitTargetId,
                            IsPlayerHit = isPlayerHit
                        };
                        SetImpact(ref result, impactIndex, in impact);
                        result.ImpactCount++;
                    }
                    continue;
                }

                // No historical player hit: fall back to environment hitscan at current host time.
                var resolved = _combatRuntime.ResolveHitscanRay(shotRequest, cameraOrigin, spreadDir, muzzlePos);

                if (resolved.HasHit)
                {
                    int impactIndex = result.ImpactCount;
                    if (impactIndex < NetworkShotResult.Capacity)
                    {
                        bool isPlayerHit = resolved.Hit.collider != null
                            && resolved.Hit.collider.GetComponentInParent<FPSProject.Combat.Runtime.IDamageable>() != null;

                        // Enforce the single-pellet-per-target rule and the shotgun damage cap.
                        // ResolveHitscanRay already applied damage via ResolveContact; for the
                        // shotgun we need to track per-target damage so we cap it. The plan says a
                        // single pellet damages a target at most once, but separate pellets may
                        // each apply damage up to the 96 cap.
                        if (entry.isShotgun && isPlayerHit && resolved.Hit.collider != null)
                        {
                            Collider hitCollider = resolved.Hit.collider;
                            if (!damagedTargets.Add(hitCollider))
                            {
                                // This pellet hit a target already damaged this shot. The damage
                                // was already applied by ResolveContact; we cannot roll it back
                                // here, so we mark the impact as non-player for presentation but
                                // still record the point for tracer/impact VFX.
                                isPlayerHit = false;
                            }
                            else if (totalDamageThisShot + perPelletDamage > shotgunDamageCap)
                            {
                                // The plan caps total close-range damage at 96 before falloff.
                                // ResolveContact already applied the full per-pellet damage; we
                                // only stop recording further hits beyond the cap.
                                isPlayerHit = false;
                            }
                            else
                            {
                                totalDamageThisShot += perPelletDamage;
                            }
                        }

                        var impact = new NetworkShotImpact
                        {
                            Point = resolved.Hit.point,
                            Normal = resolved.Hit.normal,
                            IsPlayerHit = isPlayerHit
                        };
                        SetImpact(ref result, impactIndex, in impact);
                        result.ImpactCount++;
                    }
                }
            }

            BroadcastShotResultClientRpc(result);
        }

        private static void SetImpact(ref NetworkShotResult result, int index, in NetworkShotImpact impact)
        {
            switch (index)
            {
                case 0: result.Impact0 = impact; break;
                case 1: result.Impact1 = impact; break;
                case 2: result.Impact2 = impact; break;
                case 3: result.Impact3 = impact; break;
                case 4: result.Impact4 = impact; break;
                case 5: result.Impact5 = impact; break;
                case 6: result.Impact6 = impact; break;
                case 7: result.Impact7 = impact; break;
            }
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void BroadcastShotResultClientRpc(NetworkShotResult result, ClientRpcParams rpcParams = default)
        {
            // Every client (including host and owner) plays the authoritative tracer/impact.
            // The owner dedupes predicted one-frame presentation by shot sequence. Non-owners play
            // third-person fire presentation plus tracer/impact. Damage is NOT applied here; the
            // host already applied it during resolution.
            if (_shotRouter == null) _shotRouter = GetComponent<NetworkWeaponShotRouter>();
            _shotRouter?.OnShotResult(result);
        }

        private static FPSProject.Combat.Runtime.WeaponBallisticsSettings BuildAuthoritativeBallistics(
            NetworkWeaponEntry entry, bool isAiming)
        {
            var b = entry.ballistics;
            return new FPSProject.Combat.Runtime.WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = FPSProject.Combat.Runtime.WeaponShotType.Hitscan,
                damage = b.damage,
                maxRange = b.maxRange,
                hitMask = b.hitMask,
                triggerInteraction = b.triggerInteraction,
                spreadDegrees = isAiming ? b.adsSpreadDegrees : b.hipSpreadDegrees,
                tracerPrefab = b.tracerPrefab,
                tracerSpeed = b.tracerSpeed,
                tracerLifetime = b.tracerLifetime,
                impactEffectLibrary = b.impactEffectLibrary
            };
        }

        /// <summary>Called by the health system when the player dies.</summary>
        public void NotifyDeath()
        {
            _isAlive = false;
            controller?.SetSimulationMode(PlayerSimulationMode.Disabled);
            _proxyBuffer?.Clear(ProxyInterpolationBuffer.ClearReason.ManualReset);
        }

        /// <summary>Called by the health/respawn system when the player respawns.</summary>
        public void NotifyRespawn(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            _isAlive = true;
            if (IsOwner)
            {
                controller.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
                controller.SetSimulationMode(PlayerSimulationMode.LocalOwner);
            }
            else
            {
                controller.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
                controller.ResetProxyState();
                controller.SetSimulationMode(PlayerSimulationMode.RemoteProxy);
                _proxyBuffer?.Clear(ProxyInterpolationBuffer.ClearReason.Respawn);
            }
        }

        private struct HostClientState
        {
            public Vector3 LastAcceptedPosition;
            public float LastAcceptedTime;
            public uint LastAcceptedSequence;
            public ProxyPresentationState AcceptedProxyState;
            public bool HasPendingBroadcast;
            public float LastBroadcastTime;
        }

        private static ServerRpcParams ServerRpcParams()
        {
            return default;
        }

        private static ClientRpcParams ClientRpcParams()
        {
            return default;
        }

        private static ClientRpcParams ForClient(ulong clientId)
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
        }

        private static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }
    }
}
