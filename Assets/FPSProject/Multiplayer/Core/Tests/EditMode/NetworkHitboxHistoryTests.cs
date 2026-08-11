using FPSProject.Multiplayer.Core.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class NetworkHitboxHistoryTests
    {
        private static HitboxPoseSample CreateSample(float t, Vector3 pos, float yaw = 0f,
            float height = 1.8f, float radius = 0.3f, bool crouch = false,
            float aimYaw = 0f, float aimPitch = 0f, bool isAiming = false)
        {
            return new HitboxPoseSample
            {
                Time = t,
                Position = pos,
                BodyYaw = yaw,
                AimYaw = aimYaw,
                AimPitch = aimPitch,
                IsAiming = isAiming,
                CapsuleCenter = pos + new Vector3(0f, height * 0.5f, 0f),
                CapsuleHeight = height,
                CapsuleRadius = radius,
                IsCrouching = crouch
            };
        }

        [Test]
        public void Empty_ReturnsFalseForAnyTime()
        {
            var h = new NetworkHitboxHistory(0.25f);
            Assert.IsFalse(h.TryGetCapsule(0f, out _));
            Assert.AreEqual(0, h.Count);
        }

        [Test]
        public void Record_AndRetrieveOldest()
        {
            var h = new NetworkHitboxHistory(0.25f);
            var s = CreateSample(0f, Vector3.zero);
            h.Record(0f, in s);
            Assert.IsTrue(h.TryGetCapsule(0f, out var cap));
            Assert.AreEqual(0.3f, cap.Radius);
            Assert.AreEqual(1.8f, cap.Height);
        }

        [Test]
        public void Record_PrunesOlderThanWindow()
        {
            var h = new NetworkHitboxHistory(0.25f);
            var s0 = CreateSample(0f, Vector3.zero);
            var s1 = CreateSample(0.1f, Vector3.forward);
            var s2 = CreateSample(0.3f, Vector3.forward * 3);
            h.Record(0f, in s0);
            h.Record(0.1f, in s1);
            h.Record(0.3f, in s2);
            // 0.3 - 0.25 = 0.05 cutoff; the 0f sample should be pruned.
            Assert.IsFalse(h.TryGetCapsule(0f, out _));
            Assert.IsTrue(h.TryGetCapsule(0.1f, out _));
            Assert.IsTrue(h.TryGetCapsule(0.3f, out _));
        }

        [Test]
        public void TryGetCapsule_RejectsTimeOutsideWindow()
        {
            var h = new NetworkHitboxHistory(0.25f);
            var s1 = CreateSample(1f, Vector3.zero);
            var s2 = CreateSample(1.2f, Vector3.forward);
            h.Record(1f, in s1);
            h.Record(1.2f, in s2);
            Assert.IsFalse(h.TryGetCapsule(0.5f, out _), "Too old.");
            Assert.IsFalse(h.TryGetCapsule(1.3f, out _), "Newer than newest.");
            Assert.IsTrue(h.TryGetCapsule(1.1f, out _));
        }

        [Test]
        public void TryGetCapsule_InterpolatesBetweenSamples()
        {
            var h = new NetworkHitboxHistory(2f);
            var s1 = CreateSample(1f, Vector3.zero);
            var s2 = CreateSample(2f, new Vector3(10f, 0f, 0f));
            h.Record(1f, in s1);
            h.Record(2f, in s2);
            Assert.IsTrue(h.TryGetCapsule(1.5f, out var cap));
            // Midpoint between 0 and (10,0,0) is (5,0,0). Center is +0.9 in Y.
            Assert.AreEqual(5f, cap.Center.x, 0.01f);
            Assert.AreEqual(0.9f, cap.Center.y, 0.01f);
            Assert.AreEqual(0f, cap.Center.z, 0.01f);
        }

        [Test]
        public void TryGetPose_InterpolatesAcceptedAim()
        {
            var h = new NetworkHitboxHistory(2f);
            var s1 = CreateSample(1f, Vector3.zero, aimYaw: 10f, aimPitch: -20f);
            var s2 = CreateSample(2f, Vector3.zero, aimYaw: 50f, aimPitch: 20f);
            h.Record(1f, in s1);
            h.Record(2f, in s2);

            Assert.IsTrue(h.TryGetPose(1.5f, out HitboxPoseSample pose));
            Assert.AreEqual(30f, pose.AimYaw, 0.01f);
            Assert.AreEqual(0f, pose.AimPitch, 0.01f);
        }

        [Test]
        public void TryGetPose_UsesHistoricalAcceptedAimingState()
        {
            var h = new NetworkHitboxHistory(2f);
            var hip = CreateSample(1f, Vector3.zero, isAiming: false);
            var ads = CreateSample(2f, Vector3.zero, isAiming: true);
            h.Record(1f, in hip);
            h.Record(2f, in ads);

            Assert.IsTrue(h.TryGetPose(1.5f, out HitboxPoseSample beforeAdsSample));
            Assert.IsFalse(beforeAdsSample.IsAiming,
                "ADS must not be granted before the host accepted the ADS sample.");
            Assert.IsTrue(h.TryGetPose(2f, out HitboxPoseSample atAdsSample));
            Assert.IsTrue(atAdsSample.IsAiming);
        }

        [Test]
        public void Clear_RemovesAllSamples()
        {
            var h = new NetworkHitboxHistory(0.25f);
            var s = CreateSample(0f, Vector3.zero);
            h.Record(0f, in s);
            h.Clear();
            Assert.AreEqual(0, h.Count);
            Assert.IsFalse(h.TryGetCapsule(0f, out _));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Analytical ray-vs-capsule tests.
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void RaycastCapsule_HitsCenteredCapsuleFromFront()
        {
            // Capsule centered at origin, height 1.8, radius 0.3. Ray from -Z forward.
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0.9f, 0f),
                Top = new Vector3(0f, 0.9f, 0f), // single sphere for simplicity (height == 2*radius)
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 0.6f
            };
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(0f, 0.9f, -5f), Vector3.forward, in cap, 100f, out float dist);
            Assert.IsTrue(hit);
            Assert.AreEqual(4.7f, dist, 0.05f); // 5 - 0.3 = 4.7
        }

        [Test]
        public void RaycastCapsule_MissesWhenOffAxis()
        {
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0f, 0f),
                Top = new Vector3(0f, 1.8f, 0f),
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 1.8f
            };
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(5f, 0.9f, -5f), Vector3.forward, in cap, 100f, out _);
            Assert.IsFalse(hit);
        }

        [Test]
        public void RaycastCapsule_HitsCylinderSection()
        {
            // Capsule with a real cylinder section.
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0.3f, 0f),
                Top = new Vector3(0f, 1.5f, 0f),
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 1.8f
            };
            // Ray aimed at the middle of the cylinder section.
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(0f, 0.9f, -5f), Vector3.forward, in cap, 100f, out float dist);
            Assert.IsTrue(hit);
            Assert.AreEqual(4.7f, dist, 0.05f);
        }

        [Test]
        public void RaycastCapsule_HitsTopSphere()
        {
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0.3f, 0f),
                Top = new Vector3(0f, 1.5f, 0f),
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 1.8f
            };
            // Ray aimed above the cylinder, at the top sphere center (1.5).
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(0f, 1.5f, -5f), Vector3.forward, in cap, 100f, out float dist);
            Assert.IsTrue(hit);
            Assert.AreEqual(4.7f, dist, 0.05f);
        }

        [Test]
        public void RaycastCapsule_HitsBottomSphere()
        {
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0.3f, 0f),
                Top = new Vector3(0f, 1.5f, 0f),
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 1.8f
            };
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(0f, 0.3f, -5f), Vector3.forward, in cap, 100f, out float dist);
            Assert.IsTrue(hit);
            Assert.AreEqual(4.7f, dist, 0.05f);
        }

        [Test]
        public void RaycastCapsule_RespectsMaxDistance()
        {
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0f, 0f),
                Top = new Vector3(0f, 1.8f, 0f),
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 1.8f
            };
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(0f, 0.9f, -5f), Vector3.forward, in cap, 4f, out _);
            Assert.IsFalse(hit, "Should miss when maxDistance < distance to capsule.");
        }

        [Test]
        public void RaycastCapsule_OriginInsideReturnsZero()
        {
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0f, 0f),
                Top = new Vector3(0f, 1.8f, 0f),
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 1.8f
            };
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(0f, 0.9f, 0f), Vector3.forward, in cap, 100f, out float dist);
            Assert.IsTrue(hit);
            Assert.AreEqual(0f, dist, 0.001f);
        }

        [Test]
        public void RaycastCapsule_DegenerateAxisFallsBackToSphere()
        {
            var cap = new HistoricalCapsule
            {
                Bottom = new Vector3(0f, 0.9f, 0f),
                Top = new Vector3(0f, 0.9f, 0f),
                Radius = 0.3f,
                Center = new Vector3(0f, 0.9f, 0f),
                Height = 0.6f
            };
            bool hit = NetworkHitboxHistory.RaycastCapsule(
                new Vector3(0f, 0.9f, -5f), Vector3.forward, in cap, 100f, out float dist);
            Assert.IsTrue(hit);
            Assert.AreEqual(4.7f, dist, 0.05f);
        }
    }
}
