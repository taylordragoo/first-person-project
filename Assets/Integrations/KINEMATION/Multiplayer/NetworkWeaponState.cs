using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using FPSProject.Multiplayer.Core.Weapons;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Host-authoritative persistent weapon and life state for a networked player. Server-written,
    /// everyone-readable. Applied during <see cref="NetworkCasPlayer.OnNetworkSpawn"/> before
    /// subscribing to change callbacks so late joiners immediately see the correct weapon,
    /// ammunition, mode, reload state, and life state.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkWeaponState : NetworkBehaviour
    {
        [SerializeField] private NetworkWeaponCatalog catalog;

        // ─────────────────────────────────────────────────────────────────────────────
        // Host-owned persistent state. Every client reads these.
        // ─────────────────────────────────────────────────────────────────────────────

        public NetworkVariable<ushort> EquippedWeaponId = new NetworkVariable<ushort>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<FireMode> ActiveFireMode = new NetworkVariable<FireMode>(
            FireMode.Semi, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<ReloadState> ActiveReloadState = new NetworkVariable<ReloadState>(
            ReloadState.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<PlayerLifeState> LifeState = new NetworkVariable<PlayerLifeState>(
            PlayerLifeState.Alive, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // One entry per catalog weapon, in catalog order. Host-owned.
        public NetworkList<WeaponAmmoState> AmmoState;

        // Per-weapon fire-mode tracking on the host. Not networked; used for cadence validation.
        private readonly Dictionary<ushort, FireMode> _perWeaponFireMode = new Dictionary<ushort, FireMode>();

        // Per-weapon last-shot timestamp on the host. Not networked.
        private readonly Dictionary<ushort, float> _perWeaponLastShotTime = new Dictionary<ushort, float>();

        public NetworkWeaponCatalog Catalog => catalog;
        public bool IsCatalogValid => catalog != null;

        public NetworkWeaponState()
        {
            AmmoState = new NetworkList<WeaponAmmoState>();
        }

        public override void OnNetworkSpawn()
        {
            if (catalog == null)
            {
                catalog = ResolveCatalog();
            }

            if (IsServer)
            {
                InitializeServerState();
            }

            // Apply current values to local presentation before subscribing to change callbacks
            // so late joiners see the correct persistent state immediately.
            ApplyCurrentStateToPresentation(initial: true);

            EquippedWeaponId.OnValueChanged += OnEquippedWeaponChanged;
            ActiveFireMode.OnValueChanged += OnFireModeChanged;
            ActiveReloadState.OnValueChanged += OnReloadStateChanged;
            LifeState.OnValueChanged += OnLifeStateChanged;
            AmmoState.OnListChanged += OnAmmoListChanged;
        }

        public override void OnNetworkDespawn()
        {
            EquippedWeaponId.OnValueChanged -= OnEquippedWeaponChanged;
            ActiveFireMode.OnValueChanged -= OnFireModeChanged;
            ActiveReloadState.OnValueChanged -= OnReloadStateChanged;
            LifeState.OnValueChanged -= OnLifeStateChanged;
            AmmoState.OnListChanged -= OnAmmoListChanged;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Host-side authoritative writes. Called by the shot router / weapon request path.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Host-only: set the equipped weapon ID. Validates the ID against the catalog.
        /// </summary>
        public void ServerSetEquippedWeapon(ushort weaponId)
        {
            if (!IsServer) return;
            if (catalog == null || !catalog.Contains(weaponId)) return;
            if (EquippedWeaponId.Value == weaponId) return;
            EquippedWeaponId.Value = weaponId;
        }

        /// <summary>Host-only: set the active fire mode for the equipped weapon.</summary>
        public void ServerSetFireMode(FireMode mode)
        {
            if (!IsServer) return;
            ActiveFireMode.Value = mode;
            if (EquippedWeaponId.Value != 0)
                _perWeaponFireMode[EquippedWeaponId.Value] = mode;
        }

        /// <summary>Host-only: set the reload state.</summary>
        public void ServerSetReloadState(ReloadState state)
        {
            if (!IsServer) return;
            ActiveReloadState.Value = state;
        }

        /// <summary>Host-only: set the life state.</summary>
        public void ServerSetLifeState(PlayerLifeState state)
        {
            if (!IsServer) return;
            LifeState.Value = state;
        }

        /// <summary>
        /// Host-only: decrement ammunition for the equipped weapon by one shot. Returns true if
        /// ammunition was available and decremented; false if the magazine was empty.
        /// </summary>
        public bool ServerDecrementAmmo()
        {
            if (!IsServer || catalog == null) return false;
            int idx = catalog.IndexOf(EquippedWeaponId.Value);
            if (idx < 0 || idx >= AmmoState.Count) return false;

            var entry = AmmoState[idx];
            if (entry.CurrentAmmo == 0) return false;
            entry.CurrentAmmo -= 1;
            AmmoState[idx] = entry;
            return true;
        }

        /// <summary>
        /// Host-only: refill the magazine for a specific weapon ID to its capacity.
        /// </summary>
        public void ServerRefillAmmo(ushort weaponId)
        {
            if (!IsServer || catalog == null) return;
            int idx = catalog.IndexOf(weaponId);
            if (idx < 0 || idx >= AmmoState.Count) return;
            var entry = AmmoState[idx];
            entry.CurrentAmmo = entry.Capacity;
            AmmoState[idx] = entry;
        }

        /// <summary>Host-only: refill all weapons' magazines to capacity (used on respawn).</summary>
        public void ServerRefillAllAmmo()
        {
            if (!IsServer || catalog == null) return;
            for (int i = 0; i < catalog.Count && i < AmmoState.Count; i++)
            {
                var entry = AmmoState[i];
                entry.CurrentAmmo = entry.Capacity;
                AmmoState[i] = entry;
            }
        }

        /// <summary>
        /// Host-only: record the last-shot time for the equipped weapon. Used for cadence
        /// validation. Returns true if cadence has elapsed and the shot is permitted.
        /// </summary>
        public bool ServerCheckAndRecordCadence(ushort weaponId, float fireRateRpm, float serverTime)
        {
            if (!IsServer) return false;
            if (fireRateRpm <= 0f) return true;
            float minInterval = 60f / fireRateRpm;
            if (_perWeaponLastShotTime.TryGetValue(weaponId, out float last))
            {
                if (serverTime - last < minInterval) return false;
            }
            _perWeaponLastShotTime[weaponId] = serverTime;
            return true;
        }

        /// <summary>
        /// Host-only: reset all state for a respawn. Refills ammo, sets equipped weapon to ID 1
        /// (unless overridden), resets fire mode and reload state, and clears cadence history.
        /// </summary>
        public void ServerResetForRespawn(ushort defaultWeaponId = 1)
        {
            if (!IsServer) return;
            ServerRefillAllAmmo();
            _perWeaponLastShotTime.Clear();
            _perWeaponFireMode.Clear();
            if (catalog != null && catalog.Contains(defaultWeaponId))
            {
                EquippedWeaponId.Value = defaultWeaponId;
            }
            ActiveFireMode.Value = FireMode.Semi;
            ActiveReloadState.Value = ReloadState.None;
            LifeState.Value = PlayerLifeState.Alive;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Server initialization.
        // ─────────────────────────────────────────────────────────────────────────────

        private void InitializeServerState()
        {
            if (catalog == null) return;

            // Build the ammo list in catalog order. The NetworkList must be sized to match.
            while (AmmoState.Count < catalog.Count)
            {
                AmmoState.Add(new WeaponAmmoState { WeaponId = 0, CurrentAmmo = 0, Capacity = 0 });
            }
            while (AmmoState.Count > catalog.Count)
            {
                AmmoState.RemoveAt(AmmoState.Count - 1);
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                var entry = catalog.Entries[i];
                AmmoState[i] = new WeaponAmmoState
                {
                    WeaponId = entry.weaponId,
                    CurrentAmmo = (ushort)entry.magazineCapacity,
                    Capacity = (ushort)entry.magazineCapacity
                };
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Client-side presentation application. Override or hook these from the adapter.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Apply the current NetworkVariable values to local presentation. Called on spawn
        /// (initial=true) and on each change. The default implementation is a no-op; the
        /// adapter layer is responsible for wiring ID-based presentation.
        /// </summary>
        protected virtual void ApplyCurrentStateToPresentation(bool initial)
        {
            // Default no-op. The NetworkCasPlayer adapter hooks into these callbacks to drive
            // the Tactical presentation via NetworkTacticalShooterPlayer.ApplyEquippedWeapon and
            // INetworkTacticalWeaponPresentation.
        }

        protected virtual void OnEquippedWeaponChanged(ushort previous, ushort current) { }
        protected virtual void OnFireModeChanged(FireMode previous, FireMode current) { }
        protected virtual void OnReloadStateChanged(ReloadState previous, ReloadState current) { }
        protected virtual void OnLifeStateChanged(PlayerLifeState previous, PlayerLifeState current) { }
        protected virtual void OnAmmoListChanged(NetworkListEvent<WeaponAmmoState> changeEvent) { }

        // ─────────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────────

        private static NetworkWeaponCatalog ResolveCatalog()
        {
            var assets = UnityEngine.Resources.LoadAll<NetworkWeaponCatalog>("");
            return assets != null && assets.Length > 0 ? assets[0] : null;
        }

        /// <summary>Current ammunition for the equipped weapon, or 0 if not initialized.</summary>
        public ushort GetEquippedAmmo()
        {
            if (catalog == null) return 0;
            int idx = catalog.IndexOf(EquippedWeaponId.Value);
            if (idx < 0 || idx >= AmmoState.Count) return 0;
            return AmmoState[idx].CurrentAmmo;
        }

        /// <summary>Capacity for the equipped weapon, or 0 if not initialized.</summary>
        public ushort GetEquippedCapacity()
        {
            if (catalog == null) return 0;
            int idx = catalog.IndexOf(EquippedWeaponId.Value);
            if (idx < 0 || idx >= AmmoState.Count) return 0;
            return AmmoState[idx].Capacity;
        }

        /// <summary>Current ammunition for a specific weapon ID, or 0 if unknown.</summary>
        public ushort GetAmmoFor(ushort weaponId)
        {
            if (catalog == null) return 0;
            int idx = catalog.IndexOf(weaponId);
            if (idx < 0 || idx >= AmmoState.Count) return 0;
            return AmmoState[idx].CurrentAmmo;
        }
    }
}