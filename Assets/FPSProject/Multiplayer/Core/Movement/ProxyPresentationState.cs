using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Movement
{
    /// <summary>
    /// Host-owned, everyone-readable transient locomotion state. Broadcast at <c>20 Hz</c> to
    /// remote proxies after the host accepts an <see cref="OwnerMotionSample"/>. Persistent
    /// weapon and life state use NetworkVariables; this struct only carries the locomotion
    /// fields needed to drive CAS animation and the Tactical presentation on remote clients.
    /// </summary>
    public struct ProxyPresentationState : INetworkSerializable
    {
        /// <summary>Host-accepted sequence. Remote proxies use this to discard stale snapshots.</summary>
        public uint Sequence;

        /// <summary>Host network tick at the time the accepted pose was captured.</summary>
        public int NetworkTick;

        public Vector3 Position;
        public Vector3 Velocity;

        public float BodyYaw;
        public float AimYaw;
        public float AimPitch;

        public float MoveX;
        public float MoveY;
        public float Gait;

        public bool IsGrounded;
        public bool IsInAir;
        public bool IsCrouching;
        public bool IsSprinting;
        public bool IsAiming;
        public bool IsMoving;

        /// <summary>Server-owned alive/dead flag so proxies stop locomotion on death.</summary>
        public bool IsAlive;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref NetworkTick);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref BodyYaw);
            serializer.SerializeValue(ref AimYaw);
            serializer.SerializeValue(ref AimPitch);
            serializer.SerializeValue(ref MoveX);
            serializer.SerializeValue(ref MoveY);
            serializer.SerializeValue(ref Gait);
            serializer.SerializeValue(ref IsGrounded);
            serializer.SerializeValue(ref IsInAir);
            serializer.SerializeValue(ref IsCrouching);
            serializer.SerializeValue(ref IsSprinting);
            serializer.SerializeValue(ref IsAiming);
            serializer.SerializeValue(ref IsMoving);
            serializer.SerializeValue(ref IsAlive);
        }
    }
}