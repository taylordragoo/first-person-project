using NUnit.Framework;
using UnityEngine;
using FPSProject.Combat.Runtime;

namespace FPSProject.Combat.EditModeTests
{
    public class ImpactEffectLibraryTests
    {
        private ImpactEffectLibrary _library;
        private GameObject _defaultDecal;
        private GameObject _defaultImpact;
        private GameObject _metalDecal;
        private GameObject _woodImpact;

        [SetUp]
        public void SetUp()
        {
            _library = ScriptableObject.CreateInstance<ImpactEffectLibrary>();
            _defaultDecal = new GameObject("DefaultDecal");
            _defaultImpact = new GameObject("DefaultImpact");
            _metalDecal = new GameObject("MetalDecal");
            _woodImpact = new GameObject("WoodImpact");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_library);
            Object.DestroyImmediate(_defaultDecal);
            Object.DestroyImmediate(_defaultImpact);
            Object.DestroyImmediate(_metalDecal);
            Object.DestroyImmediate(_woodImpact);
        }

        [Test]
        public void DefaultSurface_UsesDefaultPair()
        {
            _library.defaultPair = new SurfaceEffectPair
            {
                decalPrefab = _defaultDecal,
                impactPrefab = _defaultImpact
            };

            var pair = _library.GetPair(ImpactSurfaceType.Default);
            Assert.AreEqual(_defaultDecal, pair.decalPrefab);
            Assert.AreEqual(_defaultImpact, pair.impactPrefab);
        }

        [Test]
        public void MissingOverride_FallsBackToDefault()
        {
            _library.defaultPair = new SurfaceEffectPair
            {
                decalPrefab = _defaultDecal,
                impactPrefab = _defaultImpact
            };
            // Leave metalPair empty

            var pair = _library.GetPair(ImpactSurfaceType.Metal);
            Assert.AreEqual(_defaultDecal, pair.decalPrefab);
            Assert.AreEqual(_defaultImpact, pair.impactPrefab);
        }

        [Test]
        public void PartialOverride_UsesOverrideDecal_DefaultImpact()
        {
            _library.defaultPair = new SurfaceEffectPair
            {
                decalPrefab = _defaultDecal,
                impactPrefab = _defaultImpact
            };
            _library.metalPair = new SurfaceEffectPair
            {
                decalPrefab = _metalDecal,
                impactPrefab = null
            };

            var pair = _library.GetPair(ImpactSurfaceType.Metal);
            Assert.AreEqual(_metalDecal, pair.decalPrefab);
            Assert.AreEqual(_defaultImpact, pair.impactPrefab);
        }

        [Test]
        public void PartialOverride_UsesDefaultDecal_OverrideImpact()
        {
            _library.defaultPair = new SurfaceEffectPair
            {
                decalPrefab = _defaultDecal,
                impactPrefab = _defaultImpact
            };
            _library.woodPair = new SurfaceEffectPair
            {
                decalPrefab = null,
                impactPrefab = _woodImpact
            };

            var pair = _library.GetPair(ImpactSurfaceType.Wood);
            Assert.AreEqual(_defaultDecal, pair.decalPrefab);
            Assert.AreEqual(_woodImpact, pair.impactPrefab);
        }

        [Test]
        public void FullOverride_UsesBothOverrides()
        {
            _library.defaultPair = new SurfaceEffectPair
            {
                decalPrefab = _defaultDecal,
                impactPrefab = _defaultImpact
            };
            _library.metalPair = new SurfaceEffectPair
            {
                decalPrefab = _metalDecal,
                impactPrefab = _woodImpact
            };

            var pair = _library.GetPair(ImpactSurfaceType.Metal);
            Assert.AreEqual(_metalDecal, pair.decalPrefab);
            Assert.AreEqual(_woodImpact, pair.impactPrefab);
        }
    }
}
