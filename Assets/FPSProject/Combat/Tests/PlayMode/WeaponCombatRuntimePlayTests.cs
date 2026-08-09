using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FPSProject.Combat.Runtime;

namespace FPSProject.Combat.PlayModeTests
{
    public class WeaponCombatRuntimePlayTests
    {
        private GameObject _runtimeGo;
        private WeaponCombatRuntime _runtime;
        private Camera _camera;
        private GameObject _ownerRoot;
        private GameObject _weaponObject;
        private GameObject _targetObject;
        private GameObject _decalPrefab;
        private GameObject _spawnedDecal;
        private GameObject _tracerPrefab;
        private ImpactEffectLibrary _effectLibrary;

        [SetUp]
        public void SetUp()
        {
            // Create camera
            var cameraGo = new GameObject("TestCamera");
            _camera = cameraGo.AddComponent<Camera>();
            _camera.transform.position = Vector3.zero;
            _camera.transform.rotation = Quaternion.identity;

            // Create runtime
            _runtimeGo = new GameObject("CombatRuntime");
            _runtime = _runtimeGo.AddComponent<WeaponCombatRuntime>();

            // Assign camera via reflection (it's a private serialized field)
            var cameraField = typeof(WeaponCombatRuntime).GetField("_aimCamera",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            cameraField.SetValue(_runtime, _camera);

            // Create owner and weapon
            _ownerRoot = new GameObject("OwnerRoot");
            _weaponObject = new GameObject("WeaponObject");
            _weaponObject.transform.SetParent(_ownerRoot.transform);

            // Create target
            _targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _targetObject.transform.position = new Vector3(0, 0, 10f);
            _targetObject.AddComponent<TestDamageable>();

            // SubmitShot resolves contacts synchronously. Ensure the physics
            // scene sees the transforms created and moved during this setup.
            Physics.SyncTransforms();

            // Create effect library
            _effectLibrary = ScriptableObject.CreateInstance<ImpactEffectLibrary>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_spawnedDecal != null) Object.DestroyImmediate(_spawnedDecal);
            if (_decalPrefab != null) Object.DestroyImmediate(_decalPrefab);
            if (_tracerPrefab != null) Object.DestroyImmediate(_tracerPrefab);
            Object.DestroyImmediate(_runtimeGo);
            Object.DestroyImmediate(_camera.gameObject);
            Object.DestroyImmediate(_ownerRoot);
            Object.DestroyImmediate(_targetObject);
            Object.DestroyImmediate(_effectLibrary);
        }

        [UnityTest]
        public IEnumerator StaticDecal_RemainsInWorldSpaceWhenRuntimeMoves()
        {
            _targetObject.isStatic = true;
            _decalPrefab = new GameObject("TestDecalPrefab");
            _decalPrefab.SetActive(false);
            _effectLibrary.defaultPair = new SurfaceEffectPair
            {
                decalPrefab = _decalPrefab,
                impactPrefab = null
            };

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 1f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                impactEffectLibrary = _effectLibrary
            };

            var request = new WeaponShotRequest(
                ballistics,
                _ownerRoot,
                _weaponObject,
                new Vector3(0, 0, 0.5f),
                Quaternion.identity,
                Vector3.zero,
                Vector3.forward);

            Physics.SyncTransforms();
            _runtime.SubmitShot(request);

            _spawnedDecal = GameObject.Find(_decalPrefab.name);
            Assert.IsNotNull(_spawnedDecal);
            Assert.IsNull(_spawnedDecal.transform.parent,
                "An active decal must be detached from the runtime pool parent.");

            Vector3 worldPosition = _spawnedDecal.transform.position;
            _runtimeGo.transform.position = new Vector3(5f, 1f, -3f);
            yield return null;

            Assert.Less(Vector3.Distance(worldPosition, _spawnedDecal.transform.position), 0.0001f);
        }

