using System;
using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Serializable ballistics configuration stored on CAS WeaponSettings.
    /// Contains combat-enabled flag, shot type, damage, range, spread,
    /// tracer/projectile prefabs, and the shared impact effect library.
    /// </summary>
    [Serializable]
    public struct WeaponBallisticsSettings
    {
        [Header("Combat")]
        [Tooltip("When disabled, this weapon does not submit combat shots.")]
        public bool combatEnabled;

        [Tooltip("Hitscan or physical projectile.")]
        public WeaponShotType shotType;

        [Header("Damage")]
        [Min(0f)]
        [Tooltip("Damage per shot.")]
        public float damage;

        [Min(0f)]
        [Tooltip("Maximum range in meters.")]
        public float maxRange;

        [Tooltip("Layers that can be hit.")]
        public LayerMask hitMask;

        [Tooltip("How triggers are handled during queries. Default is Ignore.")]
        public QueryTriggerInteraction triggerInteraction;

        [Header("Spread")]
        [Range(0f, 45f)]
        [Tooltip("Maximum half-angle spread from the camera center ray in degrees. Zero disables spread.")]
        public float spreadDegrees;

        [Header("Tracer")]
        [Tooltip("Tracer prefab (pooled). Used for both hitscan and projectile weapons.")]
        public GameObject tracerPrefab;

        [Min(0f)]
        [Tooltip("Tracer travel speed in meters per second.")]
        public float tracerSpeed;

        [Min(0f)]
        [Tooltip("Tracer lifetime in seconds.")]
        public float tracerLifetime;

        [Header("Projectile")]
        [Tooltip("Projectile prefab (pooled). Only used when shotType is Projectile.")]
        public GameObject projectilePrefab;

        [Min(0f)]
        [Tooltip("Projectile speed in meters per second.")]
        public float projectileSpeed;

        [Min(0f)]
        [Tooltip("Projectile sweep radius for sphere cast.")]
        public float projectileSweepRadius;

        [Tooltip("When enabled, gravity affects the projectile.")]
        public bool projectileGravityEnabled;

        [Tooltip("Multiplier applied to Physics.gravity for the projectile.")]
        public float projectileGravityMultiplier;

        [Min(0f)]
        [Tooltip("Projectile lifetime in seconds.")]
        public float projectileLifetime;

        [Header("Effects")]
        [Tooltip("Shared impact effect library for decals and transient impacts.")]
        public ImpactEffectLibrary impactEffectLibrary;
    }
}
