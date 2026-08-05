using System;
using Unity.Netcode;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Per-weapon ammunition state for one catalog entry. The host owns this; every client reads it.
    /// One entry exists per catalog weapon, regardless of whether it is currently equipped, so
    /// late joiners see the correct ammunition for every weapon in the player's inventory.
    /// </summary>
    public struct WeaponAmmoState : INetworkSerializable, IEquatable<WeaponAmmoState>
    {
        /// <summary>Stable catalog ID this entry refers to.</summary>
        public ushort WeaponId;

        /// <summary>Shells currently in the magazine (host-authoritative).</summary>
        public ushort CurrentAmmo;

        /// <summary>Magazine capacity for this weapon (mirrors the catalog entry for convenience).</summary>
        public ushort Capacity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref CurrentAmmo);
            serializer.SerializeValue(ref Capacity);
        }

        public bool Equals(WeaponAmmoState other)
        {
            return WeaponId == other.WeaponId
                && CurrentAmmo == other.CurrentAmmo
                && Capacity == other.Capacity;
        }

        public override bool Equals(object obj) => obj is WeaponAmmoState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WeaponId.GetHashCode();
                hash = (hash * 397) ^ CurrentAmmo.GetHashCode();
                hash = (hash * 397) ^ Capacity.GetHashCode();
                return hash;
            }
        }
    }
}