using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Bootstrap
{
    /// <summary>
    /// Starts a local/direct host or client connection using Unity Transport on
    /// the configured address/port. No Sessions, Relay, or authentication. This
    /// is the bootstrap used to prove the gameplay vertical slice before any
    /// Unity Services integration is added.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DirectNetworkSessionBootstrap : MonoBehaviour, INetworkSessionBootstrap
    {
        [Header("Transport")]
        [SerializeField] private string connectAddress = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        [Header("References")]
        [Tooltip("The NetworkManager to drive. Resolved on the same GameObject if not assigned.")]
        [SerializeField] private NetworkManager networkManager;

        public bool IsStarted => networkManager != null && networkManager.IsListening;

        private void Awake()
        {
            if (networkManager == null) networkManager = GetComponent<NetworkManager>();
        }

        public bool StartHost()
        {
            if (!ResolveAndConfigure()) return false;
            networkManager.StartHost();
            return networkManager.IsListening;
        }

        public bool StartClient()
        {
            if (!ResolveAndConfigure()) return false;
            networkManager.StartClient();
            return networkManager.IsListening;
        }

        public void Stop()
        {
            if (networkManager == null) return;
            networkManager.Shutdown();
        }

        private bool ResolveAndConfigure()
        {
            if (networkManager == null) networkManager = GetComponent<NetworkManager>();
            if (networkManager == null)
            {
                Debug.LogError($"[{nameof(DirectNetworkSessionBootstrap)}] No NetworkManager found.", this);
                return false;
            }

            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError($"[{nameof(DirectNetworkSessionBootstrap)}] No UnityTransport on NetworkManager.", this);
                return false;
            }

            transport.SetConnectionData(connectAddress, port);
            return true;
        }
    }
}