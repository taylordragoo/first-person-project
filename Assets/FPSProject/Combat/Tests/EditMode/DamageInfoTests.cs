using NUnit.Framework;
using UnityEngine;
using FPSProject.Combat.Runtime;

namespace FPSProject.Combat.EditModeTests
{
    public class DamageInfoTests
    {
        [Test]
        public void DamageInfo_ContainsCorrectValues()
        {
            var owner = new GameObject("Owner");
            var weapon = new GameObject("Weapon");

            var info = new DamageInfo(
                25f,
                new Vector3(1, 2, 3),
                Vector3.up,
                Vector3.forward,
                owner,
                weapon);

            Assert.AreEqual(25f, info.Amount);
            Assert.AreEqual(new Vector3(1, 2, 3), info.HitPoint);
            Assert.AreEqual(Vector3.up, info.HitNormal);
            Assert.AreEqual(Vector3.forward, info.TravelDirection);
            Assert.AreEqual(owner, info.InstigatorOwner);
            Assert.AreEqual(weapon, info.SourceWeapon);

            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(weapon);
        }
    }

    public class DamageableResolutionTests
    {
        private GameObject _root;
        private GameObject _child;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root");
            _child = new GameObject("Child");
            _child.transform.SetParent(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void ResolveDamageable_OnCollider()
        {
            var collider = _child.AddComponent<BoxCollider>();
            var damageable = _child.AddComponent<TestDamageable>();

            // Simulate what WeaponCombatRuntime does
            var resolved = collider.GetComponent<IDamageable>();
            if (resolved == null)
                resolved = collider.GetComponentInParent<IDamageable>();

            Assert.IsNotNull(resolved);
            Assert.AreSame(damageable, resolved);
        }

        [Test]
        public void ResolveDamageable_OnParent()
        {
            var collider = _child.AddComponent<BoxCollider>();
            var damageable = _root.AddComponent<TestDamageable>();

            var resolved = collider.GetComponent<IDamageable>();
            if (resolved == null)
                resolved = collider.GetComponentInParent<IDamageable>();

            Assert.IsNotNull(resolved);
            Assert.AreSame(damageable, resolved);
        }

        [Test]
        public void ResolveDamageable_ExactlyOnceDispatch()
        {
            var collider = _child.AddComponent<BoxCollider>();
            var damageable = _child.AddComponent<TestDamageable>();

            var resolved = collider.GetComponent<IDamageable>();
            if (resolved == null)
                resolved = collider.GetComponentInParent<IDamageable>();

            Assert.IsNotNull(resolved);

            var info = new DamageInfo(10f, Vector3.zero, Vector3.up, Vector3.forward, null, null);
            resolved.ApplyDamage(info);

            Assert.AreEqual(1, damageable.ApplyDamageCallCount);
            Assert.AreEqual(10f, damageable.LastDamageAmount);
        }

        private class TestDamageable : MonoBehaviour, IDamageable
        {
            public int ApplyDamageCallCount { get; private set; }
            public float LastDamageAmount { get; private set; }

            public void ApplyDamage(in DamageInfo damageInfo)
            {
                ApplyDamageCallCount++;
                LastDamageAmount = damageInfo.Amount;
            }
        }
    }

    public class ImpactDecalPolicyTests
    {
        [Test]
        public void CharacterMarkerOnParent_SuppressesChildColliderDecal()
        {
            var root = new GameObject("CharacterRoot");
            var child = new GameObject("HitCollider");
            child.transform.SetParent(root.transform);
            root.AddComponent<TestImpactDecalSuppressor>();
            var collider = child.AddComponent<BoxCollider>();

            Assert.IsFalse(ImpactDecalPolicy.ShouldSpawnDecal(collider));

            Object.DestroyImmediate(root);
        }

        [Test]
        public void OrdinarySurface_AllowsDecal()
        {
            var surface = new GameObject("OrdinarySurface");
            var collider = surface.AddComponent<BoxCollider>();

            Assert.IsTrue(ImpactDecalPolicy.ShouldSpawnDecal(collider));

            Object.DestroyImmediate(surface);
        }

        private class TestImpactDecalSuppressor : MonoBehaviour, IImpactDecalSuppressor
        {
        }
    }

    public class CombatRaycastPolicyTests
    {
        [Test]
        public void ShouldSkip_OnlyMarkedCollider()
        {
            var go = new GameObject("BroadGameplayCollider");
            try
            {
                var collider = go.AddComponent<CapsuleCollider>();
                Assert.IsFalse(CombatRaycastPolicy.ShouldSkip(collider));

                go.AddComponent<CombatRaycastPassthrough>();
                Assert.IsTrue(CombatRaycastPolicy.ShouldSkip(collider));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
