using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Damage payload delivered to IDamageable receivers.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;
        public readonly Vector3 TravelDirection;
        public readonly GameObject InstigatorOwner;
        public readonly GameObject SourceWeapon;

        public DamageInfo(
            float amount,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 travelDirection,
            GameObject instigatorOwner,
            GameObject sourceWeapon)
        {
            Amount = amount;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            TravelDirection = travelDirection;
            InstigatorOwner = instigatorOwner;
            SourceWeapon = sourceWeapon;
        }
    }
}
