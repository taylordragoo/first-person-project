using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Owner-to-host shot request. The owner sends this when the local CAS/Tactical fire path
    /// fires; the host validates it against the catalog and the accepted player pose, then
    /// resolves authoritative damage. This struct never carries ScriptableObjects, prefab
    /// references, GameObjects, damage values, ammunition, or client-selected hit results.
    /// </summary>
    public struct NetworkShotCommand : INetworkSerializable
    {
        /// <summary>Stable catalog weapon ID the owner claims to have fired.</summary>
        public ushort WeaponId;

        /// <summary>Monotonically increasing per-owner shot sequence. Used to reject stale/duplicate shots.</summary>
        public uint ShotSequence;

        /// <summary>Synchronized network tick reported by the owner at fire time.</summary>
        public int NetworkTick;

        /// <summary>Aim yaw in degrees at fire time (horizontal look).</summary>
        public float AimYaw;

        /// <summary>Aim pitch in degrees at fire time (vertical look, clamped to [-90, 90]).</summary>
        public float AimPitch;

        /// <summary>
        /// Normalized aim direction at fire time, derived from the owner's camera. The host
        /// validates this against the accepted player aim within a configured tolerance.
        /// </summary>
        public Vector3 AimDirection;

        /// <summary>True when the owner was aiming down sights at fire time.</summary>
        public bool IsAiming;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref ShotSequence);
            serializer.SerializeValue(ref NetworkTick);
            serializer.SerializeValue(ref AimYaw);
            serializer.SerializeValue(ref AimPitch);
            serializer.SerializeValue(ref AimDirection);
            serializer.SerializeValue(ref IsAiming);
        }
    }
}