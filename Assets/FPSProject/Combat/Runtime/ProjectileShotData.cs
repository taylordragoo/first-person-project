using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Helper component attached to pooled projectiles to retain the shot request
    /// snapshot. This allows the projectile to reconstruct damage/effect data
    /// on contact without referencing the original weapon.
    /// </summary>
    public class ProjectileShotData : MonoBehaviour
    {
        public WeaponShotRequest Request;
    }
}
