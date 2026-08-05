using System;
using System.Collections.Generic;
using FPSProject.Combat.Runtime;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Network ballistics profile for one catalog weapon. The catalog is the single source of
    /// truth for authoritative damage, range, spread, masks, and impact/tracer assets. It is
    /// stored in the catalog (core assembly) so the host validation path can resolve shots
    /// without touching KINEMATION/Tactical presentation assets.
    /// </summary>
    [Serializable]
    public class NetworkWeaponBallistics
    {
        [Tooltip("Damage per hitscan ray or per shotgun pellet.")]
        [Min(0f)] public float damage = 25f;

        [Tooltip("Maximum range in meters.")]
        [Min(0f)] public float maxRange = 100f;

        [Tooltip("Layers that can be hit by this weapon (environment + players).")]
        public LayerMask hitMask;

        [Tooltip("How triggers are handled during raycasts.")]
        public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Spread (degrees, half-angle)")]
        [Tooltip("Hip-fire cone half-angle in degrees. Zero disables spread.")]
        [Range(0f, 45f)] public float hipSpreadDegrees = 1f;

        [Tooltip("ADS cone half-angle in degrees. Must be smaller than hip spread.")]
        [Range(0f, 45f)] public float adsSpreadDegrees = 0.1f;

        [Header("Tracer")]
        public GameObject tracerPrefab;
        [Min(0f)] public float tracerSpeed = 200f;
        [Min(0f)] public float tracerLifetime = 0.1f;

        [Header("Impact")]
        [Tooltip("Shared impact effect library for decals and transient impacts.")]
        public ImpactEffectLibrary impactEffectLibrary;
    }

    /// <summary>
    /// One entry in the <see cref="NetworkWeaponCatalog"/>. Maps a stable network ID to a
    /// presentation-side Tactical weapon prefab and the authoritative ballistics profile.
    /// </summary>
    [Serializable]
    public class NetworkWeaponEntry
    {
        [Tooltip("Stable network weapon ID. Never reuse or reorder IDs after release.")]
        [Range(1, ushort.MaxValue)] public ushort weaponId = 1;

        [Tooltip("Human-readable name for diagnostics.")]
        public string displayName = "TR15";

        [Tooltip("Tactical Shooter Pack weapon prefab used for presentation on every client.")]
        public GameObject tacticalWeaponPrefab;

        [Tooltip("Magazine capacity. The host enforces this; Tactical presentation reads it too.")]
        [Min(1)] public int magazineCapacity = 32;

        [Tooltip("Fire rate in rounds per minute. The host uses this for cadence validation.")]
        [Min(0f)] public float fireRateRpm = 600f;

        [Tooltip("Supported fire modes. At least Semi must be enabled.")]
        public bool supportsSemi = true;
        public bool supportsBurst = false;
        public bool supportsAuto = false;

        [Tooltip("Rounds fired per burst when the active fire mode is Burst.")]
        [Min(2)] public int burstRounds = 3;

        [Tooltip("True for the Herrington 11-87 Police: one shell fires multiple pellets.")]
        public bool isShotgun = false;

        [Tooltip("Number of pellets resolved per shell when isShotgun is true.")]
        [Min(1)] public int pelletCount = 8;

        [Tooltip("Network ballistics profile used for authoritative shot resolution on the host.")]
        public NetworkWeaponBallistics ballistics = new NetworkWeaponBallistics();

        [Tooltip("True when this entry has been populated. Used by OnValidate and tests.")]
        public bool configured = true;
    }

    /// <summary>
    /// Project-owned catalog mapping stable network weapon IDs to presentation assets and
    /// authoritative ballistics. The host uses this for cadence, ammunition, spread, and damage
    /// validation. Clients use it to map an accepted weapon ID to the correct Tactical weapon.
    /// </summary>
    [CreateAssetMenu(fileName = "NetworkWeaponCatalog",
        menuName = "FPSProject/Multiplayer/Network Weapon Catalog")]
    public class NetworkWeaponCatalog : ScriptableObject
    {
        [Tooltip("All weapons available in this milestone. IDs must be unique and stable.")]
        [SerializeField] private List<NetworkWeaponEntry> entries = new List<NetworkWeaponEntry>();

        /// <summary>Read-only access to the catalog entries.</summary>
        public IReadOnlyList<NetworkWeaponEntry> Entries => entries;

        /// <summary>Number of catalog entries.</summary>
        public int Count => entries.Count;

        /// <summary>
        /// Try to find an entry by its stable weapon ID. Returns true and outputs the entry
        /// when found. Logs a warning and returns false when the ID is unknown.
        /// </summary>
        public bool TryGetEntry(ushort weaponId, out NetworkWeaponEntry entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].weaponId == weaponId)
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>
        /// Index of the entry with the given weapon ID, or -1 when not found.
        /// </summary>
        public int IndexOf(ushort weaponId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].weaponId == weaponId) return i;
            }

            return -1;
        }

        /// <summary>True when the catalog contains an entry with the given ID.</summary>
        public bool Contains(ushort weaponId) => IndexOf(weaponId) >= 0;

        /// <summary>
        /// Add an entry to the catalog at runtime. Intended for test setup; production catalogs
        /// are authored as assets in the editor. Returns false if the entry's ID is already
        /// present (duplicate IDs are rejected).
        /// </summary>
        public bool AddEntry(NetworkWeaponEntry entry)
        {
            if (entry == null) return false;
            if (entry.weaponId == 0) return false;
            if (Contains(entry.weaponId)) return false;
            entries.Add(entry);
            return true;
        }

        /// <summary>
        /// Clear all entries. Intended for test setup only.
        /// </summary>
        public void ClearEntries()
        {
            entries.Clear();
        }

        private void OnValidate()
        {
            if (entries == null) return;

            var seenIds = new HashSet<ushort>();
            for (int i = 0; i < entries.Count; i++)
            {
                NetworkWeaponEntry e = entries[i];
                if (e == null) continue;

                if (e.weaponId == 0)
                {
                    Debug.LogWarning(
                        $"[NetworkWeaponCatalog] Entry {i} has weaponId 0; IDs must be >= 1.",
                        this);
                }
                else if (!seenIds.Add(e.weaponId))
                {
                    Debug.LogError(
                        $"[NetworkWeaponCatalog] Duplicate weaponId {e.weaponId} on entry {i} " +
                        "({e.displayName}). IDs must be unique.", this);
                }

                if (e.magazineCapacity < 1)
                {
                    Debug.LogWarning(
                        $"[NetworkWeaponCatalog] Entry {i} ({e.displayName}) magazineCapacity " +
                        "must be >= 1.", this);
                }

                if (!e.supportsSemi && !e.supportsBurst && !e.supportsAuto)
                {
                    Debug.LogWarning(
                        $"[NetworkWeaponCatalog] Entry {i} ({e.displayName}) has no supported " +
                        "fire modes; at least one must be enabled.", this);
                }

                if (e.supportsBurst && e.burstRounds < 2)
                {
                    Debug.LogWarning(
                        $"[NetworkWeaponCatalog] Entry {i} ({e.displayName}) supportsBurst is " +
                        "true but burstRounds < 2.", this);
                }

                if (e.isShotgun && e.pelletCount < 1)
                {
                    Debug.LogWarning(
                        $"[NetworkWeaponCatalog] Entry {i} ({e.displayName}) isShotgun but " +
                        "pelletCount < 1.", this);
                }

                if (e.ballistics != null && e.ballistics.adsSpreadDegrees > e.ballistics.hipSpreadDegrees)
                {
                    Debug.LogWarning(
                        $"[NetworkWeaponCatalog] Entry {i} ({e.displayName}) ADS spread " +
                        $"({e.ballistics.adsSpreadDegrees}) is larger than hip spread " +
                        $"({e.ballistics.hipSpreadDegrees}). ADS must be smaller.", this);
                }
            }
        }
    }
}