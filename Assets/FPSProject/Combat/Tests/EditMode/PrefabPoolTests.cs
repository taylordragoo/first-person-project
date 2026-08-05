using NUnit.Framework;
using UnityEngine;
using FPSProject.Combat.Runtime;

namespace FPSProject.Combat.EditModeTests
{
    public class PrefabPoolTests
    {
        private GameObject _prefab;
        private GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("PoolPrefab");
            _prefab.AddComponent<TestPoolComponent>();
            _prefab.SetActive(false);

            _parent = new GameObject("PoolParent");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_parent);
        }

        [Test]
        public void Rent_CreatesNewInstance()
        {
            var pool = new PrefabPool<TestPoolComponent>(_prefab, _parent.transform, 4);
            var instance = pool.Rent();

            Assert.IsNotNull(instance);
            Assert.IsTrue(instance.gameObject.activeSelf);
        }

        [Test]
        public void Return_DeactivatesInstance()
        {
            var pool = new PrefabPool<TestPoolComponent>(_prefab, _parent.transform, 4);
            var instance = pool.Rent();
            pool.Return(instance);

            Assert.IsFalse(instance.gameObject.activeSelf);
            Assert.AreEqual(_parent.transform, instance.transform.parent);
        }

        [Test]
        public void Rent_ReusesReturnedInstance()
        {
            var pool = new PrefabPool<TestPoolComponent>(_prefab, _parent.transform, 4);
            var first = pool.Rent();
            pool.Return(first);
            var second = pool.Rent();

            Assert.AreSame(first, second);
        }

        [Test]
        public void PreWarm_CreatesCapacityInstances()
        {
            var pool = new PrefabPool<TestPoolComponent>(_prefab, _parent.transform, 4);
            pool.PreWarm();

            // Rent 4 - should all come from pre-warmed pool
            var instances = new TestPoolComponent[4];
            for (int i = 0; i < 4; i++)
            {
                instances[i] = pool.Rent();
                Assert.IsNotNull(instances[i]);
            }

            // 5th rent should create a new one (expansion)
            var fifth = pool.Rent();
            Assert.IsNotNull(fifth);
        }

        [Test]
        public void ReturnAll_DeactivatesAll()
        {
            var pool = new PrefabPool<TestPoolComponent>(_prefab, _parent.transform, 4);
            var a = pool.Rent();
            var b = pool.Rent();

            pool.ReturnAll();

            Assert.IsFalse(a.gameObject.activeSelf);
            Assert.IsFalse(b.gameObject.activeSelf);
        }

        private class TestPoolComponent : MonoBehaviour { }
    }
}
