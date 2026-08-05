using System.Collections.Generic;
using CAS_Demo.Scripts.FPS;
using FPSProject.Multiplayer.Core.Weapons;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Networked Tactical Shooter Player. Derives from the vendor <see cref="TacticalShooterPlayer"/>
    /// so the existing presentation path is preserved, but disables vendor input handlers and
    /// exposes presentation-only methods driven by host-authoritative state. Weapon selection is
    /// ID-based through <see cref="ApplyEquippedWeapon(ushort)"/>; the vendor next/previous weapon
    /// callbacks are never invoked by the network path.
    /// </summary>
    public class NetworkTacticalShooterPlayer : TacticalShooterPlayer
    {
        private NetworkWeaponCatalog _catalog;
        private readonly Dictionary<ushort, int> _weaponIdToTacticalIndex = new Dictionary<ushort, int>();
        private bool _initialized;

        public bool IsNetworkInitialized => _initialized;

        /// <summary>
        /// Initialize the catalog mapping. Called by <see cref="NetworkCasPlayer"/> after the
        /// vendor Start path has populated the weapon list. Builds a lookup from stable catalog
        /// weapon IDs to the Tactical weapon array indices that the vendor Start created.
        /// </summary>
        public void InitializeNetwork(NetworkWeaponCatalog catalog)
        {
            _catalog = catalog;
            _weaponIdToTacticalIndex.Clear();
            if (_catalog == null) return;

            // The vendor Start instantiates weaponPrefabs in order. We map each catalog entry
            // to the index of the matching prefab in the vendor's weaponPrefabs array.
            // _weapons is protected on the base class; weaponPrefabs is the serialized source.
            for (int i = 0; i < weaponPrefabs.Length; i++)
            {
                GameObject prefab = weaponPrefabs[i];
                if (prefab == null) continue;
                for (int e = 0; e < _catalog.Count; e++)
                {
                    if (_catalog.Entries[e].tacticalWeaponPrefab == prefab
                        && !_weaponIdToTacticalIndex.ContainsKey(_catalog.Entries[e].weaponId))
                    {
                        _weaponIdToTacticalIndex.Add(_catalog.Entries[e].weaponId, i);
                    }
                }
            }

            _initialized = true;
        }

        /// <summary>
        /// Equip the Tactical weapon mapped to <paramref name="weaponId"/>. The host validates
        /// and writes the authoritative equipped ID; every client applies that accepted ID here.
        /// Never call the vendor next/previous weapon callbacks from the network path.
        /// </summary>
        public void ApplyEquippedWeapon(ushort weaponId)
        {
            if (!_initialized || _catalog == null) return;

            if (!_weaponIdToTacticalIndex.TryGetValue(weaponId, out int tacticalIndex))
            {
                Debug.LogWarning(
                    $"[{nameof(NetworkTacticalShooterPlayer)}] Weapon ID {weaponId} is not " +
                    "mapped to any Tactical weapon prefab in this player's inventory.", this);
                return;
            }

            EquipWeaponByIndex(tacticalIndex);
        }

        /// <summary>
        /// Equip a Tactical weapon by its vendor array index, bypassing the next/previous
        /// cycling logic. Used by <see cref="ApplyEquippedWeapon"/> and the respawn reset.
        /// </summary>
        private void EquipWeaponByIndex(int index)
        {
            // _weapons and _activeWeaponIndex are protected on the base class.
            if (_weapons == null || index < 0 || index >= _weapons.Count) return;
            if (_activeWeaponIndex == index) return;

            GetActiveWeapon().HideWeapon();
            _activeWeaponIndex = index;
            EquipWeapon(false);
        }

        /// <summary>Index of the currently-equipped Tactical weapon, or -1 if not initialized.</summary>
        public int CurrentTacticalWeaponIndex => _weapons == null ? -1 : _activeWeaponIndex;

        /// <summary>Return the presentation interface for the currently-equipped weapon, or null.</summary>
        public INetworkTacticalWeaponPresentation GetActiveNetworkWeaponPresentation()
        {
            if (_weapons == null || _activeWeaponIndex < 0 || _activeWeaponIndex >= _weapons.Count)
                return null;
            return _weapons[_activeWeaponIndex] as INetworkTacticalWeaponPresentation;
        }

        /// <summary>Return the presentation interface for the weapon at the given index, or null.</summary>
        public INetworkTacticalWeaponPresentation GetNetworkWeaponPresentation(int index)
        {
            if (_weapons == null || index < 0 || index >= _weapons.Count) return null;
            return _weapons[index] as INetworkTacticalWeaponPresentation;
        }
    }
}