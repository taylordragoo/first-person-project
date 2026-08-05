namespace FPSProject.Multiplayer.Core.Bootstrap
{
    /// <summary>
    /// Optional capability exposed by a session bootstrap that creates and joins
    /// player-facing session codes. Gameplay systems continue to depend only on
    /// <see cref="INetworkSessionBootstrap"/>.
    /// </summary>
    public interface IJoinCodeSessionBootstrap : INetworkSessionBootstrap
    {
        bool IsBusy { get; }
        string CurrentJoinCode { get; }
        string JoinCodeToJoin { get; set; }
        string LastError { get; }
    }
}
