using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Optional component that selects a surface type for a GameObject.
    /// Resolve the collider first, then the closest parent.
    /// If none exists, Default is used.
    /// </summary>
    public class ImpactSurface : MonoBehaviour
    {
        [SerializeField]
        private ImpactSurfaceType _surfaceType = ImpactSurfaceType.Default;

        public ImpactSurfaceType SurfaceType => _surfaceType;

        /// <summary>
        /// Resolves the surface type from a collider: checks the collider's GameObject
        /// first, then walks up to the closest parent with an ImpactSurface component.
        /// Returns Default if none is found.
        /// </summary>
        public static ImpactSurfaceType Resolve(Collider collider)
        {
            if (collider == null) return ImpactSurfaceType.Default;

            // Check the collider's own GameObject first
            var surface = collider.GetComponent<ImpactSurface>();
            if (surface != null) return surface.SurfaceType;

            // Walk up to closest parent
            surface = collider.GetComponentInParent<ImpactSurface>();
            if (surface != null) return surface.SurfaceType;

            return ImpactSurfaceType.Default;
        }
    }
}
