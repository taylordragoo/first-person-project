using FPSProject.Multiplayer.Core.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class DeterministicShotRandomTests
    {
        [Test]
        public void BuildSeed_IsDeterministic()
        {
            ulong s1 = DeterministicShotRandom.BuildSeed(1, 1, 1);
            ulong s2 = DeterministicShotRandom.BuildSeed(1, 1, 1);
            Assert.AreEqual(s1, s2);
        }

        [Test]
        public void BuildSeed_DiffersByShooter()
        {
            ulong s1 = DeterministicShotRandom.BuildSeed(1, 1, 1);
            ulong s2 = DeterministicShotRandom.BuildSeed(2, 1, 1);
            Assert.AreNotEqual(s1, s2);
        }

        [Test]
        public void BuildSeed_DiffersByWeapon()
        {
            ulong s1 = DeterministicShotRandom.BuildSeed(1, 1, 1);
            ulong s2 = DeterministicShotRandom.BuildSeed(1, 2, 1);
            Assert.AreNotEqual(s1, s2);
        }

        [Test]
        public void BuildSeed_DiffersBySequence()
        {
            ulong s1 = DeterministicShotRandom.BuildSeed(1, 1, 1);
            ulong s2 = DeterministicShotRandom.BuildSeed(1, 1, 2);
            Assert.AreNotEqual(s1, s2);
        }

        [Test]
        public void BuildSeed_NeverZero()
        {
            ulong s = DeterministicShotRandom.BuildSeed(0, 0, 0);
            Assert.AreNotEqual(0ul, s);
        }

        [Test]
        public void SpreadCone_IsDeterministic()
        {
            Vector3 dir = Vector3.forward;
            Vector3 a = DeterministicShotRandom.SpreadCone(5, 1, 10, 0, dir, 4f);
            Vector3 b = DeterministicShotRandom.SpreadCone(5, 1, 10, 0, dir, 4f);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void SpreadCone_DiffersByPellet()
        {
            Vector3 dir = Vector3.forward;
            Vector3 a = DeterministicShotRandom.SpreadCone(5, 3, 1, 0, dir, 4f);
            Vector3 b = DeterministicShotRandom.SpreadCone(5, 3, 1, 1, dir, 4f);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void SpreadCone_DiffersByShooter()
        {
            Vector3 dir = Vector3.forward;
            Vector3 a = DeterministicShotRandom.SpreadCone(1, 3, 1, 0, dir, 4f);
            Vector3 b = DeterministicShotRandom.SpreadCone(2, 3, 1, 0, dir, 4f);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void SpreadCone_ZeroAngleReturnsNormalizedBase()
        {
            Vector3 dir = new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 result = DeterministicShotRandom.SpreadCone(1, 1, 1, 0, dir, 0f);
            Assert.AreEqual(dir.normalized, result);
        }

        [Test]
        public void SpreadCone_StaysWithinCone()
        {
            Vector3 dir = Vector3.forward;
            float halfAngle = 4f;
            for (int i = 0; i < 64; i++)
            {
                Vector3 spread = DeterministicShotRandom.SpreadCone(1, 3, (uint)i, i % 8, dir, halfAngle);
                float angle = Vector3.Angle(dir, spread);
                Assert.LessOrEqual(angle, halfAngle + 0.1f,
                    $"Pellet {i} spread angle {angle} exceeds half-angle {halfAngle}.");
                Assert.AreEqual(1f, spread.magnitude, 0.001f, "Spread direction must be normalized.");
            }
        }

        [Test]
        public void SpreadCone_DoesNotMutateUnityRandom()
        {
            Random.InitState(12345);
            float r1 = Random.value;
            float r2 = Random.value;

            Random.InitState(12345);
            _ = DeterministicShotRandom.SpreadCone(1, 1, 1, 0, Vector3.forward, 4f);
            float r3 = Random.value;
            float r4 = Random.value;

            Assert.AreEqual(r1, r3, "Unity Random state was mutated by SpreadCone.");
            Assert.AreEqual(r2, r4, "Unity Random state was mutated by SpreadCone.");
        }
    }
}