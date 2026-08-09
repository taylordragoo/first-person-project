using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Bootstrap
{
    /// <summary>
    /// Renders the scene while the multiplayer session is establishing and no player
    /// camera exists yet. Disables itself once the local player spawns so the player
    /// camera takes over. Prevents the editor "Display 1: no camera rendering" warning
    /// during the relay loading phase.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadingCamera : MonoBehaviour
    {
        [SerializeField] private Camera loadingCamera;

        private void Awake()
        {
            if (loadingCamera == null) loadingCamera = GetComponent<Camera>();
        }

        private void Update()
        {
            if (loadingCamera == null) return;
            if (!loadingCamera.enabled) return;

            if (NetworkManager.Singleton == null) return;
            if (!NetworkManager.Singleton.IsListening) return;

            if (NetworkManager.Singleton.LocalClient != null &&
                NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                loadingCamera.enabled = false;
            }
        }
    }
}