        [UnityTest]
        public IEnumerator Hitscan_AppliesDamageOnce()
        {
            var damageable = _targetObject.GetComponent<TestDamageable>();

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 25f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                impactEffectLibrary = _effectLibrary
            };

            var request = new WeaponShotRequest(
                ballistics,
                _ownerRoot,
                _weaponObject,
                new Vector3(0, 0, 0.5f), // muzzle position
                Quaternion.identity,
                Vector3.zero, // camera origin
                Vector3.forward); // camera direction

            _runtime.SubmitShot(request);

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.AreEqual(1, damageable.ApplyDamageCallCount);
            Assert.AreEqual(25f, damageable.LastDamageAmount);
        }

        [UnityTest]
        public IEnumerator Hitscan_Miss_CreatesTracer()
        {
            // Point camera away from target
            _camera.transform.rotation = Quaternion.Euler(0, 90, 0);

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 25f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                impactEffectLibrary = _effectLibrary
            };

            var request = new WeaponShotRequest(
                ballistics,
                _ownerRoot,
                _weaponObject,
                new Vector3(0, 0, 0.5f),
                Quaternion.identity,
                _camera.transform.position,
                _camera.transform.forward);

            _runtime.SubmitShot(request);

            yield return new WaitForFixedUpdate();
            yield return null;

            // No damage should be applied on miss
            var damageable = _targetObject.GetComponent<TestDamageable>();
            Assert.AreEqual(0, damageable.ApplyDamageCallCount);
        }

        [UnityTest]
        public IEnumerator Tracer_RemainsActiveForItsConfiguredTravelTime()
        {
            _tracerPrefab = new GameObject("TestTracerPrefab");
            _tracerPrefab.AddComponent<TrailRenderer>().time = 0.1f;
            _tracerPrefab.SetActive(false);

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 1f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                tracerPrefab = _tracerPrefab,
                tracerSpeed = 40f,
                tracerLifetime = 0.05f,
                impactEffectLibrary = _effectLibrary
            };

            var request = new WeaponShotRequest(
                ballistics,
                _ownerRoot,
                _weaponObject,
                new Vector3(0, 0, 0.5f),
                Quaternion.identity,
                Vector3.zero,
                Vector3.forward);

            Physics.SyncTransforms();
            _runtime.SubmitShot(request);

            yield return new WaitForSeconds(0.1f);

            var activeTracer = GameObject.Find(_tracerPrefab.name);
            Assert.IsNotNull(activeTracer,
                "The tracer must remain active while it is still travelling to the hit point.");
            Assert.Greater(activeTracer.transform.position.z, 0.5f);
            Assert.Less(activeTracer.transform.position.z, 9.5f);
        }

        [UnityTest]
        public IEnumerator OwnerCollider_IsRejected()
        {
            // Add a collider to the owner that's in front of the target
            var ownerCollider = _ownerRoot.AddComponent<BoxCollider>();
            ownerCollider.center = new Vector3(0, 0, 1f);
            ownerCollider.size = Vector3.one;
            Physics.SyncTransforms();

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 25f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                impactEffectLibrary = _effectLibrary
            };

            var request = new WeaponShotRequest(
                ballistics,
                _ownerRoot,
                _weaponObject,
                new Vector3(0, 0, 0.5f),
                Quaternion.identity,
                Vector3.zero,
                Vector3.forward);

            _runtime.SubmitShot(request);

            yield return new WaitForFixedUpdate();
            yield return null;

            // Should still hit the target behind the owner
            var damageable = _targetObject.GetComponent<TestDamageable>();
            Assert.AreEqual(1, damageable.ApplyDamageCallCount);
        }

        [UnityTest]
        public IEnumerator MuzzleObstruction_WinsOverCameraTarget()
        {
            // Place an obstruction right in front of the muzzle
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.transform.position = new Vector3(0, 0, 1f);
            obstruction.transform.localScale = new Vector3(2, 2, 0.1f);
            var obstructionDamageable = obstruction.AddComponent<TestDamageable>();
            Physics.SyncTransforms();

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 25f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                impactEffectLibrary = _effectLibrary
            };

            var request = new WeaponShotRequest(
                ballistics,
                _ownerRoot,
                _weaponObject,
                new Vector3(0, 0, 0.5f), // muzzle behind obstruction
                Quaternion.identity,
                Vector3.zero,
                Vector3.forward); // camera sees target at z=10

            _runtime.SubmitShot(request);

            yield return new WaitForFixedUpdate();
            yield return null;

            // Obstruction should take the hit, not the target
            Assert.AreEqual(1, obstructionDamageable.ApplyDamageCallCount);
            var targetDamageable = _targetObject.GetComponent<TestDamageable>();
            Assert.AreEqual(0, targetDamageable.ApplyDamageCallCount);

            Object.DestroyImmediate(obstruction);
        }

        [UnityTest]
        public IEnumerator CameraHit_IsRetainedWhenMuzzleStartsInsideHitSurface()
        {
            _decalPrefab = new GameObject("TestInsideSurfaceDecalPrefab");
            _decalPrefab.SetActive(false);
            _effectLibrary.defaultPair = new SurfaceEffectPair
            {
                decalPrefab = _decalPrefab,
                impactPrefab = null
            };

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 10f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                impactEffectLibrary = _effectLibrary
            };

            // Simulate recoil/weapon animation pushing the muzzle just inside the
            // wall while the camera still has a valid hit on its front surface.
            var request = new WeaponShotRequest(
                ballistics,
                _ownerRoot,
                _weaponObject,
                _targetObject.transform.position,
                Quaternion.identity,
                Vector3.zero,
                Vector3.forward);

            Physics.SyncTransforms();
            _runtime.SubmitShot(request);

            yield return null;

            var damageable = _targetObject.GetComponent<TestDamageable>();
            Assert.AreEqual(1, damageable.ApplyDamageCallCount);

            _spawnedDecal = GameObject.Find(_decalPrefab.name);
            Assert.IsNotNull(_spawnedDecal,
                "A valid camera hit must still spawn its decal when the muzzle starts inside the surface.");
        }

        [UnityTest]
        public IEnumerator PlayImpact_SpawnsSurfacePairWithoutApplyingDamage()
        {
            _decalPrefab = new GameObject("TestNetworkFleshDecalPrefab");
            _decalPrefab.SetActive(false);
            _effectLibrary.fleshPair = new SurfaceEffectPair
            {
                decalPrefab = _decalPrefab,
                impactPrefab = null
            };

            _runtime.PlayImpact(
                _effectLibrary,
                new Vector3(1f, 2f, 3f),
                Vector3.up,
                ImpactSurfaceType.Flesh,
                _targetObject.transform);

            yield return null;

            _spawnedDecal = GameObject.Find(_decalPrefab.name);
            Assert.IsNotNull(_spawnedDecal);
            Assert.AreEqual(0, _targetObject.GetComponent<TestDamageable>().ApplyDamageCallCount);
        }

        [UnityTest]
        public IEnumerator RepeatedSubmitShot_EachCreatesOneShot()
        {
            var damageable = _targetObject.GetComponent<TestDamageable>();

            var ballistics = new WeaponBallisticsSettings
            {
                combatEnabled = true,
                shotType = WeaponShotType.Hitscan,
                damage = 10f,
                maxRange = 100f,
                hitMask = ~0,
                triggerInteraction = QueryTriggerInteraction.Ignore,
                spreadDegrees = 0f,
                impactEffectLibrary = _effectLibrary
            };

            for (int i = 0; i < 5; i++)
            {
                var request = new WeaponShotRequest(
                    ballistics,
                    _ownerRoot,
                    _weaponObject,
                    new Vector3(0, 0, 0.5f),
                    Quaternion.identity,
                    Vector3.zero,
                    Vector3.forward);

                _runtime.SubmitShot(request);
            }

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.AreEqual(5, damageable.ApplyDamageCallCount);
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
}
