using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Pooled physical projectile updated during fixed simulation steps.
    /// Handles sphere-sweep movement, gravity, initial overlap detection,
    /// and returns itself to the pool on contact, lifetime expiry, or max range.
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public class WeaponCombatProjectile : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private TrailRenderer _trailRenderer;
        [SerializeField] private Rigidbody _rigidbody;

        // Shot data retained for the projectile's lifetime
        private WeaponShotRequest _shotRequest;
        private Vector3 _velocity;
        private float _lifetime;
        private float _distanceTraveled;
        private bool _hasContacted;

        // Pool reference
        private WeaponCombatRuntime _runtime;
        private GameObject _prefabKey;

        public Vector3 Velocity => _velocity;
        public bool HasContacted => _hasContacted;

        private void Awake()
        {
            if (_trailRenderer == null)
                _trailRenderer = GetComponent<TrailRenderer>();
            if (_rigidbody == null)
                _rigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Initialize the projectile for launch. Called by the pool/combat runtime.
        /// </summary>
        public void Launch(WeaponShotRequest request, Vector3 velocity, WeaponCombatRuntime runtime, GameObject prefabKey)
        {
            _shotRequest = request;
            _velocity = velocity;
            _lifetime = request.Ballistics.projectileLifetime > 0f ? request.Ballistics.projectileLifetime : 5f;
            _distanceTraveled = 0f;
            _hasContacted = false;
            _runtime = runtime;
            _prefabKey = prefabKey;

            transform.position = request.MuzzlePosition;
            transform.rotation = Quaternion.LookRotation(velocity.normalized);

            // Reset trail
            if (_trailRenderer != null)
            {
                _trailRenderer.Clear();
                _trailRenderer.emitting = true;
                _trailRenderer.time = Mathf.Min(_lifetime, 0.5f);
            }

            // Reset rigidbody
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            gameObject.SetActive(true);

            // Perform initial overlap check at spawn position
            PerformInitialOverlapCheck();
        }

        private void FixedUpdate()
        {
            if (_hasContacted) return;

            float dt = Time.fixedDeltaTime;
            _lifetime -= dt;

            // Check lifetime expiry
            if (_lifetime <= 0f)
            {
                ReturnToPool();
                return;
            }

            // Check max range
            if (_distanceTraveled >= _shotRequest.Ballistics.maxRange)
            {
                ReturnToPool();
                return;
            }

            // Apply gravity
            var ballistics = _shotRequest.Ballistics;
            if (ballistics.projectileGravityEnabled)
            {
                _velocity += Physics.gravity * ballistics.projectileGravityMultiplier * dt;
            }

            Vector3 currentPos = transform.position;
            Vector3 displacement = _velocity * dt;
            float sweepDistance = displacement.magnitude;

            // Clamp to max range
            float remainingRange = ballistics.maxRange - _distanceTraveled;
            if (sweepDistance > remainingRange)
            {
                displacement = displacement.normalized * remainingRange;
                sweepDistance = remainingRange;
            }

            // Sphere sweep
            float radius = ballistics.projectileSweepRadius > 0f ? ballistics.projectileSweepRadius : 0.01f;
            var hitMask = ballistics.hitMask;
            var triggerInteraction = ballistics.triggerInteraction;

            // Use non-allocating buffer approach
            RaycastHit[] hits = new RaycastHit[32];
            int hitCount = Physics.SphereCastNonAlloc(
                currentPos, radius, displacement.normalized,
                hits, sweepDistance, hitMask, triggerInteraction);

            // Expand buffer if needed
            if (hitCount == hits.Length)
            {
                hits = new RaycastHit[hits.Length * 2];
                hitCount = Physics.SphereCastNonAlloc(
                    currentPos, radius, displacement.normalized,
                    hits, sweepDistance, hitMask, triggerInteraction);
            }

            // Find nearest accepted hit
            RaycastHit? nearestHit = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                var hit = hits[i];
                if (IsOwnerOrDescendant(hit.transform))
                    continue;
                if (CombatRaycastPolicy.ShouldSkip(hit.collider)) continue;

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestHit = hit;
                }
            }

            if (nearestHit.HasValue)
            {
                // Move to hit point
                transform.position = currentPos + displacement.normalized * nearestHit.Value.distance;
                _distanceTraveled += nearestHit.Value.distance;

                // Resolve contact
                _hasContacted = true;
                _runtime.ResolveProjectileContact(this, nearestHit.Value);
                return;
            }

            // No hit - move full displacement
            transform.position = currentPos + displacement;
            _distanceTraveled += sweepDistance;
            transform.rotation = Quaternion.LookRotation(_velocity.normalized);
        }

        private void PerformInitialOverlapCheck()
        {
            var ballistics = _shotRequest.Ballistics;
            float radius = ballistics.projectileSweepRadius > 0f ? ballistics.projectileSweepRadius : 0.01f;

            Collider[] overlaps = new Collider[32];
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, overlaps,
                ballistics.hitMask, ballistics.triggerInteraction);

            // Expand buffer if needed
            if (count == overlaps.Length)
            {
                overlaps = new Collider[overlaps.Length * 2];
                count = Physics.OverlapSphereNonAlloc(
                    transform.position, radius, overlaps,
                    ballistics.hitMask, ballistics.triggerInteraction);
            }

            // Find nearest accepted overlap
            Collider nearest = null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = overlaps[i];
                if (IsOwnerOrDescendant(col.transform))
                    continue;
                if (CombatRaycastPolicy.ShouldSkip(col)) continue;

                Vector3 closestPoint = col.ClosestPoint(transform.position);
                float dist = Vector3.Distance(transform.position, closestPoint);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = col;
                }
            }

            if (nearest != null)
            {
                _hasContacted = true;

                // Build a hit from the overlap
                Vector3 closestPoint = nearest.ClosestPoint(transform.position);
                Vector3 normal = (transform.position - closestPoint).normalized;
                if (normal == Vector3.zero)
                    normal = -_velocity.normalized;

                var hit = new RaycastHit(); // We can't fully populate this, but we pass the collider
                _runtime.ResolveProjectileOverlapContact(this, nearest, closestPoint, normal);
            }
        }

        private bool IsOwnerOrDescendant(Transform t)
        {
            if (t == null) return false;
            var owner = _shotRequest.OwnerRoot;
            if (owner == null) return false;

            Transform current = t;
            while (current != null)
            {
                if (current.gameObject == owner)
                    return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// Return this projectile to the pool, resetting all state.
        /// </summary>
        public void ReturnToPool()
        {
            if (_runtime == null || _prefabKey == null)
            {
                // Fallback: just deactivate
                gameObject.SetActive(false);
                return;
            }

            // Reset state
            _hasContacted = false;
            _velocity = Vector3.zero;
            _lifetime = 0f;
            _distanceTraveled = 0f;

            // Reset trail
            if (_trailRenderer != null)
            {
                _trailRenderer.Clear();
                _trailRenderer.emitting = false;
            }

            // Reset rigidbody
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            // Clear parent
            transform.SetParent(null);

            _runtime.ReturnProjectile(_prefabKey, this);
        }

        private void OnDestroy()
        {
            // If externally destroyed, notify runtime to remove from pool tracking
            if (_runtime != null && _prefabKey != null)
            {
                _runtime.OnProjectileDestroyed(_prefabKey, this);
            }
        }
    }
}
