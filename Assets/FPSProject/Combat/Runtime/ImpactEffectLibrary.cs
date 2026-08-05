using System;
using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// A pair of effect prefabs for a surface type: a decal (bullet hole) and a
    /// transient impact (particle system). Either may be null.
    /// </summary>
    [Serializable]
    public struct SurfaceEffectPair
    {
        [Tooltip("Decal prefab (bullet hole) for this surface type.")]
        public GameObject decalPrefab;

        [Tooltip("Transient impact effect prefab (particles) for this surface type.")]
        public GameObject impactPrefab;
    }

    /// <summary>
    /// ScriptableObject containing a default decal/transient-impact pair and
    /// optional overrides for specific surface types. A missing override falls
    /// back to the default pair.
    /// </summary>
    [CreateAssetMenu(fileName = "ImpactEffectLibrary", menuName = "FPS Project/Impact Effect Library")]
    public class ImpactEffectLibrary : ScriptableObject
    {
        [Header("Default (fallback for all surfaces)")]
        public SurfaceEffectPair defaultPair;

        [Header("Surface Overrides")]
        public SurfaceEffectPair metalPair;
        public SurfaceEffectPair woodPair;
        public SurfaceEffectPair grassPair;
        public SurfaceEffectPair mudPair;
        public SurfaceEffectPair fleshPair;

        /// <summary>
        /// Returns the effect pair for the given surface type, falling back to
        /// the default pair when an override is not configured.
        /// </summary>
        public SurfaceEffectPair GetPair(ImpactSurfaceType surfaceType)
        {
            SurfaceEffectPair pair = surfaceType switch
            {
                ImpactSurfaceType.Metal => metalPair,
                ImpactSurfaceType.Wood => woodPair,
                ImpactSurfaceType.Grass => grassPair,
                ImpactSurfaceType.Mud => mudPair,
                ImpactSurfaceType.Flesh => fleshPair,
                _ => defaultPair
            };

            // Fall back to default if the override has no decal
            if (pair.decalPrefab == null && pair.impactPrefab == null)
                return defaultPair;

            // Use override decal if present, else default decal
            // Use override impact if present, else default impact
            return new SurfaceEffectPair
            {
                decalPrefab = pair.decalPrefab != null ? pair.decalPrefab : defaultPair.decalPrefab,
                impactPrefab = pair.impactPrefab != null ? pair.impactPrefab : defaultPair.impactPrefab
            };
        }
    }
}
