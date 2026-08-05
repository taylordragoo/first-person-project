using NUnit.Framework;
using UnityEngine;
using FPSProject.Combat.Runtime;

namespace FPSProject.Combat.EditModeTests
{
    public class ImpactSurfaceTests
    {
        private GameObject _root;
        private GameObject _child;
        private GameObject _grandchild;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root");
            _child = new GameObject("Child");
            _grandchild = new GameObject("Grandchild");

            _child.transform.SetParent(_root.transform);
            _grandchild.transform.SetParent(_child.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void DefaultSurface_WhenNoComponent_ReturnsDefault()
        {
            var collider = _grandchild.AddComponent<BoxCollider>();
            var result = ImpactSurface.Resolve(collider);
            Assert.AreEqual(ImpactSurfaceType.Default, result);
        }

        [Test]
        public void ExplicitOverride_OnCollider_ReturnsOverrideType()
        {
            var collider = _grandchild.AddComponent<BoxCollider>();
            var surface = _grandchild.AddComponent<ImpactSurface>();
            // Use reflection to set private field since we can't access it directly
            var field = typeof(ImpactSurface).GetField("_surfaceType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(surface, ImpactSurfaceType.Metal);

            var result = ImpactSurface.Resolve(collider);
            Assert.AreEqual(ImpactSurfaceType.Metal, result);
        }

        [Test]
        public void ChildCollider_ParentSurface_ParentPrecedence()
        {
            var collider = _grandchild.AddComponent<BoxCollider>();
            var surface = _child.AddComponent<ImpactSurface>();
            var field = typeof(ImpactSurface).GetField("_surfaceType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(surface, ImpactSurfaceType.Wood);

            var result = ImpactSurface.Resolve(collider);
            Assert.AreEqual(ImpactSurfaceType.Wood, result);
        }

        [Test]
        public void ColliderOverride_WinsOverParent()
        {
            var collider = _grandchild.AddComponent<BoxCollider>();
            var childSurface = _child.AddComponent<ImpactSurface>();
            var grandchildSurface = _grandchild.AddComponent<ImpactSurface>();

            var field = typeof(ImpactSurface).GetField("_surfaceType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(childSurface, ImpactSurfaceType.Wood);
            field.SetValue(grandchildSurface, ImpactSurfaceType.Metal);

            var result = ImpactSurface.Resolve(collider);
            Assert.AreEqual(ImpactSurfaceType.Metal, result);
        }

        [Test]
        public void NullCollider_ReturnsDefault()
        {
            var result = ImpactSurface.Resolve(null);
            Assert.AreEqual(ImpactSurfaceType.Default, result);
        }
    }
}
