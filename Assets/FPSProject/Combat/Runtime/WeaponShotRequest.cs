using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Optional player-level override for the visual muzzle used to originate
    /// shots. Hybrid presentation rigs can implement this when the rendered
    /// weapon is different from the gameplay weapon prop.
    /// </summary>
    public interface IWeaponMuzzleProvider
    {
        bool TryGetMuzzle(out Vector3 position, out Quaternion rotation);
    }

    /// <summary>
    /// Immutable per-shot input containing a snapshot of ballistics values,
    /// firing owner, weapon object, muzzle transform, and camera aim data.
    /// Pooled projectiles retain this snapshot even if the player changes weapons.
    /// </summary>
    public readonly struct WeaponShotRequest
    {
        public readonly WeaponBallisticsSettings Ballistics;
        public readonly GameObject OwnerRoot;
        public readonly GameObject WeaponObject;
        public readonly Vector3 MuzzlePosition;
        public readonly Quaternion MuzzleRotation;
        public readonly Vector3 CameraOrigin;
        public readonly Vector3 CameraDirection;

        public WeaponShotRequest(
            WeaponBallisticsSettings ballistics,
            GameObject ownerRoot,
            GameObject weaponObject,
            Vector3 muzzlePosition,
            Quaternion muzzleRotation,
            Vector3 cameraOrigin,
            Vector3 cameraDirection)
        {
            Ballistics = ballistics;
            OwnerRoot = ownerRoot;
            WeaponObject = weaponObject;
            MuzzlePosition = muzzlePosition;
            MuzzleRotation = muzzleRotation;
            CameraOrigin = cameraOrigin;
            CameraDirection = cameraDirection;
        }
    }
}
