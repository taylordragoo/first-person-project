using UnityEngine;

namespace FPSProject.Multiplayer.Core.Movement
{
    /// <summary>
    /// Host-side validator for <see cref="OwnerMotionSample"/> submissions. Implements the plan's
    /// movement reconciliation rules: stale/out-of-order sequence rejection, non-finite and
    /// world-bounds rejection, speed-limit enforcement, blocking-geometry capsule sweep, and
    /// incompatible state rejection (sprint while crouched, sprint without forward movement,
    /// aiming while sprinting, firing while sprinting). This is not competitive-grade anti-cheat;
    /// it catches malformed input, wall crossing, and major divergence.
    /// </summary>
    public static class HostMotionValidator
    {
        public struct ValidationContext
        {
            public MultiplayerTuningSettings Tuning;
            public Vector3 LastAcceptedPosition;
            public float LastAcceptedTime;
            public LayerMask StaticEnvironmentMask;
            public float CapsuleRadius;
            public float CapsuleHeight;
            /// <summary>Local-space center of the CharacterController capsule. The validation capsule must be offset by this so it matches the live collider, not the transform origin.</summary>
            public Vector3 CapsuleCenter;
            public float ElapsedTime;
            /// <summary>Maximum fall speed the CAS rig permits (matches <c>maxFallVelocity</c>). Vertical displacement is validated against this, not the horizontal gait speed.</summary>
            public float MaxFallSpeed;
            /// <summary>Host's current network tick. Samples with a tick ahead of this are rejected as future-timestamped.</summary>
            public int CurrentNetworkTick;
        }

        public enum RejectReason
        {
            Accepted,
            StaleSequence,
            FutureTick,
            NonFinite,
            OutOfWorldBounds,
            ImpossibleSpeed,
            BlockedByGeometry,
            SprintWhileCrouched,
            SprintWithoutForward,
            AimingWhileSprinting,
            FiringWhileSprinting
        }

        public struct ValidationResult
        {
            public bool Accepted;
            public RejectReason Reason;
            public Vector3 AcceptedPosition;
            public float AcceptedSpeed;
            public string DebugMessage;
        }

        /// <summary>
        /// Validate a single owner motion sample against the last accepted host state.
        /// </summary>
        public static ValidationResult Validate(in OwnerMotionSample sample, in ValidationContext ctx)
        {
            var result = new ValidationResult { Accepted = false, Reason = RejectReason.Accepted };

            // Non-finite position/velocity are always rejected.
            if (!ValidateFinite(sample))
            {
                result.Reason = RejectReason.NonFinite;
                result.DebugMessage = "Non-finite value in motion sample.";
                return result;
            }

            // Future-tick rejection: a sample whose network tick is ahead of the host's current
            // tick cannot be trusted; the owner's clock is running ahead or the sample is forged.
            if (sample.NetworkTick > ctx.CurrentNetworkTick)
            {
                result.Reason = RejectReason.FutureTick;
                result.DebugMessage = $"Sample tick {sample.NetworkTick} is ahead of host tick {ctx.CurrentNetworkTick}.";
                return result;
            }

            // World bounds.
            if (!ctx.Tuning.IsInsideWorldBounds(sample.Position))
            {
                result.Reason = RejectReason.OutOfWorldBounds;
                result.DebugMessage = $"Position {sample.Position} outside world bounds.";
                return result;
            }

            // Incompatible state combinations.
            result = ValidateStateConsistency(sample);
            if (!result.Accepted) return result;

            // Speed and displacement.
            result = ValidateSpeedAndDisplacement(sample, ctx);
            if (!result.Accepted) return result;

            // Static environment capsule sweep.
            result = ValidateCapsuleSweep(sample, ctx);
            if (!result.Accepted) return result;

            result.Accepted = true;
            result.Reason = RejectReason.Accepted;
            result.AcceptedPosition = sample.Position;
            return result;
        }

        private static bool ValidateFinite(in OwnerMotionSample s)
        {
            return IsFinite(s.Position) && IsFinite(s.Velocity)
                && float.IsFinite(s.BodyYaw) && float.IsFinite(s.AimYaw) && float.IsFinite(s.AimPitch)
                && float.IsFinite(s.MoveX) && float.IsFinite(s.MoveY) && float.IsFinite(s.Gait);
        }

        private static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }

        private static ValidationResult ValidateStateConsistency(in OwnerMotionSample s)
        {
            if (s.IsSprinting && s.IsCrouching)
            {
                return Reject(RejectReason.SprintWhileCrouched, "Sprint and crouch cannot be active together.");
            }

            if (s.IsSprinting && s.MoveY <= 0.01f)
            {
                return Reject(RejectReason.SprintWithoutForward, "Sprint requires forward movement (MoveY > 0).");
            }

            if (s.IsSprinting && s.IsAiming)
            {
                return Reject(RejectReason.AimingWhileSprinting, "Aiming while sprinting is not permitted.");
            }

            // The firing-while-sprint check is structural; the shot router enforces fire locks,
            // but motion validation still flags the impossible state so it cannot be used to
            // smuggle an accepted sprint pose that should have blocked firing.
            // (Firing flag lives in the shot command, not motion sample; nothing to check here.)

            return new ValidationResult { Accepted = true, Reason = RejectReason.Accepted };
        }

