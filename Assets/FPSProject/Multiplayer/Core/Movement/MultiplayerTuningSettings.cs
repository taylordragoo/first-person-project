using UnityEngine;

namespace FPSProject.Multiplayer.Core.Movement
{
    /// <summary>
    /// Project-owned tuning for movement synchronization, interpolation, and host validation.
    /// All values that could be hard-coded constants live here so designers can iterate without
    /// touching code. Defaults match the plan's initial values.
    /// </summary>
    [CreateAssetMenu(fileName = "MultiplayerTuningSettings",
        menuName = "FPSProject/Multiplayer/Multiplayer Tuning Settings")]
    public class MultiplayerTuningSettings : ScriptableObject
    {
        [Header("Send Rates")]
        [Tooltip("Owner-to-host and host-to-proxy motion send rate in Hz.")]
        [Min(1f)] public float motionSendRate = 20f;

        [Header("Interpolation")]
        [Tooltip("Remote proxies render this many seconds behind the latest accepted host state.")]
        [Min(0f)] public float interpolationDelay = 0.1f;

        [Tooltip("Maximum seconds to extrapolate when no new sample arrives before holding the last state.")]
        [Min(0f)] public float extrapolationCap = 0.1f;

        [Header("Correction Thresholds (owner reconciliation)")]
        [Tooltip("Divergence in meters that triggers a reliable smooth correction to the owner.")]
        [Min(0f)] public float softCorrectionThreshold = 0.75f;

        [Tooltip("Divergence in meters that triggers a hard snap and interpolation buffer reset.")]
        [Min(0f)] public float hardCorrectionThreshold = 2f;

        [Tooltip("Duration in seconds over which a soft correction is smoothed.")]
        [Min(0.01f)] public float correctionSmoothDuration = 0.1f;

        [Header("Movement Validation")]
        [Tooltip("Extra displacement grace in meters applied on top of speedLimit * elapsedTime.")]
        [Min(0f)] public float movementValidationGrace = 0.35f;

        [Tooltip("Maximum speed in m/s the host permits before crouch/sprint multipliers. " +
                 "Derived from the jog gait velocity by default; raise only if your CAS rig moves faster.")]
        [Min(0f)] public float baseSpeedLimit = 3f;

        [Tooltip("Maximum speed multiplier applied when sprinting with forward input.")]
        [Min(1f)] public float sprintSpeedMultiplier = 1.67f;

        [Tooltip("Maximum speed multiplier applied while crouching.")]
        [Min(0.01f)] public float crouchSpeedMultiplier = 0.55f;

        [Header("World Bounds")]
        [Tooltip("Center of the playable world bounds used to reject out-of-world samples.")]
        public Vector3 worldBoundsCenter = Vector3.zero;

        [Tooltip("Size of the playable world bounds. Samples outside [center - size/2, center + size/2] are rejected.")]
        public Vector3 worldBoundsSize = new Vector3(500f, 200f, 500f);

        [Header("Rewind")]
        [Tooltip("Seconds of host-side accepted pose history retained for lag-compensated hitscan.")]
        [Min(0f)] public float rewindDuration = 0.25f;

        [Tooltip("Maximum age in seconds of an accepted shot command before it is rejected.")]
        [Min(0f)] public float maxShotCommandAge = 0.25f;

        /// <summary>Half-extents of the world bounds, for fast containment tests.</summary>
        public Vector3 WorldBoundsExtents => worldBoundsSize * 0.5f;

        /// <summary>True if <paramref name="position"/> is inside the configured world bounds.</summary>
        public bool IsInsideWorldBounds(Vector3 position)
        {
            Vector3 min = worldBoundsCenter - WorldBoundsExtents;
            Vector3 max = worldBoundsCenter + WorldBoundsExtents;
            return position.x >= min.x && position.x <= max.x
                && position.y >= min.y && position.y <= max.y
                && position.z >= min.z && position.z <= max.z;
        }
    }
}