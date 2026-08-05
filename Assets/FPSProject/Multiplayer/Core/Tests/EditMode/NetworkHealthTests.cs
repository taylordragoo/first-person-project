using FPSProject.Multiplayer.Core.Health;
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
    }
}
