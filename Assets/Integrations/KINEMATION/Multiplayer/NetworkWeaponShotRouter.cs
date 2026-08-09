using System.Collections.Generic;
using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Network owner-side shot router. Converts a local <see cref="WeaponShotRequest"/> into a
    /// <see cref="NetworkShotCommand"/> and sends it to the host. The owner never applies local
    /// damage; the host validates and resolves authoritatively, then broadcasts
    /// <see cref="NetworkShotResult"/> so every client (including the owner) plays presentation.
    /// Predicted one-frame presentation (recoil, muzzle flash, casing, fire animation, fire
    /// audio) is played locally on the owner and cached by shot sequence so the confirmed
    /// <see cref="NetworkShotResult"/> does not replay it.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkWeaponShotRouter : MonoBehaviour, IWeaponShotRouter
    {
        private NetworkCasPlayer _networkCasPlayer;
        private NetworkWeaponState _weaponState;
        private NetworkTacticalShooterPlayer _tacticalPlayer;
        private uint _shotSequence;

        // Predicted presentation cache keyed by shot sequence. Step 8 populates and consumes
        // this to avoid double-playing recoil/muzzle/audio when the host confirms a shot.
        private readonly Dictionary<uint, PredictedShot> _predictedShots = new Dictionary<uint, PredictedShot>();

        // Maximum predicted shots to retain before pruning oldest.
        private const int MaxPredictedCache = 64;

        public bool IsOwner => _networkCasPlayer != null && _networkCasPlayer.IsOwner;
        public uint ShotSequence => _shotSequence;

        private void Awake()
        {
            _networkCasPlayer = GetComponent<NetworkCasPlayer>();
            _weaponState = GetComponent<NetworkWeaponState>();
            _tacticalPlayer = GetComponentInChildren<NetworkTacticalShooterPlayer>(true);
        }

        /// <summary>
        /// Route a local shot request. Called by <c>WeaponProp.SubmitCombatShot()</c> after it
        /// built the request from the local camera/muzzle. On the owner, this increments the
        /// shot sequence, plays predicted one-frame presentation, caches it by sequence, and
        /// sends the <see cref="NetworkShotCommand"/> to the host. It does NOT apply local damage.
        /// </summary>
        public void SubmitShot(
            in WeaponShotRequest request,
            ushort weaponId,
            uint shotSequence,
            int networkTick,
            float aimYaw,
            float aimPitch,
            bool isAiming)
        {
            if (_networkCasPlayer == null || !_networkCasPlayer.IsOwner)
            {
                // Proxies cannot submit shots. Defensive guard; WeaponProp should be disabled on
                // proxies, but if it fires anyway we silently drop the shot.
                return;
            }

            // Use the router-owned sequence if the caller passed 0, otherwise trust the caller.
            // The caller (WeaponProp path) does not own the sequence; the router does.
            uint seq = _shotSequence + 1;
            _shotSequence = seq;

            // Play predicted one-frame presentation: recoil, muzzle flash, casing, fire
            // animation, and fire audio. This runs on the owner immediately for responsiveness.
            // When the host's confirmation arrives, OnShotResult skips replaying these.
            PlayPredictedFirePresentation(weaponId);

            // Cache the prediction by sequence so the host's confirmation does not double-play.
            RecordPredictedShot(seq, weaponId, isAiming);

            var command = new NetworkShotCommand
            {
                WeaponId = weaponId,
                ShotSequence = seq,
                NetworkTick = networkTick,
                AimYaw = aimYaw,
                AimPitch = aimPitch,
                AimDirection = request.CameraDirection.normalized,
                IsAiming = isAiming
            };

            SubmitShotServerRpc(command);
        }

        /// <summary>
        /// Play the owner's predicted one-frame fire presentation: recoil (via the shared
        /// RecoilAnimation), camera shake, Tactical muzzle flash, casing, fire animation, and
        /// fire audio. CAS's shared recoil response runs once per local predicted shot. The
        /// Tactical presentation methods must not invoke a second shared recoil or camera-shake
        /// path.
        /// </summary>
        private void PlayPredictedFirePresentation(ushort weaponId)
        {
            if (_tacticalPlayer == null) return;
            var presentation = _tacticalPlayer.GetActiveNetworkWeaponPresentation();
            presentation?.PlayNetworkFirePresentation();
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable, RequireOwnership = true)]
        private void SubmitShotServerRpc(NetworkShotCommand command, ServerRpcParams rpcParams = default)
        {
            // ServerRpc bodies only run on the server/host. No explicit IsServer guard needed.
            ulong clientId = rpcParams.Receive.SenderClientId;
            // Delegate to the host-side authoritative resolver. The host validates the command
            // against the catalog, accepted pose, cadence, and ammunition, then resolves
            // damage and broadcasts NetworkShotResult. The host resolver lives on
            // NetworkCasPlayer so it has access to the host-side pose history.
            if (_networkCasPlayer != null)
            {
                _networkCasPlayer.HostResolveShot(clientId, in command);
            }
        }

        /// <summary>
        /// Called on the owner and all clients when the host broadcasts a
        /// <see cref="NetworkShotResult"/>. The owner reconciles predicted presentation; every
        /// client plays tracer/impact presentation. Damage is not applied here.
        /// </summary>
        public void OnShotResult(in NetworkShotResult result)
        {
            // Owner: dedupe predicted presentation by shot sequence. The predicted one-frame fire
            // presentation (recoil, muzzle, casing, animation, audio) was already played locally
            // when the shot was submitted. Do NOT replay it on confirmation.
            if (_networkCasPlayer != null && _networkCasPlayer.IsOwner)
            {
                if (_predictedShots.TryGetValue(result.ShotSequence, out var predicted))
                {
                    _predictedShots.Remove(result.ShotSequence);
                }
            }
            else
            {
                // Non-owning clients (and the host for remote owners) play the third-person fire
                // presentation plus tracer/impact results, without local camera shake or recoil
                // input. CAS's shared recoil runs once per local predicted shot on the owner only.
                if (_tacticalPlayer != null)
                {
                    var presentation = _tacticalPlayer.GetActiveNetworkWeaponPresentation();
                    presentation?.PlayNetworkFirePresentation();
                }
            }

            // Every client (including host and owner): play tracer + impact VFX.
            PlayShotResultPresentation(in result);
        }

        /// <summary>
        /// Play the authoritative tracer and impact presentation for a confirmed shot. Called
        /// on every client; does not apply damage. The host already applied damage during
        /// resolution. Tracers and impacts are spawned through the local WeaponCombatRuntime
        /// using the catalog's VFX assets.
        /// </summary>
        private void PlayShotResultPresentation(in NetworkShotResult result)
        {
            if (_weaponState == null || _weaponState.Catalog == null) return;
            if (!_weaponState.Catalog.TryGetEntry(result.WeaponId, out var entry)) return;

            if (_combatRuntime == null)
                _combatRuntime = GetComponentInChildren<FPSProject.Combat.Runtime.WeaponCombatRuntime>(true);
            if (_combatRuntime == null) return;

            var ballistics = BuildPresentationBallistics(entry);

            // Spawn tracer from muzzle to the first impact, or to max range if no hits.
            Vector3 endpoint = result.ImpactCount > 0
                ? GetImpact(result, 0).Point
                : result.MuzzlePosition + (result.MuzzlePosition.sqrMagnitude > 0f
                    ? Vector3.forward * entry.ballistics.maxRange
                    : Vector3.zero);

            // Build a presentation-only request (no damage; combatEnabled triggers tracer spawn).
            var presentationRequest = new FPSProject.Combat.Runtime.WeaponShotRequest(
                ballistics,
                gameObject,
                gameObject,
                result.MuzzlePosition,
                Quaternion.LookRotation(endpoint - result.MuzzlePosition),
                result.MuzzlePosition,
                (endpoint - result.MuzzlePosition).normalized);

            // Use PlayShotResult with a synthesized result so tracers spawn. Impacts are spawned
            // individually below from the authoritative impact points.
            var synth = new FPSProject.Combat.Runtime.WeaponCombatRuntime.AuthoritativeShotResult
            {
                MuzzlePosition = result.MuzzlePosition,
                Endpoint = endpoint,
                HasHit = false // We spawn impacts manually below so each pellet gets its own.
            };
            _combatRuntime.PlayShotResult(presentationRequest, in synth);

            // Spawn per-impact VFX at each authoritative impact point. The host already applied
            // damage; clients only play decal/impact effects. PlayShotResult spawns the tracer
            // above; here we spawn individual impact effects for each pellet.
            for (int i = 0; i < result.ImpactCount && i < NetworkShotResult.Capacity; i++)
            {
                var impact = GetImpact(result, i);
                SpawnImpactVfx(entry, in impact);
            }
        }

        private FPSProject.Combat.Runtime.WeaponCombatRuntime _combatRuntime;

        private void SpawnImpactVfx(NetworkWeaponEntry entry, in NetworkShotImpact impact)
        {
            if (_combatRuntime == null || entry.ballistics.impactEffectLibrary == null) return;

            ImpactSurfaceType surfaceType = System.Enum.IsDefined(
                typeof(ImpactSurfaceType), (int)impact.SurfaceType)
                ? (ImpactSurfaceType)impact.SurfaceType
                : ImpactSurfaceType.Default;

            Transform hitTransform = null;
            if (impact.HitTargetNetworkId != 0 && NetworkManager.Singleton != null &&
                NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    impact.HitTargetNetworkId, out NetworkObject hitTarget))
            {
                hitTransform = hitTarget.transform;
            }

            _combatRuntime.PlayImpact(
                entry.ballistics.impactEffectLibrary,
                impact.Point,
                impact.Normal,
                surfaceType,
                hitTransform);
        }

        private static FPSProject.Combat.Runtime.WeaponBallisticsSettings BuildPresentationBallistics(
            NetworkWeaponEntry entry)
        {
            var b = entry.ballistics;
            return new FPSProject.Combat.Runtime.WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = FPSProject.Combat.Runtime.WeaponShotType.Hitscan,
                damage = 0f, // Presentation only; damage is not applied here.
                maxRange = b.maxRange,
                hitMask = b.hitMask,
                triggerInteraction = b.triggerInteraction,
                spreadDegrees = 0f, // No additional spread on playback.
                tracerPrefab = b.tracerPrefab,
                tracerSpeed = b.tracerSpeed,
                tracerLifetime = b.tracerLifetime,
                impactEffectLibrary = b.impactEffectLibrary
            };
        }

        private static NetworkShotImpact GetImpact(in NetworkShotResult result, int index)
        {
            switch (index)
            {
                case 0: return result.Impact0;
                case 1: return result.Impact1;
                case 2: return result.Impact2;
                case 3: return result.Impact3;
                case 4: return result.Impact4;
                case 5: return result.Impact5;
                case 6: return result.Impact6;
                case 7: return result.Impact7;
                default: return default;
            }
        }

        private void RecordPredictedShot(uint sequence, ushort weaponId, bool isAiming)
        {
            _predictedShots[sequence] = new PredictedShot
            {
                WeaponId = weaponId,
                IsAiming = isAiming
            };

            // Prune oldest entries to bound the cache.
            if (_predictedShots.Count > MaxPredictedCache)
            {
                uint oldest = uint.MaxValue;
                foreach (var key in _predictedShots.Keys)
                {
                    if (key < oldest) oldest = key;
                }
                if (oldest != uint.MaxValue) _predictedShots.Remove(oldest);
            }
        }

        /// <summary>
        /// Clear the predicted-shot cache. Called on respawn and hard corrections so stale
        /// predictions do not block future confirmation.
        /// </summary>
        public void ClearPredictedShots()
        {
            _predictedShots.Clear();
        }

        /// <summary>True if a predicted shot with the given sequence is cached.</summary>
        public bool HasPredictedShot(uint sequence) => _predictedShots.ContainsKey(sequence);

        private struct PredictedShot
        {
            public ushort WeaponId;
            public bool IsAiming;
        }
    }
}
