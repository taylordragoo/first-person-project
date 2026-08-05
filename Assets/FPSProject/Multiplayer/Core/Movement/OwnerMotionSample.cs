using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Movement
{
    /// <summary>
    /// Owner-to-host motion claim. Clients run the CAS motor every rendered frame and submit
    /// this sample at <c>20 Hz</c> using unreliable-sequenced delivery. The host validates,
    /// accepts or rejects, and broadcasts a <see cref="ProxyPresentationState"/> back. This
    /// struct never carries health, alive state, authoritative weapon state, or ammunition.
    /// </summary>
    public struct OwnerMotionSample : INetworkSerializable
    {
        /// <summary>Monotonically increasing per-owner sequence. Used to discard stale/out-of-order samples.</summary>
        public uint Sequence;

        /// <summary>Synchronized network tick reported by the owner at capture time.</summary>
        public int NetworkTick;

        public Vector3 Position;
        public Vector3 Velocity;

        /// <summary>Body yaw in degrees (transform.rotation.eulerAngles.y).</summary>
        public float BodyYaw;

        /// <summary>Aim yaw in degrees (horizontal look).</summary>
        public float AimYaw;

        /// <summary>Aim pitch in degrees (vertical look, clamped to [-90, 90]).</summary>
        public float AimPitch;

        /// <summary>Local move input X (strafe) at capture time, range [-1, 1].</summary>
        public float MoveX;

        /// <summary>Local move input Y (forward) at capture time, range [-1, 1].</summary>
        public float MoveY;

        /// <summary>Resolved CAS gait at capture time (0 = idle, ~1 = walk, ~2 = jog, ~3 = sprint).</summary>
        public float Gait;

        public bool IsGrounded;
        public bool IsInAir;
        public bool IsCrouching;
        public bool IsSprinting;
        public bool IsAiming;
        public bool IsMoving;

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
        }
    }
}