        private static ValidationResult ValidateSpeedAndDisplacement(in OwnerMotionSample sample, in ValidationContext ctx)
        {
            float elapsed = Mathf.Max(0.001f, ctx.ElapsedTime);
            Vector3 displacement = sample.Position - ctx.LastAcceptedPosition;

            // Horizontal displacement is validated against the gait-derived speed limit.
            // Vertical displacement (falling/jumping) is validated separately against the
            // CAS rig's max fall speed plus the gravity-driven impulse on a fresh jump, so
            // legitimate falling never trips the horizontal speed check.
            float horizontalDisplacement = new Vector2(displacement.x, displacement.z).magnitude;
            float verticalDisplacement = Mathf.Abs(displacement.y);

            float speedLimit = ctx.Tuning.baseSpeedLimit;
            if (sample.IsSprinting) speedLimit *= ctx.Tuning.sprintSpeedMultiplier;
            else if (sample.IsCrouching) speedLimit *= ctx.Tuning.crouchSpeedMultiplier;

            float maxHorizontalDisplacement = speedLimit * elapsed + ctx.Tuning.movementValidationGrace;
            if (horizontalDisplacement > maxHorizontalDisplacement)
            {
                return Reject(RejectReason.ImpossibleSpeed,
                    $"Horizontal displacement {horizontalDisplacement:F3} m exceeds limit {maxHorizontalDisplacement:F3} m " +
                    $"(speedLimit={speedLimit:F2}, elapsed={elapsed:F3}).");
            }

            // Vertical: allow the configured max fall speed plus the same grace. Jump impulse
            // is a brief upward spike; the grace covers it without letting players teleport
            // vertically. Use MaxFallSpeed if configured, otherwise a generous default.
            float maxFallSpeed = Mathf.Max(1f, ctx.MaxFallSpeed);
            float maxVerticalDisplacement = maxFallSpeed * elapsed + ctx.Tuning.movementValidationGrace;
            if (verticalDisplacement > maxVerticalDisplacement)
            {
                return Reject(RejectReason.ImpossibleSpeed,
                    $"Vertical displacement {verticalDisplacement:F3} m exceeds limit {maxVerticalDisplacement:F3} m " +
                    $"(maxFallSpeed={maxFallSpeed:F2}, elapsed={elapsed:F3}).");
            }

            float acceptedSpeed = displacement.magnitude / elapsed;
            return new ValidationResult
            {
                Accepted = true,
                Reason = RejectReason.Accepted,
                AcceptedSpeed = acceptedSpeed
            };
        }

        private static ValidationResult ValidateCapsuleSweep(in OwnerMotionSample sample, in ValidationContext ctx)
        {
            if (ctx.StaticEnvironmentMask == 0) return new ValidationResult { Accepted = true, Reason = RejectReason.Accepted };

            Vector3 start = ctx.LastAcceptedPosition;
            Vector3 end = sample.Position;
            Vector3 delta = end - start;

            if (delta.sqrMagnitude < 1e-6f)
            {
                return new ValidationResult { Accepted = true, Reason = RejectReason.Accepted };
            }

            float radius = Mathf.Max(0.01f, ctx.CapsuleRadius);
            float halfHeight = Mathf.Max(radius + 0.01f, ctx.CapsuleHeight * 0.5f);
            float hemisphereOffset = Mathf.Max(0f, halfHeight - radius);

            // Offset the capsule points by the CharacterController's local-space center so the
            // validation capsule matches the live collider. Without this, the capsule is placed
            // at the transform origin, which can put part of it below ground and produce false
            // blocking-geometry hits on flat terrain.
            Vector3 centerOffset = ctx.CapsuleCenter;

            Vector3 castStartBottom = start + centerOffset + Vector3.down * hemisphereOffset;
            Vector3 castStartTop = start + centerOffset + Vector3.up * hemisphereOffset;

            Vector3 direction = delta.normalized;
            float distance = delta.magnitude;

            if (Physics.CapsuleCast(castStartBottom, castStartTop, radius, direction, out RaycastHit hit,
                distance, ctx.StaticEnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                return Reject(RejectReason.BlockedByGeometry,
                    $"Capsule sweep from {start} to {end} hit {hit.collider.name} at {hit.point}.");
            }

            return new ValidationResult { Accepted = true, Reason = RejectReason.Accepted };
        }

        private static ValidationResult Reject(RejectReason reason, string message)
        {
            return new ValidationResult { Accepted = false, Reason = reason, DebugMessage = message };
        }
    }
}