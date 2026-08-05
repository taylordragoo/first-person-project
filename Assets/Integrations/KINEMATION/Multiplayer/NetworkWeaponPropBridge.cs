using CAS_Demo.Scripts.FPS;
using FPSProject.Multiplayer.Core.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Wires the hidden CAS <see cref="WeaponProp"/> on the owner's local rig to the network
    /// shot router. The CAS weapon prop remains the local presentation and input source; this
    /// bridge updates its <c>networkWeaponId</c>, <c>networkTick</c>, and <c>isAiming</c> every
    /// frame and points its <c>SubmitCombatShot</c> at the <see cref="IWeaponShotRouter"/> so
    /// the owner never applies local damage and the host resolves authoritatively.
    /// </summary>
    public class NetworkWeaponPropBridge : NetworkBehaviour
    {
        private NetworkCasPlayer _networkCasPlayer;
        private IWeaponShotRouter _shotRouter;
        private WeaponProp[] _weaponProps;
        private bool _initialized;

        private void Awake()
        {
            _networkCasPlayer = GetComponent<NetworkCasPlayer>();
            _shotRouter = GetComponent<IWeaponShotRouter>();
        }

        public override void OnNetworkSpawn()
        {
            if (_networkCasPlayer == null) _networkCasPlayer = GetComponent<NetworkCasPlayer>();
            if (_shotRouter == null) _shotRouter = GetComponent<IWeaponShotRouter>();

            // Only the owner needs the weapon prop bridge. Proxies do not fire locally.
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            // Cache the CAS weapon props on the hidden source rig. They are children of the
            // root, activated by the base controller when equipped.
            _weaponProps = GetComponentsInChildren<WeaponProp>(true);
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized || _shotRouter == null || _networkCasPlayer == null) return;

            int tick = NetworkManager != null ? (int)NetworkManager.LocalTime.Tick : 0;
            bool isAiming = _networkCasPlayer.Controller != null && _networkCasPlayer.Controller.IsAiming;
            ushort equippedId = _networkCasPlayer.WeaponState != null
                ? _networkCasPlayer.WeaponState.EquippedWeaponId.Value
                : (ushort)0;

            // Push the current authoritative equipped weapon ID, tick, and aim flag into every
            // CAS weapon prop so SubmitCombatShot carries the correct values to the router.
            for (int i = 0; i < _weaponProps.Length; i++)
            {
                var prop = _weaponProps[i];
                if (prop == null) continue;
                prop.SetNetworkTick(tick);
                prop.SetNetworkAiming(isAiming);
                prop.SetNetworkWeaponId(equippedId);
                prop.SetShotRouter(_shotRouter);
            }
        }

        public override void OnNetworkDespawn()
        {
            // Clear the router so the weapon props fall back to local resolution if the prefab
            // is reused offline (e.g. after the network session ends).
            if (_weaponProps != null)
            {
                foreach (var prop in _weaponProps)
                {
                    if (prop != null) prop.SetShotRouter(null);
                }
            }
            _initialized = false;
        }
    }
}