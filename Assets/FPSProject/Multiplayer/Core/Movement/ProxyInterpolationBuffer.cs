using System.Collections.Generic;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Movement
{
    /// <summary>
    /// Time-indexed buffer of accepted <see cref="ProxyPresentationState"/> snapshots used by
    /// remote proxies to interpolate and extrapolate motion. Rendered approximately
    /// <c>interpolationDelay</c> seconds behind the latest accepted host state, extrapolates
    /// for no more than <c>extrapolationCap</c> seconds, then holds the last state.
    /// </summary>
    public sealed class ProxyInterpolationBuffer
    {
        public struct Snapshot
        {
            public float LocalTime;
            public int NetworkTick;
            public ProxyPresentationState State;
        }

        public struct SampledState
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float BodyYaw;
            public float AimYaw;
            public float AimPitch;
            public float MoveX;
            public float MoveY;
            public float Gait;
            public bool IsGrounded;
            public bool IsInAir;
            public bool IsCrouching;
            public bool IsSprinting;
            public bool IsAiming;
            public bool IsMoving;
            public bool IsAlive;
            public bool IsExtrapolating;
        }

        public enum ClearReason
        {
            ManualReset,
            HardCorrection,
            Respawn,
            LateJoin
        }

        private readonly List<Snapshot> _snapshots = new List<Snapshot>(32);
        private readonly MultiplayerTuningSettings _tuning;
        private bool _lastAlive = true;

        public int Count => _snapshots.Count;
        public bool HasData => _snapshots.Count > 0;
        public uint LatestSequence => _snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1].State.Sequence : 0u;
        public bool LatestIsAlive => _lastAlive;

        public ProxyInterpolationBuffer(MultiplayerTuningSettings tuning)
        {
            _tuning = tuning;
        }

        /// <summary>Add a newly accepted host snapshot. Discards stale sequences.</summary>
        public void Add(in ProxyPresentationState state, float localTime)
        {
            if (_snapshots.Count > 0 && state.Sequence <= LatestSequence)
            {
                return;
            }

            // Maintain ascending local-time order. Insertion sort keeps the buffer small.
            var snapshot = new Snapshot { LocalTime = localTime, NetworkTick = state.NetworkTick, State = state };

            if (_snapshots.Count == 0 || localTime >= _snapshots[_snapshots.Count - 1].LocalTime)
            {
                _snapshots.Add(snapshot);
            }
            else
            {
                int insertAt = _snapshots.Count - 1;
                while (insertAt > 0 && _snapshots[insertAt - 1].LocalTime > localTime)
                {
                    insertAt--;
                }
                _snapshots.Insert(insertAt, snapshot);
            }

            _lastAlive = state.IsAlive;
            Prune(localTime);
        }

        /// <summary>
        /// Sample the buffer at render time, interpolating between the two snapshots that straddle
        /// <c>renderTime</c>. When render time is ahead of the latest snapshot, extrapolates up to
        /// <c>extrapolationCap</c> seconds and then holds the last state. Returns false when the
        /// buffer has no data yet.
        /// </summary>
        public bool Sample(float renderTime, out SampledState result)
        {
            result = default;
            if (_snapshots.Count == 0) return false;

            // Interpolation target is behind the latest state.
            float targetTime = renderTime - _tuning.interpolationDelay;

            // Find the two snapshots that bracket targetTime.
            int newerIndex = -1;
            for (int i = 0; i < _snapshots.Count; i++)
            {
                if (_snapshots[i].LocalTime >= targetTime)
                {
                    newerIndex = i;
                    break;
                }
            }

            if (newerIndex == -1)
            {
                // targetTime is ahead of the latest snapshot: extrapolate or hold.
                Snapshot latest = _snapshots[_snapshots.Count - 1];
                float elapsedSinceLatest = Mathf.Max(0f, targetTime - latest.LocalTime);

                if (elapsedSinceLatest <= _tuning.extrapolationCap && _snapshots.Count >= 1)
                {
                    result = ExtrapolateFrom(latest, elapsedSinceLatest);
                    result.IsExtrapolating = true;
                    return true;
                }

                result = ToSampledState(latest.State);
                result.IsExtrapolating = false;
                return true;
            }

            if (newerIndex == 0)
            {
                // targetTime is at or before the oldest snapshot: hold oldest.
                result = ToSampledState(_snapshots[0].State);
                result.IsExtrapolating = false;
                return true;
            }

            Snapshot older = _snapshots[newerIndex - 1];
            Snapshot newer = _snapshots[newerIndex];
            float span = newer.LocalTime - older.LocalTime;
            float t = span > Mathf.Epsilon ? Mathf.Clamp01((targetTime - older.LocalTime) / span) : 0f;

            result = Interpolate(older.State, newer.State, t);
            result.IsExtrapolating = false;
            return true;
        }

        /// <summary>Remove all snapshots and mark the reason for telemetry/debugging.</summary>
        public void Clear(ClearReason reason = ClearReason.ManualReset)
        {
            _snapshots.Clear();
        }

        /// <summary>Drop snapshots older than the retention window to bound memory.
        /// Always keeps at least the two most recent snapshots so interpolation between
        /// them remains possible even when the render time is close to the latest.</summary>
        private void Prune(float currentTime)
        {
            // Retain snapshots that could be needed for interpolation. The oldest useful
            // render target is (latestTime + extrapolationCap - interpolationDelay), and
            // interpolating at that target needs the snapshot just before it. A generous
            // margin avoids dropping the bracketing pair.
            float cutoff = currentTime - _tuning.interpolationDelay - _tuning.extrapolationCap - 0.5f;
            for (int i = _snapshots.Count - 1; i >= 0; i--)
            {
                // Never prune the two most recent snapshots.
                if (i >= _snapshots.Count - 2) break;
                if (_snapshots[i].LocalTime < cutoff)
                {
                    _snapshots.RemoveAt(i);
                }
            }
        }

        private static SampledState ToSampledState(in ProxyPresentationState s)
        {
            return new SampledState
            {
                Position = s.Position,
                Velocity = s.Velocity,
                BodyYaw = s.BodyYaw,
                AimYaw = s.AimYaw,
                AimPitch = s.AimPitch,
                MoveX = s.MoveX,
                MoveY = s.MoveY,
                Gait = s.Gait,
                IsGrounded = s.IsGrounded,
                IsInAir = s.IsInAir,
                IsCrouching = s.IsCrouching,
                IsSprinting = s.IsSprinting,
                IsAiming = s.IsAiming,
                IsMoving = s.IsMoving,
                IsAlive = s.IsAlive,
                IsExtrapolating = false
            };
        }

        private static SampledState Interpolate(in ProxyPresentationState a, in ProxyPresentationState b, float t)
        {
            return new SampledState
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
                BodyYaw = LerpAngle(a.BodyYaw, b.BodyYaw, t),
                AimYaw = LerpAngle(a.AimYaw, b.AimYaw, t),
                AimPitch = Mathf.Lerp(a.AimPitch, b.AimPitch, t),
                MoveX = Mathf.Lerp(a.MoveX, b.MoveX, t),
                MoveY = Mathf.Lerp(a.MoveY, b.MoveY, t),
                Gait = Mathf.Lerp(a.Gait, b.Gait, t),
                IsGrounded = t < 0.5f ? a.IsGrounded : b.IsGrounded,
                IsInAir = t < 0.5f ? a.IsInAir : b.IsInAir,
                IsCrouching = t < 0.5f ? a.IsCrouching : b.IsCrouching,
                IsSprinting = t < 0.5f ? a.IsSprinting : b.IsSprinting,
                IsAiming = t < 0.5f ? a.IsAiming : b.IsAiming,
                IsMoving = t < 0.5f ? a.IsMoving : b.IsMoving,
                IsAlive = t < 0.5f ? a.IsAlive : b.IsAlive,
                IsExtrapolating = false
            };
        }

        private static SampledState ExtrapolateFrom(in Snapshot from, float elapsed)
        {
            var s = from.State;
            return new SampledState
            {
                Position = s.Position + s.Velocity * elapsed,
                Velocity = s.Velocity,
                BodyYaw = s.BodyYaw,
                AimYaw = s.AimYaw,
                AimPitch = s.AimPitch,
                MoveX = s.MoveX,
                MoveY = s.MoveY,
                Gait = s.Gait,
                IsGrounded = s.IsGrounded,
                IsInAir = s.IsInAir,
                IsCrouching = s.IsCrouching,
                IsSprinting = s.IsSprinting,
                IsAiming = s.IsAiming,
                IsMoving = s.IsMoving,
                IsAlive = s.IsAlive,
                IsExtrapolating = true
            };
        }

        private static float LerpAngle(float a, float b, float t)
        {
            float delta = Mathf.Repeat(b - a, 360f);
            if (delta > 180f) delta -= 360f;
            return Mathf.Repeat(a + delta * t, 360f);
        }
    }
}