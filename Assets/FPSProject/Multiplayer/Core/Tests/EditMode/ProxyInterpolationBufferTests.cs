using FPSProject.Multiplayer.Core.Movement;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class ProxyInterpolationBufferTests
    {
        private static MultiplayerTuningSettings CreateTuning()
        {
            var t = ScriptableObject.CreateInstance<MultiplayerTuningSettings>();
            t.interpolationDelay = 0.1f;
            t.extrapolationCap = 0.1f;
            return t;
        }

        private static ProxyPresentationState CreateState(uint seq, Vector3 pos, float yaw = 0f)
        {
            return new ProxyPresentationState
            {
                Sequence = seq,
                NetworkTick = 0,
                Position = pos,
                Velocity = Vector3.zero,
                BodyYaw = yaw,
                AimYaw = yaw,
                AimPitch = 0f,
                MoveX = 0f,
                MoveY = 0f,
                Gait = 0f,
                IsGrounded = true,
                IsInAir = false,
                IsCrouching = false,
                IsSprinting = false,
                IsAiming = false,
                IsMoving = false,
                IsAlive = true
            };
        }

        [Test]
        public void ReturnsFalseWhenEmpty()
        {
            var buf = new ProxyInterpolationBuffer(CreateTuning());
            Assert.IsFalse(buf.Sample(1f, out _));
            Assert.IsFalse(buf.HasData);
        }

        [Test]
        public void HoldsOldestWhenRenderTimeBeforeBuffer()
        {
            var buf = new ProxyInterpolationBuffer(CreateTuning());
            buf.Add(CreateState(1, new Vector3(1f, 0f, 0f)), 1f);

            // Render time 0.5, interpolation delay 0.1 -> target = 0.4, before oldest (1.0).
            Assert.IsTrue(buf.Sample(0.5f, out var result));
            Assert.AreEqual(new Vector3(1f, 0f, 0f), result.Position);
            Assert.IsFalse(result.IsExtrapolating);
        }

        [Test]
        public void InterpolatesBetweenTwoSnapshots()
        {
            var buf = new ProxyInterpolationBuffer(CreateTuning());
            buf.Add(CreateState(1, new Vector3(0f, 0f, 0f)), 1f);
            buf.Add(CreateState(2, new Vector3(10f, 0f, 0f)), 2f);

            // Render time 2.0, delay 0.1 -> target = 1.9. Snapshots at t=1 and t=2.
            // t = (1.9 - 1.0) / (2.0 - 1.0) = 0.9 -> pos = 9.0.
            Assert.IsTrue(buf.Sample(2.0f, out var result));
            Assert.AreEqual(9f, result.Position.x, 0.01f);
            Assert.IsFalse(result.IsExtrapolating);
        }

        [Test]
        public void ExtrapolatesWithinCapThenHolds()
        {
            var buf = new ProxyInterpolationBuffer(CreateTuning());
            // Give the state a velocity so extrapolation produces movement.
            var s = CreateState(1, new Vector3(0f, 0f, 0f));
            s.Velocity = new Vector3(1f, 0f, 0f);
            buf.Add(s, 1f);

            // Render time 1.15, delay 0.1 -> target = 1.05. Latest at t=1.0.
            // elapsedSinceLatest = 0.05, within extrapolationCap (0.1).
            // Extrapolated pos = 0 + 1 * 0.05 = 0.05.
            Assert.IsTrue(buf.Sample(1.15f, out var result));
            Assert.IsTrue(result.IsExtrapolating);
            Assert.AreEqual(0.05f, result.Position.x, 0.01f);

            // Render time 1.3, delay 0.1 -> target = 1.2. Latest at t=1.0.
            // elapsedSinceLatest = 0.2, exceeds extrapolationCap (0.1) -> hold.
            Assert.IsTrue(buf.Sample(1.3f, out var holdResult));
            Assert.IsFalse(holdResult.IsExtrapolating);
            Assert.AreEqual(0f, holdResult.Position.x, 0.01f);
        }

        [Test]
        public void RejectsStaleSequence()
        {
            var buf = new ProxyInterpolationBuffer(CreateTuning());
            buf.Add(CreateState(5, new Vector3(5f, 0f, 0f)), 1f);
            buf.Add(CreateState(3, new Vector3(3f, 0f, 0f)), 2f); // stale

            Assert.AreEqual(5u, buf.LatestSequence);
            Assert.AreEqual(1, buf.Count);
        }

        [Test]
        public void ClearsOnReset()
        {
            var buf = new ProxyInterpolationBuffer(CreateTuning());
            buf.Add(CreateState(1, Vector3.zero), 1f);
            Assert.IsTrue(buf.HasData);

            buf.Clear(ProxyInterpolationBuffer.ClearReason.HardCorrection);
            Assert.IsFalse(buf.HasData);
            Assert.AreEqual(0, buf.Count);
        }

        [Test]
        public void InterpolatesAngleAcrossWraparound()
        {
            var buf = new ProxyInterpolationBuffer(CreateTuning());
            buf.Add(CreateState(1, Vector3.zero, 350f), 1f);
            buf.Add(CreateState(2, Vector3.zero, 10f), 2f);

            // Render time 2.0, delay 0.1 -> target 1.9, t = 0.9.
            // Angle should wrap from 350 toward 10, giving ~8 at t=0.9.
            Assert.IsTrue(buf.Sample(2.0f, out var result));
            Assert.That(result.BodyYaw, Is.EqualTo(8f).Within(1f));
        }
    }
}