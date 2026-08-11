using System.Collections.Generic;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// A single historical pose sample for one player, used for lag-compensated hitscan. The
    /// host records these from the accepted movement timeline; the shot resolver interpolates
    /// between them to reconstruct each target's capsule at the client's shot tick.
    /// </summary>
    public struct HitboxPoseSample
    {
        /// <summary>Host network time (seconds) this sample was recorded.</summary>
        public float Time;

        /// <summary>Accepted root position at this time.</summary>
        public Vector3 Position;

        /// <summary>Accepted body yaw in degrees at this time.</summary>
        public float BodyYaw;

        /// <summary>Accepted camera aim yaw in degrees at this time.</summary>
        public float AimYaw;

        /// <summary>Accepted camera aim pitch in degrees at this time.</summary>
        public float AimPitch;

        /// <summary>Whether the host accepted ADS as active at this time.</summary>
        public bool IsAiming;

        /// <summary>Capsule center in world space at this time.</summary>
        public Vector3 CapsuleCenter;

        /// <summary>Capsule height at this time (reduced while crouching).</summary>
        public float CapsuleHeight;

        /// <summary>Capsule radius at this time.</summary>
        public float CapsuleRadius;

        /// <summary>True when the player was crouching at this time.</summary>
        public bool IsCrouching;
    }

    /// <summary>
    /// A capsule reconstructed at a historical time for ray intersection testing.
    /// </summary>
    public struct HistoricalCapsule
    {
        public Vector3 Top;
        public Vector3 Bottom;
        public float Radius;
        public Vector3 Center;
        public float Height;
    }

    /// <summary>
    /// Host-side rolling history of accepted poses for one player, used for lag-compensated
    /// hitscan. Maintains up to <see cref="HistoryDuration"/> seconds of samples and provides
    /// analytical ray-versus-capsule intersection at an arbitrary historical time.
    /// </summary>
    public class NetworkHitboxHistory
    {
        private readonly float _historyDuration;
        private readonly List<HitboxPoseSample> _samples = new List<HitboxPoseSample>();

        public NetworkHitboxHistory(float historyDuration)
        {
            _historyDuration = Mathf.Max(0.1f, historyDuration);
        }

        /// <summary>Number of samples currently stored.</summary>
        public int Count => _samples.Count;

        /// <summary>Oldest sample time, or float.MaxValue when empty.</summary>
        public float OldestTime => _samples.Count > 0 ? _samples[0].Time : float.MaxValue;

        /// <summary>Newest sample time, or float.MinValue when empty.</summary>
        public float NewestTime => _samples.Count > 0 ? _samples[_samples.Count - 1].Time : float.MinValue;

        /// <summary>
        /// Record a new accepted pose sample. Prunes samples older than the history window.
        /// </summary>
        public void Record(float time, in HitboxPoseSample sample)
        {
            // Maintain ascending time order. If the new sample is not newer than the last, replace
            // the tail to keep the timeline monotonic.
            if (_samples.Count > 0 && time <= _samples[_samples.Count - 1].Time)
            {
                var tail = _samples[_samples.Count - 1];
                tail = sample;
                _samples[_samples.Count - 1] = tail;
            }
            else
            {
                _samples.Add(sample);
            }

            PruneOlderThan(time - _historyDuration);
        }

        /// <summary>Clear all samples.</summary>
        public void Clear()
        {
            _samples.Clear();
        }

        /// <summary>
        /// True when <paramref name="time"/> is within the recorded history window
        /// (between the oldest and newest sample, inclusive, and within HistoryDuration of the
        /// newest).
        /// </summary>
        public bool IsTimeInWindow(float time)
        {
            if (_samples.Count == 0) return false;
            if (time < OldestTime) return false;
            if (time > NewestTime) return false;
            return NewestTime - time <= _historyDuration;
        }

        /// <summary>
        /// Reconstruct the target's capsule at the given historical time by interpolating the
        /// two bracketing samples. Returns false when the time is outside the recorded window.
        /// </summary>
        public bool TryGetCapsule(float time, out HistoricalCapsule capsule)
        {
            capsule = default;
            if (!TryGetPose(time, out HitboxPoseSample pose)) return false;
            capsule = BuildCapsule(pose);
            return true;
        }

        /// <summary>
        /// Reconstruct the complete accepted pose at a historical time. This is used to validate
        /// shot aim against the same server-accepted timeline used for hitbox rewind.
        /// </summary>
        public bool TryGetPose(float time, out HitboxPoseSample pose)
        {
            pose = default;
            if (_samples.Count == 0) return false;
            if (time < OldestTime || time > NewestTime) return false;
            if (NewestTime - time > _historyDuration) return false;

            // Find the bracketing pair.
            int upper = FindUpperIndex(time);
            if (upper <= 0)
            {
                // time is at or before the oldest sample; clamp to oldest.
                pose = _samples[0];
                return true;
            }

            HitboxPoseSample a = _samples[upper - 1];
            HitboxPoseSample b = _samples[upper];
            float span = b.Time - a.Time;
            float t = span > 0.0001f ? (time - a.Time) / span : 0f;
            t = Mathf.Clamp01(t);

            pose = new HitboxPoseSample
            {
                Time = time,
                Position = Vector3.Lerp(a.Position, b.Position, t),
                BodyYaw = Mathf.LerpAngle(a.BodyYaw, b.BodyYaw, t),
                AimYaw = Mathf.LerpAngle(a.AimYaw, b.AimYaw, t),
                AimPitch = Mathf.LerpAngle(a.AimPitch, b.AimPitch, t),
                CapsuleCenter = Vector3.Lerp(a.CapsuleCenter, b.CapsuleCenter, t),
                CapsuleHeight = Mathf.Lerp(a.CapsuleHeight, b.CapsuleHeight, t),
                CapsuleRadius = Mathf.Lerp(a.CapsuleRadius, b.CapsuleRadius, t),
                IsCrouching = t < 0.5f ? a.IsCrouching : b.IsCrouching,
                // Discrete fire state must never be granted before the accepted sample that
                // contains it. At the upper sample's exact timestamp, use that newer value.
                IsAiming = t < 1f ? a.IsAiming : b.IsAiming
            };
            return true;
        }

        private HistoricalCapsule BuildCapsule(in HitboxPoseSample sample)
        {
            // Capsule aligned to world Y at the recorded center/height.
            float halfHeight = sample.CapsuleHeight * 0.5f;
            float radius = sample.CapsuleRadius;
            // The capsule's two sphere centers are at top and bottom, inset by the radius so the
            // cylinder section connects them.
            float tubeHalf = Mathf.Max(0f, halfHeight - radius);
            Vector3 center = sample.CapsuleCenter;
            return new HistoricalCapsule
            {
                Center = center,
                Top = center + Vector3.up * tubeHalf,
                Bottom = center - Vector3.up * tubeHalf,
                Radius = radius,
                Height = sample.CapsuleHeight
            };
        }

        private int FindUpperIndex(float time)
        {
            // Binary search for the first sample with Time >= query.
            int lo = 0;
            int hi = _samples.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_samples[mid].Time < time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private void PruneOlderThan(float cutoff)
        {
            int i = 0;
            while (i < _samples.Count && _samples[i].Time < cutoff) i++;
            if (i > 0) _samples.RemoveRange(0, i);
        }

        /// <summary>
        /// Analytical ray-versus-capsule intersection test. Returns true and the hit distance
        /// when the ray intersects the capsule; false otherwise. Does not allocate.
        /// </summary>
        public static bool RaycastCapsule(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            in HistoricalCapsule capsule,
            float maxDistance,
            out float hitDistance)
        {
            hitDistance = float.MaxValue;

            Vector3 axis = capsule.Top - capsule.Bottom;
            float axisLenSq = axis.sqrMagnitude;
            if (axisLenSq < 1e-8f)
            {
                // Degenerate: treat as a sphere.
                return RaycastSphere(rayOrigin, rayDirection, capsule.Top, capsule.Radius, maxDistance, out hitDistance);
            }

            // Project the ray against the infinite capsule line. Solve for the closest
            // approach between the ray and the capsule axis segment.
            Vector3 ab = axis; // capsule axis
            Vector3 ao = rayOrigin - capsule.Bottom; // from capsule bottom to ray origin
            Vector3 rd = rayDirection;

            float abDotRd = Vector3.Dot(ab, rd);
            float abDotAb = axisLenSq;
            float abDotAo = Vector3.Dot(ab, ao);
            float rdDotAo = Vector3.Dot(rd, ao);

            // Solve for t (ray param) and s (axis param) minimizing |P(t) - Q(s)|^2.
            // P(t) = rayOrigin + t*rd ; Q(s) = capsule.Bottom + s*ab.
            // d = P - Q = ao + t*rd - s*ab.
            // Minimize |d|^2 -> partials zero:
            //   d/dt: 2 * rd . (ao + t*rd - s*ab) = 0  ->  rd.rd * t - rd.ab * s = -rd.ao
            //   d/ds: -2 * ab . (ao + t*rd - s*ab) = 0  ->  ab.rd * t - ab.ab * s = -ab.ao
            float rdDotRd = Vector3.Dot(rd, rd);
            float denom = rdDotRd * (-abDotAb) - (-abDotRd) * abDotRd;
            // denom = -(rd.rd * ab.ab - (rd.ab)^2)

            float t, s;
            if (Mathf.Abs(denom) < 1e-10f)
            {
                // Ray is parallel to the capsule axis. Use any t and clamp s.
                t = 0f;
                s = Mathf.Clamp(-abDotAo / abDotAb, 0f, 1f);
            }
            else
            {
                // Solve the 2x2 system.
                //   rd.rd * t - rd.ab * s = -rd.ao
                //   ab.rd * t - ab.ab * s = -ab.ao
                // Using Cramer's rule:
                t = ((-rdDotAo) * (-abDotAb) - (-abDotRd) * (-abDotAo)) / denom;
                s = (rdDotRd * (-abDotAo) - abDotRd * (-rdDotAo)) / denom;
            }

            // Clamp s to the segment and recompute the closest point.
            s = Mathf.Clamp(s, 0f, 1f);
            Vector3 closestOnAxis = capsule.Bottom + ab * s;
            Vector3 closestOnRay = rayOrigin + rd * t;

            // If t is negative or beyond maxDistance, the closest approach is outside the ray.
            // Clamp t to [0, maxDistance] and recompute distance.
            if (t < 0f)
            {
                t = 0f;
                closestOnRay = rayOrigin;
            }
            else if (t > maxDistance)
            {
                t = maxDistance;
                closestOnRay = rayOrigin + rd * maxDistance;
            }

            Vector3 diff = closestOnRay - closestOnAxis;
            float distSq = diff.sqrMagnitude;
            float radius = capsule.Radius;

            if (distSq > radius * radius) return false;

            // The ray enters the capsule at a distance closer than the closest-approach t. Compute
            // the entry distance analytically: along the ray, the capsule is a "thick" line. The
            // perpendicular distance from the ray to the axis is sqrt(distSq) at the closest
            // approach; the entry is t minus the half-chord along the ray.
            float perp = Mathf.Sqrt(Mathf.Max(0f, distSq));
            float chord = Mathf.Sqrt(Mathf.Max(0f, radius * radius - perp * perp));

            // The entry point is the closest-approach t minus the projection of the chord onto
            // the ray direction. For a near-parallel ray, the chord is along the ray.
            float entry = t - chord;
            if (entry < 0f)
            {
                // Ray origin is inside the capsule; the hit is at distance 0.
                hitDistance = 0f;
                return true;
            }
            if (entry > maxDistance) return false;

            hitDistance = entry;
            return true;
        }

        private static bool RaycastSphere(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 center,
            float radius,
            float maxDistance,
            out float hitDistance)
        {
            hitDistance = float.MaxValue;
            Vector3 oc = rayOrigin - center;
            float b = Vector3.Dot(oc, rayDirection);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float sq = Mathf.Sqrt(disc);
            float t = -b - sq;
            if (t < 0f) t = -b + sq;
            if (t < 0f || t > maxDistance) return false;
            hitDistance = t;
            return true;
        }
    }
}
