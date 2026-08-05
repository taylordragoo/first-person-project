using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Weapons;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Offline shot router. Used by the non-networked CAS/Tactical player. Forwards the local
    /// <see cref="WeaponShotRequest"/> to the existing <see cref="WeaponCombatRuntime.SubmitShot"/>
    /// facade, which resolves damage and plays presentation locally. This preserves the existing
    /// offline behavior without any network code.
    /// </summary>
    public class OfflineWeaponShotRouter : MonoBehaviour, IWeaponShotRouter
    {
        private WeaponCombatRuntime _combatRuntime;

        private void Awake()
        {
            _combatRuntime = GetComponentInChildren<WeaponCombatRuntime>();
        }

        public void SubmitShot(
            in WeaponShotRequest request,
            ushort weaponId,
            uint shotSequence,
            int networkTick,
            float aimYaw,
            float aimPitch,
            bool isAiming)
        {
            if (_combatRuntime == null)
            {
                Debug.LogWarning($"[{nameof(OfflineWeaponShotRouter)}] WeaponCombatRuntime is null.");
                return;
            }

            _combatRuntime.SubmitShot(request);
        }
    }
}