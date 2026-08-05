using FPSProject.Combat.Runtime;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Routes a local combat shot request from <c>WeaponProp.SubmitCombatShot()</c> to the
    /// correct resolution path depending on the current network role:
    /// <list type="bullet">
    /// <item><description>Offline player: <c>OfflineWeaponShotRouter</c> forwards to the existing
    /// local <see cref="WeaponCombatRuntime"/>.</description></item>
    /// <item><description>Network owner: <c>NetworkWeaponShotRouter</c> converts the local
    /// request into a <see cref="NetworkShotCommand"/> and sends it to the host. It never
    /// applies local damage.</description></item>
    /// <item><description>Host: validates the command, reconstructs a trusted
    /// <see cref="WeaponShotRequest"/> from the catalog and accepted player pose, and invokes
    /// authoritative resolution exactly once.</description></item>
    /// <item><description>Remote proxy: cannot submit shots.</description></item>
    /// </list>
    /// Implementations live on the player root alongside <see cref="WeaponCombatRuntime"/>.
    /// </summary>
    public interface IWeaponShotRouter
    {
        /// <summary>
        /// Route a local shot request. Called by <c>WeaponProp.SubmitCombatShot()</c> after it
        /// has built the <see cref="WeaponShotRequest"/> from the local camera and muzzle.
        /// </summary>
        /// <param name="request">The local shot request built from the owner's camera/muzzle.</param>
        /// <param name="weaponId">Stable catalog weapon ID the owner claims to have fired.</param>
        /// <param name="shotSequence">Monotonic per-owner shot sequence number for deduplication.</param>
        /// <param name="networkTick">Synchronized network tick at fire time.</param>
        /// <param name="aimYaw">Aim yaw in degrees at fire time.</param>
        /// <param name="aimPitch">Aim pitch in degrees at fire time.</param>
        /// <param name="isAiming">True when the owner was aiming down sights.</param>
        void SubmitShot(
            in WeaponShotRequest request,
            ushort weaponId,
            uint shotSequence,
            int networkTick,
            float aimYaw,
            float aimPitch,
            bool isAiming);
    }
}