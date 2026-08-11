using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class NetworkHealthTests
    {
        [Test]
        public void MaxHealth_DefaultsTo100()
        {
            var go = new GameObject("TestHealth");
            var health = go.AddComponent<NetworkHealth>();
            Assert.AreEqual(100f, health.MaxHealth);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void IsDead_ReturnsTrue_WhenHealthIsZero()
        {
            var go = new GameObject("TestHealth");
            var health = go.AddComponent<NetworkHealth>();
            health.CurrentHealth.Value = 0f;
            Assert.IsTrue(health.IsDead);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void IsDead_ReturnsFalse_WhenHealthIsPositive()
        {
            var go = new GameObject("TestHealth");
            var health = go.AddComponent<NetworkHealth>();
            health.CurrentHealth.Value = 50f;
            Assert.IsFalse(health.IsDead);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void IsDead_ReturnsFalse_WhenHealthIsFull()
        {
            var go = new GameObject("TestHealth");
            var health = go.AddComponent<NetworkHealth>();
            health.CurrentHealth.Value = 100f;
            Assert.IsFalse(health.IsDead);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ApplyDamage_WithoutNetworkSession_UsesStandaloneAuthority()
        {
            var target = new GameObject("StandaloneTarget");
            var instigator = new GameObject("StandalonePlayer");
            try
            {
                var health = target.AddComponent<NetworkHealth>();
                health.InitializeStandalone();

                health.ApplyDamage(new DamageInfo(25f, Vector3.zero, Vector3.up,
                    Vector3.forward, instigator, null));

                Assert.AreEqual(75f, health.CurrentHealth.Value);
                Assert.IsFalse(health.IsDead);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(instigator);
            }
        }

        [Test]
        public void PassiveBot_StandaloneInitializationSetsEnemyIdentity()
        {
            var go = new GameObject("StandaloneBot");
            try
            {
                var bot = go.AddComponent<PassiveTargetBot>();

                bot.InitializeStandalone(MatchTeam.Bravo, 7);

                Assert.IsTrue(bot.IsStandalone);
                Assert.AreEqual(MatchTeam.Bravo, bot.TeamValue);
                Assert.AreEqual(7, bot.SpawnSlot.Value);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
