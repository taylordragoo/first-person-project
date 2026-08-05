namespace FPSProject.Multiplayer.Core.Bootstrap
{
    /// <summary>
    /// Hides the connection mechanism behind a single contract so gameplay
    /// networking code never depends on whether the session is direct local,
    /// Relay-backed, or anything else. The bootstrap owns starting and stopping
    /// the <see cref="Unity.Netcode.NetworkManager"/>; gameplay code only reacts
    /// to spawn/despawn and replicated state.
    /// </summary>
    public interface INetworkSessionBootstrap
    {
        /// <summary>True while the bootstrap has started a session that is still running.</summary>
        bool IsStarted { get; }

        /// <summary>Start a host session (listen server). Returns true on success.</summary>
        bool StartHost();

        /// <summary>Start a client session connected to a host. Returns true on success.</summary>
        bool StartClient();

        /// <summary>Stop the current session (host or client).</summary>
        void Stop();
    }
}