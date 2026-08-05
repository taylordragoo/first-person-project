using UnityEngine;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Deterministic pseudo-random number generator for host-side shot spread. The seed is
    /// derived from the shooter ID, weapon ID, and accepted shot sequence so every client that
    /// knows the seed reproduces the exact same spread pattern. This never mutates Unity's
    /// global <see cref="UnityEngine.Random"/> state.
    /// </summary>
    public static class DeterministicShotRandom
    {
        /// <summary>
        /// Build a 64-bit deterministic seed from the shooter ID, weapon ID, and shot sequence.
        /// The mix uses xorshift-style combining so small changes in any input produce a
        /// well-distributed seed.
        /// </summary>
        public static ulong BuildSeed(ulong shooterClientId, ushort weaponId, uint shotSequence)
        {
            ulong seed = shooterClientId;
            seed ^= ((ulong)weaponId << 32);
            seed ^= ((ulong)shotSequence << 16);
            seed ^= (ulong)weaponId;
            seed ^= (ulong)shotSequence;
            // Ensure non-zero seeds do not collide with the common all-zero case.
            if (seed == 0ul) seed = 0x9E3779B97F4A7C15ul;
            return seed;
        }

        /// <summary>
        /// Generate a deterministic unit-vector offset within a cone of the given half-angle
        /// (in degrees) around the <paramref name="baseDirection"/>. Uses a xorshift64 PRNG
        /// seeded from the shooter/weapon/sequence triple. Returns the spread-adjusted direction.
        /// </summary>
        public static Vector3 SpreadCone(
            ulong shooterClientId,
            ushort weaponId,
            uint shotSequence,
            int pelletIndex,
            Vector3 baseDirection,
            float halfAngleDegrees)
        {
            if (halfAngleDegrees <= 0f) return baseDirection.normalized;

            ulong seed = BuildSeed(shooterClientId, weaponId, shotSequence);
            // Mix the pellet index in so each pellet gets a different point in the cone.
            seed ^= ((ulong)pelletIndex * 0x9E3779B97F4A7C15ul);
            if (seed == 0ul) seed = 0x6A09E667F3BCC909ul;

            // xorshift64
            seed ^= seed << 13;
            seed ^= seed >> 7;
            seed ^= seed << 17;

            // Convert two 32-bit chunks to two floats in [0, 1).
            float u1 = (seed & 0xFFFFFFFFul) / (float)0x100000000ul;
            seed ^= seed << 13;
            seed ^= seed >> 7;
            seed ^= seed << 17;
            float u2 = (seed & 0xFFFFFFFFul) / (float)0x100000000ul;

            // Uniform point in a disk of radius tan(halfAngle) for an even cone distribution.
            float radius = Mathf.Sqrt(u1);
            float angle = u2 * Mathf.PI * 2f;

            float spreadRad = halfAngleDegrees * Mathf.Deg2Rad;
            float tanSpread = Mathf.Tan(spreadRad);
            float offsetX = radius * Mathf.Cos(angle) * tanSpread;
            float offsetY = radius * Mathf.Sin(angle) * tanSpread;

            return ApplyOffsetToDirection(baseDirection, offsetX, offsetY);
        }

        /// <summary>
        /// Apply a tangent-plane offset to a direction vector. Builds an orthonormal basis from
        /// the base direction and adds the offset, then renormalizes.
        /// </summary>
        private static Vector3 ApplyOffsetToDirection(Vector3 baseDir, float offsetX, float offsetY)
        {
            Vector3 forward = baseDir.normalized;
            Vector3 up = Mathf.Abs(forward.y) > 0.99f ? Vector3.right : Vector3.up;
            Vector3 right = Vector3.Cross(forward, up).normalized;
            Vector3 realUp = Vector3.Cross(right, forward).normalized;

            Vector3 offset = (right * offsetX) + (realUp * offsetY);
            return (forward + offset).normalized;
        }
    }
}