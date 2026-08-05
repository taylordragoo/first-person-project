using UnityEngine;

namespace FPSProject.Multiplayer.Core.Bootstrap
{
    /// <summary>
    /// Minimal development launcher that drives an <see cref="INetworkSessionBootstrap"/>
    /// from keyboard so the vertical slice can be exercised without UI. Host: H,
    /// Client: C, Stop: X. This is development-only and not part of gameplay code.
    /// </summary>
    [DisallowMultipleComponent]
    public class DevSessionLauncher : MonoBehaviour
    {
        [SerializeField] private INetworkSessionBootstrap bootstrap;
        [Tooltip("Optional NetworkManager to keep listening-state output accurate.")]
        [SerializeField] private MonoBehaviour bootstrapComponent;

        private void Awake()
        {
            if (bootstrap == null) bootstrap = bootstrapComponent as INetworkSessionBootstrap;
            if (bootstrap == null) bootstrap = GetComponent<INetworkSessionBootstrap>();
        }

        private void Update()
        {
            if (bootstrap == null) return;

            if (Input.GetKeyDown(KeyCode.H)) bootstrap.StartHost();
            else if (Input.GetKeyDown(KeyCode.C)) bootstrap.StartClient();
            else if (Input.GetKeyDown(KeyCode.X)) bootstrap.Stop();
        }
    }
}