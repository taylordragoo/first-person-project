namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Authoritative reload state for the currently-equipped weapon. Host-written;
    /// every client reads it so late joiners and proxies present the correct pose.
    /// </summary>
    public enum ReloadState : byte
    {
        None = 0,
        Reloading = 1,
        // Per-shell shotgun reload states are tracked by the Tactical presentation via
        // ReloadWeapon callbacks; the network state only needs to flag an in-progress reload.
    }
}