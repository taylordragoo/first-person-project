namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Authoritative life state for a networked player. Host-written; every client reads it.
    /// </summary>
    public enum PlayerLifeState : byte
    {
        Alive = 0,
        Dead = 1,
        Respawning = 2
    }
}