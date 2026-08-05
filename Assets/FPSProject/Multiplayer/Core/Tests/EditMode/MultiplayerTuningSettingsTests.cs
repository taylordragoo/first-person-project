using FPSProject.Multiplayer.Core.Movement;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class MultiplayerTuningSettingsTests
    {
        [Test]
        public void IsInsideWorldBounds_AcceptsCenter()
        {
            var t = ScriptableObject.CreateInstance<MultiplayerTuningSettings>();
            t.worldBoundsCenter = Vector3.zero;
            t.worldBoundsSize = new Vector3(100f, 100f, 100f);

            Assert.IsTrue(t.IsInsideWorldBounds(Vector3.zero));
            Assert.IsTrue(t.IsInsideWorldBounds(new Vector3(49f, 49f, 49f)));
            Assert.IsTrue(t.IsInsideWorldBounds(new Vector3(-49f, -49f, -49f)));
        }

        [Test]
        public void IsInsideWorldBounds_RejectsOutside()
        {
            var t = ScriptableObject.CreateInstance<MultiplayerTuningSettings>();
            t.worldBoundsCenter = Vector3.zero;
            t.worldBoundsSize = new Vector3(100f, 100f, 100f);

            Assert.IsFalse(t.IsInsideWorldBounds(new Vector3(51f, 0f, 0f)));
            Assert.IsFalse(t.IsInsideWorldBounds(new Vector3(0f, -51f, 0f)));
            Assert.IsFalse(t.IsInsideWorldBounds(new Vector3(0f, 0f, 1000f)));
        }

        [Test]
        public void IsInsideWorldBounds_RespectsOffsetCenter()
        {
            var t = ScriptableObject.CreateInstance<MultiplayerTuningSettings>();
            t.worldBoundsCenter = new Vector3(100f, 0f, 100f);
            t.worldBoundsSize = new Vector3(100f, 100f, 100f);

            Assert.IsTrue(t.IsInsideWorldBounds(new Vector3(100f, 0f, 100f)));
            Assert.IsTrue(t.IsInsideWorldBounds(new Vector3(149f, 0f, 51f)));
            Assert.IsFalse(t.IsInsideWorldBounds(Vector3.zero));
        }

        [Test]
        public void WorldBoundsExtents_IsHalfOfSize()
        {
            var t = ScriptableObject.CreateInstance<MultiplayerTuningSettings>();
            t.worldBoundsSize = new Vector3(200f, 100f, 50f);

            Assert.AreEqual(new Vector3(100f, 50f, 25f), t.WorldBoundsExtents);
        }
    }
}