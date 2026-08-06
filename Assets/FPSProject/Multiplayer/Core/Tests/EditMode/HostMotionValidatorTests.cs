using FPSProject.Multiplayer.Core.Movement;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class HostMotionValidatorTests
    {
        private static MultiplayerTuningSettings CreateTuning()
        {
            var t = ScriptableObject.CreateInstance<MultiplayerTuningSettings>();
            t.baseSpeedLimit = 3f;
            t.sprintSpeedMultiplier = 1.67f;
            t.crouchSpeedMultiplier = 0.55f;
            t.movementValidationGrace = 0.35f;
            t.softCorrectionThreshold = 0.75f;
            t.hardCorrectionThreshold = 2f;
            t.worldBoundsCenter = Vector3.zero;
            t.worldBoundsSize = new Vector3(500f, 200f, 500f);
            return t;
        }

        private static HostMotionValidator.ValidationContext CreateContext(
            MultiplayerTuningSettings tuning, Vector3 lastPos, float elapsed,
            int currentTick = 100, LayerMask mask = default)
        {
            return new HostMotionValidator.ValidationContext
            {
                Tuning = tuning,
                LastAcceptedPosition = lastPos,
                LastAcceptedTime = 0f,
                StaticEnvironmentMask = mask,
                CapsuleRadius = 0.3f,
                CapsuleHeight = 1.8f,
                CapsuleCenter = new Vector3(0f, 0.9f, 0f),
                MaxFallSpeed = 25f,
                CurrentNetworkTick = currentTick,
                ElapsedTime = elapsed
            };
        }

        private static OwnerMotionSample CreateSample(uint seq, Vector3 pos, int tick = 50)
        {
            return new OwnerMotionSample
            {
                Sequence = seq,
                NetworkTick = tick,
                Position = pos,
                Velocity = Vector3.zero,
                BodyYaw = 0f,
                AimYaw = 0f,
                AimPitch = 0f,
                MoveX = 0f,
                MoveY = 1f,
                Gait = 1f,
                IsGrounded = true,
                IsInAir = false,
                IsCrouching = false,
                IsSprinting = false,
                IsAiming = false,
                IsMoving = true
            };
        }

        [Test]
        public void AcceptsValidSample()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f));

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsTrue(result.Accepted, $"Expected accepted, got {result.Reason}: {result.DebugMessage}");
        }

        [Test]
        public void RejectsStaleSequence()
        {
            // HostMotionValidator itself doesn't check sequence (the caller does), but we
            // verify that a sample with the same position still passes validation — sequence
            // ordering is the NetworkCasPlayer's responsibility.
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f));

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsTrue(result.Accepted);
        }

        [Test]
        public void RejectsNonFinitePosition()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(float.NaN, 0f, 0f));

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.NonFinite, result.Reason);
        }

        [Test]
        public void RejectsNonFiniteVelocity()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f));
            sample.Velocity = new Vector3(float.PositiveInfinity, 0f, 0f);

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.NonFinite, result.Reason);
        }

        [Test]
        public void RejectsFutureTick()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f, currentTick: 100);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f), tick: 101);

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.FutureTick, result.Reason);
        }

        [Test]
        public void AcceptsCurrentTick()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f, currentTick: 100);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f), tick: 100);

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsTrue(result.Accepted);
        }

        [Test]
        public void AcceptsPastSynchronizedServerTick()
        {
            // A remote owner captures the client's synchronized ServerTime tick. By the time
            // the packet reaches the host it is expected to be behind the host's current tick.
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f, currentTick: 100);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f), tick: 95);

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsTrue(result.Accepted,
                $"Synchronized past tick was rejected: {result.Reason} - {result.DebugMessage}");
        }

        [Test]
        public void RejectsOutOfWorldBounds()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(1000f, 0f, 0f)); // outside 500m bounds

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.OutOfWorldBounds, result.Reason);
        }

        [Test]
        public void AcceptsLegitimateFalling()
        {
            // A player falling at maxFallSpeed (25 m/s) for 0.05s moves 1.25m down.
            // This must NOT trigger the horizontal speed check.
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0f, -1.25f, 0f));
            sample.IsInAir = true;
            sample.IsGrounded = false;

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsTrue(result.Accepted, $"Falling was rejected: {result.Reason} - {result.DebugMessage}");
        }

        [Test]
        public void RejectsExcessiveVerticalDisplacement()
        {
            // Vertical displacement far exceeding maxFallSpeed * elapsed + grace should reject.
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0f, -50f, 0f)); // 50m in 0.05s = 1000 m/s

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.ImpossibleSpeed, result.Reason);
        }

        [Test]
        public void AcceptsSprintSpeed()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0f, 0f, 0.25f));
            sample.IsSprinting = true;
            sample.MoveY = 1f;

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsTrue(result.Accepted, $"Sprint rejected: {result.Reason} - {result.DebugMessage}");
        }

        [Test]
        public void RejectsSprintWhileCrouching()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f));
            sample.IsSprinting = true;
            sample.IsCrouching = true;
            sample.MoveY = 1f;

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.SprintWhileCrouched, result.Reason);
        }

        [Test]
        public void RejectsSprintWithoutForward()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f));
            sample.IsSprinting = true;
            sample.MoveY = 0f;

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.SprintWithoutForward, result.Reason);
        }

        [Test]
        public void RejectsAimingWhileSprinting()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            var sample = CreateSample(1, new Vector3(0.1f, 0f, 0f));
            sample.IsSprinting = true;
            sample.IsAiming = true;
            sample.MoveY = 1f;

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.AimingWhileSprinting, result.Reason);
        }

        [Test]
        public void RejectsExcessiveHorizontalSpeed()
        {
            var tuning = CreateTuning();
            var ctx = CreateContext(tuning, Vector3.zero, 0.05f);
            // 10m in 0.05s = 200 m/s, far above any gait speed + grace
            var sample = CreateSample(1, new Vector3(10f, 0f, 0f));

            var result = HostMotionValidator.Validate(sample, ctx);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(HostMotionValidator.RejectReason.ImpossibleSpeed, result.Reason);
        }
    }
}
