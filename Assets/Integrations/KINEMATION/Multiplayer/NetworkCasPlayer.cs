using System;
using System.Collections;
using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Movement;
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

        public override void OnNetworkSpawn()
        {
            if (controller == null) controller = GetComponent<NetworkFPSExampleController>();
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

            controller?.SetSimulationMode(PlayerSimulationMode.Disabled);
            _proxyBuffer?.Clear(ProxyInterpolationBuffer.ClearReason.ManualReset);
            _hostStates.Clear();
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

                int networkTick = (int)NetworkManager.LocalTime.Tick;
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
            uint acceptedSeq = state.LastAcceptedSequence + 1;
            ProxyPresentationState proxy = BuildProxyFromSample(sample, acceptedSeq, _isAlive);

            state.LastAcceptedPosition = sample.Position;
            state.LastAcceptedTime = Time.time;
            state.LastAcceptedSequence = acceptedSeq;
            state.AcceptedProxyState = proxy;
            state.HasPendingBroadcast = true;
            _hostStates[clientId] = state;

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
            TacticalShooterPlayer tacticalPlayer
                = tacticalChild.GetComponent<TacticalShooterPlayer>();
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

        /// <summary>Called by the health system when the player dies.</summary>
        public void NotifyDeath()
        {
            _isAlive = false;
            controller?.SetSimulationMode(PlayerSimulationMode.Disabled);
        }

        /// <summary>Called by the health/respawn system when the player respawns.</summary>
        public void NotifyRespawn(Vector3 spawnPosition)
        {
            _isAlive = true;
            if (IsOwner)
            {
                controller.transform.position = spawnPosition;
                controller.SetSimulationMode(PlayerSimulationMode.LocalOwner);
            }
            else
            {
                controller.transform.position = spawnPosition;
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
