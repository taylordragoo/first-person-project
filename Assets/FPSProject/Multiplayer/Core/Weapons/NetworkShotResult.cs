using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// One resolved impact endpoint from an accepted shot. The host produces these; every client
    /// plays tracers and impacts at these points. Damage is applied on the host only.
    /// </summary>
    public struct NetworkShotImpact : INetworkSerializable
    {
        /// <summary>World-space impact point.</summary>
        public Vector3 Point;

        /// <summary>World-space surface normal at the impact point.</summary>
        public Vector3 Normal;

        /// <summary>
        /// NetworkObject instance ID of the hit player, or 0 for environment hits. Used so
        /// clients can highlight hit markers without re-running damage logic.
        /// </summary>
        public ulong HitTargetNetworkId;

        /// <summary>True when this impact hit a player (for hit-marker presentation).</summary>
        public bool IsPlayerHit;

        /// <summary>
        /// Serialized <see cref="Combat.Runtime.ImpactSurfaceType"/> value used to select the
        /// same impact effect on every client without repeating a local physics query.
        /// </summary>
        public byte SurfaceType;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Point);
            serializer.SerializeValue(ref Normal);
            serializer.SerializeValue(ref HitTargetNetworkId);
            serializer.SerializeValue(ref IsPlayerHit);
            serializer.SerializeValue(ref SurfaceType);
        }
    }

    /// <summary>
    /// Host-to-all result of an accepted shot. The host resolves authoritative damage exactly
    /// once and broadcasts this so every client plays tracer/impact presentation. The fixed
    /// capacity is sized for the 8-pellet Herrington Police shotgun.
    /// </summary>
    public struct NetworkShotResult : INetworkSerializable
    {
        /// <summary>Stable catalog weapon ID that was fired.</summary>
        public ushort WeaponId;

        /// <summary>Shot sequence this result confirms. Owners use this to dedupe prediction.</summary>
        public uint ShotSequence;

        /// <summary>Client ID of the shooter.</summary>
        public ulong ShooterClientId;

        /// <summary>Tracer start (muzzle) position in world space.</summary>
        public Vector3 MuzzlePosition;

        /// <summary>Number of valid impacts in <see cref="Impacts"/>.</summary>
        public byte ImpactCount;

        /// <summary>
        /// Fixed-capacity impact collection. Sized for the 8-pellet shotgun plus a small margin
        /// for the typical hitscan single-ray case.
        /// </summary>
        public NetworkShotImpact Impact0;
        public NetworkShotImpact Impact1;
        public NetworkShotImpact Impact2;
        public NetworkShotImpact Impact3;
        public NetworkShotImpact Impact4;
        public NetworkShotImpact Impact5;
        public NetworkShotImpact Impact6;
        public NetworkShotImpact Impact7;

        /// <summary>
        /// The maximum number of impacts this struct can hold. Sized for the 8-pellet shotgun.
        /// </summary>
        public const int Capacity = 8;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref ShotSequence);
            serializer.SerializeValue(ref ShooterClientId);
            serializer.SerializeValue(ref MuzzlePosition);
            serializer.SerializeValue(ref ImpactCount);

            serializer.SerializeValue(ref Impact0);
            serializer.SerializeValue(ref Impact1);
            serializer.SerializeValue(ref Impact2);
            serializer.SerializeValue(ref Impact3);
            serializer.SerializeValue(ref Impact4);
            serializer.SerializeValue(ref Impact5);
            serializer.SerializeValue(ref Impact6);
            serializer.SerializeValue(ref Impact7);
        }
    }
}
