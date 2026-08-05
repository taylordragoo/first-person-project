using System.Collections.Generic;
using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Generic prefab-keyed object pool with lazy creation and automatic expansion.
    /// </summary>
    public class PrefabPool<T> where T : Component
    {
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly int _defaultCapacity;
        private readonly Stack<T> _available = new Stack<T>();
        private readonly List<T> _allInstances = new List<T>();

        public PrefabPool(GameObject prefab, Transform parent, int defaultCapacity)
        {
            _prefab = prefab;
            _parent = parent;
            _defaultCapacity = defaultCapacity;
        }

        /// <summary>
        /// Rent an instance from the pool, creating a new one if none are available.
        /// </summary>
        public T Rent()
        {
            T instance;
            if (_available.Count > 0)
            {
                instance = _available.Pop();
            }
            else
            {
                instance = CreateNew();
            }

            if (instance != null)
            {
                instance.gameObject.SetActive(true);
            }

            return instance;
        }

        /// <summary>
        /// Return an instance to the pool.
        /// </summary>
        public void Return(T instance)
        {
            if (instance == null) return;

            instance.gameObject.SetActive(false);
            instance.transform.SetParent(_parent);
            _available.Push(instance);
        }

        /// <summary>
        /// Pre-warm the pool to the default capacity.
        /// </summary>
        public void PreWarm()
        {
            int toCreate = _defaultCapacity - _allInstances.Count;
            for (int i = 0; i < toCreate; i++)
            {
                var instance = CreateNew();
                instance.gameObject.SetActive(false);
                instance.transform.SetParent(_parent);
                _available.Push(instance);
            }
        }

        /// <summary>
        /// Return all active instances to the pool.
        /// </summary>
        public void ReturnAll()
        {
            foreach (var instance in _allInstances)
            {
                if (instance != null && instance.gameObject.activeSelf)
                {
                    instance.gameObject.SetActive(false);
                    instance.transform.SetParent(_parent);
                    if (!_available.Contains(instance))
                        _available.Push(instance);
                }
            }
        }

        /// <summary>
        /// Destroy all pooled instances.
        /// </summary>
        public void Clear()
        {
            foreach (var instance in _allInstances)
            {
                if (instance != null)
                    Object.Destroy(instance.gameObject);
            }
            _allInstances.Clear();
            _available.Clear();
        }

        private T CreateNew()
        {
            if (_prefab == null) return null;

            var go = Object.Instantiate(_prefab, _parent);
            go.name = _prefab.name;
            var component = go.GetComponent<T>();
            if (component == null)
                component = go.AddComponent<T>();
            _allInstances.Add(component);
            return component;
        }
    }
}
