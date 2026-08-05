using System.Collections.Generic;
using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Core combat runtime. The only shot entry point used by the CAS bridge.
    /// Handles hitscan and projectile shots through a shared aim, damage,
    /// surface, impact, decal, and tracer pipeline.
    /// </summary>
    public class WeaponCombatRuntime : MonoBehaviour
    {
        [Header("Aim")]
        [Tooltip("The camera used for aim ray origin and direction.")]
        [SerializeField] private Camera _aimCamera;

        [Header("Decal Settings")]
        [Tooltip("Distance above the surface to place decals.")]
        [SerializeField] private float _decalSurfaceOffset = 0.005f;

        [Header("Pool Capacities (per prefab)")]
        [SerializeField] private int _decalPoolCapacity = 32;
        [SerializeField] private int _impactPoolCapacity = 16;
        [SerializeField] private int _tracerPoolCapacity = 16;
        [SerializeField] private int _projectilePoolCapacity = 16;

        [Header("Default Lifetimes")]
        [Tooltip("Default lifetime for decals in seconds.")]
        [SerializeField] private float _defaultDecalLifetime = 20f;
        [Tooltip("Default lifetime for transient impact effects in seconds.")]
        [SerializeField] private float _defaultImpactLifetime = 2f;

        private PoolManager _poolManager;
        private readonly List<ActiveDecal> _activeDecals = new List<ActiveDecal>();
        private readonly List<ActiveImpact> _activeImpacts = new List<ActiveImpact>();
        private readonly List<ActiveTracer> _activeTracers = new List<ActiveTracer>();
        private int _nextDecalSortingOrder = 1;

        // Track pooled projectiles for cleanup
        private readonly Dictionary<GameObject, HashSet<WeaponCombatProjectile>> _activeProjectiles =
            new Dictionary<GameObject, HashSet<WeaponCombatProjectile>>();

        public Camera AimCamera => _aimCamera;

        private struct ActiveDecal
        {
            public GameObject instance;
            public GameObject prefab;
            public Transform targetTransform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public float lifetime;
        }

        private struct ActiveImpact
        {
            public GameObject instance;
            public GameObject prefab;
            public float lifetime;
        }

        private struct ActiveTracer
        {
            public GameObject instance;
            public GameObject prefab;
            public Vector3 startPosition;
            public Vector3 endPosition;
            public float speed;
            public float lifetime;
            public float elapsed;
        }

        private void Awake()
        {
            _poolManager = new PoolManager(
                transform,
                _decalPoolCapacity,
                _impactPoolCapacity,
                _tracerPoolCapacity,
                _projectilePoolCapacity);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            UpdateDecals(dt);
            UpdateImpacts(dt);
            UpdateTracers(dt);
        }

        private void OnDestroy()
        {
            _poolManager?.Clear();
        }

        private void OnValidate()
        {
            if (_aimCamera == null)
            {
                Debug.LogWarning($"[{nameof(WeaponCombatRuntime)}] Aim Camera is not assigned. " +
                    "Combat shots will fail until a Camera is assigned.");
            }
        }

        /// <summary>
        /// Submit a shot request. This is the only entry point used by the CAS bridge.
        /// </summary>
        public void SubmitShot(in WeaponShotRequest request)
        {
            if (!request.Ballistics.combatEnabled) return;
            if (_aimCamera == null)
            {
                Debug.LogWarning($"[{nameof(WeaponCombatRuntime)}] Cannot submit shot: Aim Camera is null.");
                return;
            }

            // Resolve camera aim with spread
            Vector3 cameraOrigin = request.CameraOrigin;
            Vector3 cameraDirection = request.CameraDirection;

            // Apply spread
            float spreadDeg = request.Ballistics.spreadDegrees;
            if (spreadDeg > 0f)
            {
                float spreadRad = spreadDeg * Mathf.Deg2Rad;
                Vector3 randomOffset = Random.insideUnitSphere * Mathf.Tan(spreadRad);
                cameraDirection = (cameraDirection + randomOffset).normalized;
            }

            // Camera query
            float maxRange = request.Ballistics.maxRange;
            var hitMask = request.Ballistics.hitMask;
            var triggerInteraction = request.Ballistics.triggerInteraction;

            // Start with the unobstructed camera endpoint so the value is always
            // defined, even when the non-alloc query returns no accepted hits.
            Vector3 desiredDestination = cameraOrigin + cameraDirection * maxRange;

            RaycastHit[] cameraHits = new RaycastHit[32];
            int cameraHitCount = Physics.RaycastNonAlloc(
                cameraOrigin, cameraDirection, cameraHits,
                maxRange, hitMask, triggerInteraction);

            if (cameraHitCount == cameraHits.Length)
            {
                cameraHits = new RaycastHit[cameraHits.Length * 2];
                cameraHitCount = Physics.RaycastNonAlloc(
                    cameraOrigin, cameraDirection, cameraHits,
                    maxRange, hitMask, triggerInteraction);
            }

            // Find nearest accepted camera hit
            float nearestCamDist = float.MaxValue;
            for (int i = 0; i < cameraHitCount; i++)
            {
                var hit = cameraHits[i];
                if (IsOwnerOrDescendant(hit.transform, request.OwnerRoot))
                    continue;

                if (hit.distance < nearestCamDist)
                {
                    nearestCamDist = hit.distance;
                    desiredDestination = hit.point;
                }
            }

            // Muzzle query: aim from muzzle toward desired destination
            Vector3 muzzlePosition = request.MuzzlePosition;
            Vector3 muzzleDirection = (desiredDestination - muzzlePosition).normalized;
            float muzzleDistance = Vector3.Distance(muzzlePosition, desiredDestination);
            muzzleDistance = Mathf.Min(muzzleDistance, maxRange);

            Vector3 authoritativeEndpoint;
            RaycastHit? authoritativeHit = null;

            RaycastHit[] muzzleHits = new RaycastHit[32];
            int muzzleHitCount = Physics.RaycastNonAlloc(
                muzzlePosition, muzzleDirection, muzzleHits,
                muzzleDistance, hitMask, triggerInteraction);

            if (muzzleHitCount == muzzleHits.Length)
            {
                muzzleHits = new RaycastHit[muzzleHits.Length * 2];
                muzzleHitCount = Physics.RaycastNonAlloc(
                    muzzlePosition, muzzleDirection, muzzleHits,
                    muzzleDistance, hitMask, triggerInteraction);
            }

            float nearestMuzzleDist = float.MaxValue;
            for (int i = 0; i < muzzleHitCount; i++)
            {
                var hit = muzzleHits[i];
                if (IsOwnerOrDescendant(hit.transform, request.OwnerRoot))
                    continue;

                if (hit.distance < nearestMuzzleDist)
                {
                    nearestMuzzleDist = hit.distance;
                    authoritativeHit = hit;
                }
            }

            if (authoritativeHit.HasValue)
            {
                authoritativeEndpoint = authoritativeHit.Value.point;
            }
            else
            {
                authoritativeEndpoint = muzzlePosition + muzzleDirection * muzzleDistance;
            }

            // Spawn tracer from muzzle to authoritative endpoint
            SpawnTracer(request, muzzlePosition, authoritativeEndpoint);

            // Handle based on shot type
            if (request.Ballistics.shotType == WeaponShotType.Hitscan)
            {
                if (authoritativeHit.HasValue)
                {
                    ResolveContact(request, authoritativeHit.Value);
                }
            }
            else // Projectile
            {
                SpawnProjectile(request, muzzleDirection);
            }
        }

        /// <summary>
        /// Resolve a hitscan contact: apply damage, spawn impact and decal.
        /// </summary>
        public void ResolveContact(in WeaponShotRequest request, RaycastHit hit)
        {
            Vector3 travelDirection = (hit.point - request.MuzzlePosition).normalized;

            var damageInfo = new DamageInfo(
                request.Ballistics.damage,
                hit.point,
                hit.normal,
                travelDirection,
                request.OwnerRoot,
                request.WeaponObject);

            // Resolve IDamageable: check collider first, then closest parent
            ApplyDamage(hit.collider, damageInfo);

            // Resolve surface type and spawn effects
            ImpactSurfaceType surfaceType = ImpactSurface.Resolve(hit.collider);
            SpawnImpactEffects(request, hit.point, hit.normal, surfaceType, hit.transform);
        }

        /// <summary>
        /// Resolve a projectile sphere-sweep contact.
        /// </summary>
        public void ResolveProjectileContact(WeaponCombatProjectile projectile, RaycastHit hit)
        {
            // We need to reconstruct the shot request from the projectile
            // The projectile stores it internally - we access via a public method
            // For now, we use the hit directly
            var request = projectile.GetComponent<ProjectileShotData>()?.Request
                ?? default;

            Vector3 travelDirection = projectile.Velocity.normalized;

            var damageInfo = new DamageInfo(
                request.Ballistics.damage,
                hit.point,
                hit.normal,
                travelDirection,
                request.OwnerRoot,
                request.WeaponObject);

            ApplyDamage(hit.collider, damageInfo);

            ImpactSurfaceType surfaceType = ImpactSurface.Resolve(hit.collider);
            SpawnImpactEffects(request, hit.point, hit.normal, surfaceType, hit.transform);

            projectile.ReturnToPool();
        }

        /// <summary>
        /// Resolve a projectile initial overlap contact.
        /// </summary>
        public void ResolveProjectileOverlapContact(
            WeaponCombatProjectile projectile, Collider collider,
            Vector3 point, Vector3 normal)
        {
            var request = projectile.GetComponent<ProjectileShotData>()?.Request
                ?? default;

            Vector3 travelDirection = projectile.Velocity.normalized;

            var damageInfo = new DamageInfo(
                request.Ballistics.damage,
                point,
                normal,
                travelDirection,
                request.OwnerRoot,
                request.WeaponObject);

            ApplyDamage(collider, damageInfo);

            ImpactSurfaceType surfaceType = ImpactSurface.Resolve(collider);
            SpawnImpactEffects(request, point, normal, surfaceType, collider.transform);

            projectile.ReturnToPool();
        }

        /// <summary>
        /// Called by WeaponCombatProjectile when it is externally destroyed.
        /// </summary>
        public void OnProjectileDestroyed(GameObject prefabKey, WeaponCombatProjectile projectile)
        {
            if (_activeProjectiles.TryGetValue(prefabKey, out var set))
            {
                set.Remove(projectile);
            }
        }

        /// <summary>
        /// Return a projectile to its pool.
        /// </summary>
        public void ReturnProjectile(GameObject prefabKey, WeaponCombatProjectile projectile)
        {
            _poolManager.ReturnProjectile(prefabKey, projectile);
        }

        private void SpawnProjectile(in WeaponShotRequest request, Vector3 muzzleDirection)
        {
            var prefab = request.Ballistics.projectilePrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[{nameof(WeaponCombatRuntime)}] Projectile prefab is null.");
                return;
            }

            var projectile = _poolManager.RentProjectile(prefab);
            if (projectile == null) return;

            // Active pooled effects must live in world space. Leaving them parented
            // to this runtime makes them inherit player movement after spawning.
            projectile.transform.SetParent(null, true);

            // Attach shot data so the projectile can reconstruct the request
            var shotData = projectile.GetComponent<ProjectileShotData>();
            if (shotData == null)
                shotData = projectile.gameObject.AddComponent<ProjectileShotData>();
            shotData.Request = request;

            Vector3 velocity = muzzleDirection * request.Ballistics.projectileSpeed;
            projectile.Launch(request, velocity, this, prefab);

            // Track active projectile
            if (!_activeProjectiles.ContainsKey(prefab))
                _activeProjectiles[prefab] = new HashSet<WeaponCombatProjectile>();
            _activeProjectiles[prefab].Add(projectile);
        }

        private void SpawnTracer(in WeaponShotRequest request, Vector3 start, Vector3 end)
        {
            var prefab = request.Ballistics.tracerPrefab;
            if (prefab == null) return;

            var instance = _poolManager.RentTracer(prefab);
            if (instance == null) return;

            instance.transform.SetParent(null, true);
            instance.transform.position = start;
            instance.transform.rotation = Quaternion.LookRotation((end - start).normalized);

            float speed = request.Ballistics.tracerSpeed > 0f ? request.Ballistics.tracerSpeed : 200f;
            float lifetime = request.Ballistics.tracerLifetime > 0f ? request.Ballistics.tracerLifetime : 0.1f;

            _activeTracers.Add(new ActiveTracer
            {
                instance = instance,
                prefab = prefab,
                startPosition = start,
                endPosition = end,
                speed = speed,
                lifetime = lifetime,
                elapsed = 0f
            });
        }

        private void SpawnImpactEffects(
            in WeaponShotRequest request,
            Vector3 point, Vector3 normal,
            ImpactSurfaceType surfaceType,
            Transform hitTransform)
        {
            var library = request.Ballistics.impactEffectLibrary;
            if (library == null) return;

            var pair = library.GetPair(surfaceType);
            Vector3 surfaceNormal = normal.sqrMagnitude > 0.000001f
                ? normal.normalized
                : Vector3.forward;
            Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.forward, surfaceNormal);

            // Spawn decal
            if (pair.decalPrefab != null)
            {
                var decal = _poolManager.RentDecal(pair.decalPrefab);
                if (decal != null)
                {
                    decal.transform.SetParent(null, true);

                    float surfaceOffset = Mathf.Max(_decalSurfaceOffset, 0.005f);
                    Vector3 decalPos = point + surfaceNormal * surfaceOffset;

                    // Align local +Z to the surface normal, then apply a stable
                    // random twist without relying on LookRotation's up vector.
                    float randomAngle = Random.Range(0f, 360f);
                    Quaternion decalRot =
                        Quaternion.AngleAxis(randomAngle, surfaceNormal) * surfaceRotation;

                    decal.transform.position = decalPos;
                    decal.transform.rotation = decalRot;

                    // Give overlapping transparent sprites a deterministic draw
                    // order so repeated shots at one point do not flicker.
                    var spriteRenderers = decal.GetComponentsInChildren<SpriteRenderer>(true);
                    foreach (var spriteRenderer in spriteRenderers)
                    {
                        spriteRenderer.sortingOrder = _nextDecalSortingOrder;
                    }
                    _nextDecalSortingOrder = _nextDecalSortingOrder >= 30000
                        ? 1
                        : _nextDecalSortingOrder + 1;

                    // If the hit transform is moving, store relative pose for tracking
                    if (hitTransform != null && !hitTransform.gameObject.isStatic)
                    {
                        _activeDecals.Add(new ActiveDecal
                        {
                            instance = decal,
                            prefab = pair.decalPrefab,
                            targetTransform = hitTransform,
                            localPosition = hitTransform.InverseTransformPoint(decalPos),
                            localRotation = Quaternion.Inverse(hitTransform.rotation) * decalRot,
                            lifetime = _defaultDecalLifetime
                        });
                    }
                    else
                    {
                        _activeDecals.Add(new ActiveDecal
                        {
                            instance = decal,
                            prefab = pair.decalPrefab,
                            targetTransform = null,
                            localPosition = Vector3.zero,
                            localRotation = Quaternion.identity,
                            lifetime = _defaultDecalLifetime
                        });
                    }
                }
            }

            // Spawn transient impact
            if (pair.impactPrefab != null)
            {
                var impact = _poolManager.RentImpact(pair.impactPrefab);
                if (impact != null)
                {
                    impact.transform.SetParent(null, true);
                    impact.transform.position = point;
                    impact.transform.rotation = surfaceRotation;

                    // Restart particle systems
                    var particleSystems = impact.GetComponentsInChildren<ParticleSystem>();
                    foreach (var ps in particleSystems)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.Play();
                    }

                    _activeImpacts.Add(new ActiveImpact
                    {
                        instance = impact,
                        prefab = pair.impactPrefab,
                        lifetime = _defaultImpactLifetime
                    });
                }
            }
        }

        private void ApplyDamage(Collider collider, in DamageInfo damageInfo)
        {
            if (collider == null) return;

            // Check collider first
            var damageable = collider.GetComponent<IDamageable>();
            if (damageable == null)
            {
                // Check closest parent
                damageable = collider.GetComponentInParent<IDamageable>();
            }

            damageable?.ApplyDamage(damageInfo);
        }

        private bool IsOwnerOrDescendant(Transform t, GameObject ownerRoot)
        {
            if (t == null || ownerRoot == null) return false;

            Transform current = t;
            while (current != null)
            {
                if (current.gameObject == ownerRoot)
                    return true;
                current = current.parent;
            }
            return false;
        }

        private void UpdateDecals(float dt)
        {
            for (int i = _activeDecals.Count - 1; i >= 0; i--)
            {
                var decal = _activeDecals[i];

                if (decal.instance == null)
                {
                    _activeDecals.RemoveAt(i);
                    continue;
                }

                decal.lifetime -= dt;

                // Check if target was destroyed
                if (decal.targetTransform != null && decal.targetTransform.gameObject == null)
                {
                    _poolManager.ReturnDecal(decal.prefab, decal.instance);
                    _activeDecals.RemoveAt(i);
                    continue;
                }

                // Update position for moving targets
                if (decal.targetTransform != null)
                {
                    decal.instance.transform.position =
                        decal.targetTransform.TransformPoint(decal.localPosition);
                    decal.instance.transform.rotation =
                        decal.targetTransform.rotation * decal.localRotation;
                }

                // Return on lifetime expiry
                if (decal.lifetime <= 0f)
                {
                    _poolManager.ReturnDecal(decal.prefab, decal.instance);
                    _activeDecals.RemoveAt(i);
                }
                else
                {
                    _activeDecals[i] = decal;
                }
            }
        }

        private void UpdateImpacts(float dt)
        {
            for (int i = _activeImpacts.Count - 1; i >= 0; i--)
            {
                var impact = _activeImpacts[i];

                if (impact.instance == null)
                {
                    _activeImpacts.RemoveAt(i);
                    continue;
                }

                impact.lifetime -= dt;

                if (impact.lifetime <= 0f)
                {
                    // Stop particles before returning
                    var particleSystems = impact.instance.GetComponentsInChildren<ParticleSystem>();
                    foreach (var ps in particleSystems)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }

                    _poolManager.ReturnImpact(impact.prefab, impact.instance);
                    _activeImpacts.RemoveAt(i);
                }
                else
                {
                    _activeImpacts[i] = impact;
                }
            }
        }

        private void UpdateTracers(float dt)
        {
            for (int i = _activeTracers.Count - 1; i >= 0; i--)
            {
                var tracer = _activeTracers[i];

                if (tracer.instance == null)
                {
                    _activeTracers.RemoveAt(i);
                    continue;
                }

                tracer.elapsed += dt;

                if (tracer.elapsed >= tracer.lifetime)
                {
                    _poolManager.ReturnTracer(tracer.prefab, tracer.instance);
                    _activeTracers.RemoveAt(i);
                    continue;
                }

                // Move tracer toward endpoint
                float t = tracer.elapsed / tracer.lifetime;
                Vector3 currentPos = Vector3.Lerp(tracer.startPosition, tracer.endPosition, t);
                tracer.instance.transform.position = currentPos;

                _activeTracers[i] = tracer;
            }
        }
    }
}
