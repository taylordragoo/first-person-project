using System.Collections.Generic;
using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Manages multiple PrefabPool instances keyed by prefab reference.
    /// </summary>
    public class PoolManager
    {
        private readonly Dictionary<GameObject, object> _pools = new Dictionary<GameObject, object>();
        private readonly Transform _parent;
        private readonly int _decalCapacity;
        private readonly int _impactCapacity;
        private readonly int _tracerCapacity;
        private readonly int _projectileCapacity;

        public PoolManager(Transform parent,
            int decalCapacity = 32,
            int impactCapacity = 16,
            int tracerCapacity = 16,
            int projectileCapacity = 16)
        {
            _parent = parent;
            _decalCapacity = decalCapacity;
            _impactCapacity = impactCapacity;
            _tracerCapacity = tracerCapacity;
            _projectileCapacity = projectileCapacity;
        }

        /// <summary>
        /// Rent a decal instance from the pool for the given prefab.
        /// </summary>
        public GameObject RentDecal(GameObject prefab)
        {
            return Rent(prefab, _decalCapacity);
        }

        /// <summary>
        /// Rent an impact effect instance from the pool for the given prefab.
        /// </summary>
        public GameObject RentImpact(GameObject prefab)
        {
            return Rent(prefab, _impactCapacity);
        }

        /// <summary>
        /// Rent a tracer instance from the pool for the given prefab.
        /// </summary>
        public GameObject RentTracer(GameObject prefab)
        {
            return Rent(prefab, _tracerCapacity);
        }

        /// <summary>
        /// Rent a projectile instance from the pool for the given prefab.
        /// </summary>
        public WeaponCombatProjectile RentProjectile(GameObject prefab)
        {
            if (prefab == null) return null;
            var pool = GetOrCreatePool<WeaponCombatProjectile>(prefab, _projectileCapacity);
            return pool.Rent();
        }

        /// <summary>
        /// Return a decal to its pool.
        /// </summary>
        public void ReturnDecal(GameObject prefab, GameObject instance)
        {
            Return(prefab, instance);
        }

        /// <summary>
        /// Return an impact effect to its pool.
        /// </summary>
        public void ReturnImpact(GameObject prefab, GameObject instance)
        {
            Return(prefab, instance);
        }

        /// <summary>
        /// Return a tracer to its pool.
        /// </summary>
        public void ReturnTracer(GameObject prefab, GameObject instance)
        {
            Return(prefab, instance);
        }

        /// <summary>
        /// Return a projectile to its pool.
        /// </summary>
        public void ReturnProjectile(GameObject prefab, WeaponCombatProjectile instance)
        {
            if (prefab == null || instance == null) return;
            var pool = GetOrCreatePool<WeaponCombatProjectile>(prefab, _projectileCapacity);
            pool.Return(instance);
        }

        /// <summary>
        /// Return all active instances to their pools.
        /// </summary>
        public void ReturnAll()
        {
            foreach (var kvp in _pools)
            {
                // Use reflection to call ReturnAll on the generic pool
                var poolType = kvp.Value.GetType();
                var method = poolType.GetMethod("ReturnAll");
                method?.Invoke(kvp.Value, null);
            }
        }

        /// <summary>
        /// Clear all pools and destroy instances.
        /// </summary>
        public void Clear()
        {
            foreach (var kvp in _pools)
            {
                var poolType = kvp.Value.GetType();
                var method = poolType.GetMethod("Clear");
                method?.Invoke(kvp.Value, null);
            }
            _pools.Clear();
        }

        private GameObject Rent(GameObject prefab, int capacity)
        {
            if (prefab == null) return null;
            var pool = GetOrCreatePool<Transform>(prefab, capacity);
            var instance = pool.Rent();
            return instance != null ? instance.gameObject : null;
        }

        private void Return(GameObject prefab, GameObject instance)
        {
            if (prefab == null || instance == null) return;
            var pool = GetOrCreatePool<Transform>(prefab, 0);
            var t = instance.GetComponent<Transform>();
            if (t != null) pool.Return(t);
        }

        private PrefabPool<T> GetOrCreatePool<T>(GameObject prefab, int capacity) where T : Component
        {
            if (!_pools.TryGetValue(prefab, out var existing))
            {
                var pool = new PrefabPool<T>(prefab, _parent, capacity);
                _pools[prefab] = pool;
                return pool;
            }
            return (PrefabPool<T>)existing;
        }
    }
}
