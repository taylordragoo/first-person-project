using System.Collections;
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
        [Tooltip("Default bootstrap used by H/C/X. The command line may select Services instead.")]
        [SerializeField] private MonoBehaviour bootstrapComponent;
        [SerializeField] private MonoBehaviour servicesBootstrapComponent;
        [SerializeField] private bool autoStartFromCommandLine = true;

        private void Awake()
        {
            string backend = SessionBootstrapUtility.GetCommandLineValue(
                System.Environment.GetCommandLineArgs(), "-fpsSessionBackend");
            if (string.Equals(backend, "services",
                System.StringComparison.OrdinalIgnoreCase))
            {
                bootstrap = servicesBootstrapComponent as INetworkSessionBootstrap;
            }

            if (bootstrap == null) bootstrap = bootstrapComponent as INetworkSessionBootstrap;
            if (bootstrap == null) bootstrap = GetComponent<INetworkSessionBootstrap>();

            if (bootstrap is IJoinCodeSessionBootstrap joinCodeBootstrap)
            {
                string joinCode = SessionBootstrapUtility.GetCommandLineValue(
                    System.Environment.GetCommandLineArgs(), "-fpsSessionCode");
                if (!string.IsNullOrWhiteSpace(joinCode))
                    joinCodeBootstrap.JoinCodeToJoin = joinCode;
            }
        }

        private IEnumerator Start()
        {
            if (!autoStartFromCommandLine || bootstrap == null) yield break;

            string[] args = System.Environment.GetCommandLineArgs();
            bool autoHost = SessionBootstrapUtility.HasCommandLineFlag(args, "-fpsAutoHost");
            bool autoClient = SessionBootstrapUtility.HasCommandLineFlag(args, "-fpsAutoClient");
            if (!autoHost && !autoClient) yield break;

            // Allow NetworkManager, transport adapters, and presentation objects to finish Awake.
            yield return null;
            if (autoHost) bootstrap.StartHost();
            else bootstrap.StartClient();
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
