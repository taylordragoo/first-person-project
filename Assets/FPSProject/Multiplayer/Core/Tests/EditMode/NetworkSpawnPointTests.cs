using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class NetworkSpawnPointTests
    {
        [Test]
        public void SelectSpawnPoint_ReturnsNull_WhenListIsNull()
        {
            var result = NetworkSpawnPoint.SelectSpawnPoint(null, null);
            Assert.IsNull(result);
        }

        [Test]
        public void SelectSpawnPoint_ReturnsNull_WhenListIsEmpty()
        {
            var result = NetworkSpawnPoint.SelectSpawnPoint(
                new List<NetworkSpawnPoint>(), null);
            Assert.IsNull(result);
        }

        [Test]
        public void SelectSpawnPoint_ReturnsOnlyCandidate_WhenOneAvailable()
        {
            var go = new GameObject("Spawn1");
            go.transform.position = new Vector3(10f, 0f, 10f);
            var sp = go.AddComponent<NetworkSpawnPoint>();

            var candidates = new List<NetworkSpawnPoint> { sp };
            var result = NetworkSpawnPoint.SelectSpawnPoint(candidates, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(sp, result);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void SelectSpawnPoint_PrefersFarthestFromLivingPlayers()
        {
            var go1 = new GameObject("Spawn1");
            go1.transform.position = new Vector3(0f, 0f, 0f);
            var sp1 = go1.AddComponent<NetworkSpawnPoint>();

            var go2 = new GameObject("Spawn2");
            go2.transform.position = new Vector3(100f, 0f, 100f);
            var sp2 = go2.AddComponent<NetworkSpawnPoint>();

            var candidates = new List<NetworkSpawnPoint> { sp1, sp2 };
            var livingPositions = new List<Vector3>
            {
                new Vector3(5f, 0f, 5f)
            };

            var result = NetworkSpawnPoint.SelectSpawnPoint(candidates, livingPositions);

            Assert.IsNotNull(result);
            Assert.AreEqual(sp2, result);

            Object.DestroyImmediate(go1);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void SelectSpawnPoint_ReturnsFirst_WhenNoLivingPlayers()
        {
            var go1 = new GameObject("Spawn1");
            go1.transform.position = new Vector3(0f, 0f, 0f);
            var sp1 = go1.AddComponent<NetworkSpawnPoint>();

            var go2 = new GameObject("Spawn2");
            go2.transform.position = new Vector3(100f, 0f, 100f);
            var sp2 = go2.AddComponent<NetworkSpawnPoint>();

            var candidates = new List<NetworkSpawnPoint> { sp1, sp2 };
            var result = NetworkSpawnPoint.SelectSpawnPoint(candidates, new List<Vector3>());

            Assert.IsNotNull(result);
            Assert.AreEqual(sp1, result);

            Object.DestroyImmediate(go1);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void SelectSpawnPoint_SkipsNullEntries()
        {
            var go = new GameObject("Spawn1");
            go.transform.position = new Vector3(10f, 0f, 10f);
            var sp = go.AddComponent<NetworkSpawnPoint>();

            var candidates = new List<NetworkSpawnPoint> { null, sp, null };
            var result = NetworkSpawnPoint.SelectSpawnPoint(candidates, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(sp, result);
            Object.DestroyImmediate(go);
        }
    }
}
