using System;
using System.Collections;
using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPSProject.Multiplayer.Core.Bootstrap
{
    internal enum MultiplayerLaunchMode
    {
        None,
        Host,
        Client
    }

    /// <summary>
    /// Entry point used by the main-menu UI. It records the requested Relay role,
    /// loads the multiplayer operation, and exposes a small JSON snapshot to OneJS.
    /// </summary>
    public static class MultiplayerMenuBridge
    {
        public const string MultiplayerSceneName = "OperationsDemoMultiplayer";
        public const string MainMenuSceneName = "MainMenu";

        private static MultiplayerLaunchMode _pendingMode;
        private static string _pendingJoinCode = string.Empty;
        private static string _lastError = string.Empty;

        public static string LastError => _lastError;

        public static bool LaunchHost()
        {
            return PrepareLaunch(MultiplayerLaunchMode.Host, string.Empty);
        }

        public static bool LaunchClient(string joinCode)
        {
            string normalizedCode = SessionBootstrapUtility.NormalizeJoinCode(joinCode);
            if (string.IsNullOrEmpty(normalizedCode))
            {
                _lastError = "Enter a session code before joining.";
                return false;
            }

            return PrepareLaunch(MultiplayerLaunchMode.Client, normalizedCode);
        }

        public static string ReadSessionSnapshot()
        {
            MultiplayerSceneLauncher launcher = MultiplayerSceneLauncher.Instance;
            if (launcher != null) return launcher.ReadSnapshot();

            bool sceneIsLoading = SceneManager.GetActiveScene().name == MultiplayerSceneName;
            var snapshot = new MultiplayerSessionSnapshot
            {
                active = sceneIsLoading,
                state = sceneIsLoading ? "LOADING" : "OFFLINE",
                joinCode = string.Empty,
                error = _lastError,
                players = 0,
                host = false
            };
            return JsonUtility.ToJson(snapshot);
        }

        public static bool CopyJoinCode()
        {
            string code = MultiplayerSceneLauncher.Instance?.CurrentJoinCode;
            if (string.IsNullOrEmpty(code)) return false;

            GUIUtility.systemCopyBuffer = code;
            return true;
        }

        public static void ReturnToMainMenu()
        {
            MultiplayerSceneLauncher launcher = MultiplayerSceneLauncher.Instance;
            if (launcher != null)
            {
                launcher.ReturnToMainMenu();
                return;
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(MainMenuSceneName);
        }

        internal static bool TryConsumeLaunch(
            out MultiplayerLaunchMode mode, out string joinCode)
        {
            mode = _pendingMode;
            joinCode = _pendingJoinCode;
            _pendingMode = MultiplayerLaunchMode.None;
            _pendingJoinCode = string.Empty;
            return mode != MultiplayerLaunchMode.None;
        }

        internal static void SetLastError(string message)
        {
            _lastError = message ?? string.Empty;
        }

        private static bool PrepareLaunch(MultiplayerLaunchMode mode, string joinCode)
        {
            _lastError = string.Empty;
            if (!Application.CanStreamedLevelBeLoaded(MultiplayerSceneName))
            {
                _lastError = $"Scene '{MultiplayerSceneName}' is not enabled in Build Settings.";
                return false;
            }

            _pendingMode = mode;
            _pendingJoinCode = joinCode;
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(MultiplayerSceneName);
            return true;
        }
    }

    [Serializable]
    internal struct MultiplayerSessionSnapshot
    {
        public bool active;
        public string state;
        public string joinCode;
        public string error;
        public int players;
        public bool host;
    }

    /// <summary>
    /// Consumes the main-menu request after the multiplayer scene loads and starts
    /// its Unity Services session. Sessions configure Relay and Unity Transport,
    /// allowing players on separate networks to connect with a short join code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerSceneLauncher : MonoBehaviour
    {
        [SerializeField] private UnityServicesSessionBootstrap servicesBootstrap;
        [SerializeField] private NetworkManager networkManager;
        [SerializeField, Min(1f)] private float leaveTimeoutSeconds = 8f;

        private MultiplayerLaunchMode _mode;
        private string _state = "LOADING";
        private string _localError = string.Empty;
        private bool _returningToMenu;
        private readonly Dictionary<ulong, AssignedSpawnPose> _assignedSpawnPoses =
            new Dictionary<ulong, AssignedSpawnPose>();

        public static MultiplayerSceneLauncher Instance { get; private set; }

        public string CurrentJoinCode => servicesBootstrap != null
            ? servicesBootstrap.CurrentJoinCode
            : string.Empty;

        private void Awake()
        {
            Instance = this;
            if (servicesBootstrap == null)
                servicesBootstrap = GetComponent<UnityServicesSessionBootstrap>();
            if (networkManager == null) networkManager = GetComponent<NetworkManager>();

            if (networkManager != null)
            {
                networkManager.NetworkConfig.ConnectionApproval = true;
                networkManager.ConnectionApprovalCallback = ApproveConnection;
                networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        private IEnumerator Start()
        {
            yield return null;

            if (!MultiplayerMenuBridge.TryConsumeLaunch(out _mode, out string joinCode))
            {
                _state = "DEVELOPMENT READY";
                yield break;
            }

            if (servicesBootstrap == null)
            {
                SetError("Unity Services session bootstrap is missing from NetworkManager.");
                yield break;
            }

            bool accepted;
            if (_mode == MultiplayerLaunchMode.Host)
            {
                _state = "STARTING RELAY HOST";
                accepted = servicesBootstrap.StartHost();
            }
            else
            {
                _state = "JOINING RELAY SESSION";
                servicesBootstrap.JoinCodeToJoin = joinCode;
                accepted = servicesBootstrap.StartClient();
            }

            if (!accepted)
            {
                SetError(string.IsNullOrEmpty(servicesBootstrap.LastError)
                    ? "Could not start the multiplayer session."
                    : servicesBootstrap.LastError);
                yield break;
            }

            while (servicesBootstrap.IsBusy) yield return null;

            if (!string.IsNullOrEmpty(servicesBootstrap.LastError))
            {
                SetError(servicesBootstrap.LastError);
                yield break;
            }

            if (!servicesBootstrap.IsStarted)
            {
                SetError("Session setup completed, but Netcode did not start listening.");
                yield break;
            }

            _state = _mode == MultiplayerLaunchMode.Host ? "HOSTING" : "CONNECTED";
        }

        private void OnDestroy()
        {
            if (networkManager != null)
            {
                if (networkManager.ConnectionApprovalCallback == ApproveConnection)
                    networkManager.ConnectionApprovalCallback = null;
                networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            _assignedSpawnPoses.Clear();
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Returns the server-selected initial pose for a player. Connection approval records
        /// this before Netcode creates the player object, so even a paused first frame starts
        /// on a scene spawn marker instead of the prefab-authored transform.
        /// </summary>
        public bool TryGetAssignedSpawnPose(ulong clientId, out Vector3 position,
            out Quaternion rotation)
        {
            if (_assignedSpawnPoses.TryGetValue(clientId, out AssignedSpawnPose pose))
            {
                position = pose.Position;
                rotation = pose.Rotation;
                return true;
            }

            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        public string ReadSnapshot()
        {
            string bootstrapError = servicesBootstrap != null
                ? servicesBootstrap.LastError
                : string.Empty;
            string error = !string.IsNullOrEmpty(bootstrapError) ? bootstrapError : _localError;

            string state = _state;
            if (!string.IsNullOrEmpty(error)) state = "CONNECTION FAILED";
            else if (servicesBootstrap != null && servicesBootstrap.IsBusy)
                state = _mode == MultiplayerLaunchMode.Host
                    ? "STARTING RELAY HOST"
                    : "JOINING RELAY SESSION";
            else if (servicesBootstrap != null && servicesBootstrap.IsStarted)
                state = _mode == MultiplayerLaunchMode.Host ? "HOSTING" : "CONNECTED";

            int players = 0;
            if (networkManager != null)
            {
                if (networkManager.IsServer) players = networkManager.ConnectedClients.Count;
                else if (networkManager.IsConnectedClient) players = 1;
            }

            var snapshot = new MultiplayerSessionSnapshot
            {
                active = true,
                state = state,
                joinCode = CurrentJoinCode,
                error = error,
                players = players,
                host = _mode == MultiplayerLaunchMode.Host
            };
            return JsonUtility.ToJson(snapshot);
        }

        public void ReturnToMainMenu()
        {
            if (_returningToMenu) return;
            StartCoroutine(ReturnToMainMenuRoutine());
        }

        private IEnumerator ReturnToMainMenuRoutine()
        {
            _returningToMenu = true;
            _state = "LEAVING SESSION";
            Time.timeScale = 1f;

            if (servicesBootstrap != null)
            {
                servicesBootstrap.Stop();
                float deadline = Time.realtimeSinceStartup + leaveTimeoutSeconds;
                while (!servicesBootstrap.CurrentOperation.IsCompleted
                    && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }

            if (networkManager != null && networkManager.IsListening)
                networkManager.Shutdown();

            MultiplayerMenuBridge.SetLastError(string.Empty);
            SceneManager.LoadScene(MultiplayerMenuBridge.MainMenuSceneName);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            _assignedSpawnPoses.Remove(clientId);

            if (_returningToMenu || _mode == MultiplayerLaunchMode.None
                || networkManager == null) return;
            if (clientId != networkManager.LocalClientId) return;

            SetError("Disconnected from multiplayer session.");
        }

        private void SetError(string message)
        {
            _localError = message ?? string.Empty;
            _state = "CONNECTION FAILED";
            MultiplayerMenuBridge.SetLastError(_localError);
            Debug.LogError($"[{nameof(MultiplayerSceneLauncher)}] {_localError}", this);
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Pending = false;

            if (!TrySelectInitialSpawnPoint(request.ClientNetworkId,
                    out NetworkSpawnPoint spawnPoint, out int spawnIndex)) return;

            var pose = new AssignedSpawnPose(spawnPoint.Position, spawnPoint.Rotation,
                spawnIndex);
            _assignedSpawnPoses[request.ClientNetworkId] = pose;
            response.Position = pose.Position;
            response.Rotation = pose.Rotation;
        }

        private bool TrySelectInitialSpawnPoint(ulong clientId,
            out NetworkSpawnPoint selected, out int selectedIndex)
        {
            NetworkSpawnPoint[] discovered = FindObjectsByType<NetworkSpawnPoint>(
                FindObjectsSortMode.None);
            Array.Sort(discovered, CompareSpawnPoints);

            var candidates = new List<NetworkSpawnPoint>(discovered.Length);
            foreach (NetworkSpawnPoint spawnPoint in discovered)
            {
                if (spawnPoint != null && spawnPoint.isActiveAndEnabled)
                    candidates.Add(spawnPoint);
            }

            selected = null;
            selectedIndex = -1;
            if (candidates.Count == 0) return false;

            var usedIndices = new HashSet<int>();
            foreach (AssignedSpawnPose pose in _assignedSpawnPoses.Values)
                usedIndices.Add(pose.SpawnIndex);

            // Client 0 is Player 1, client 1 is Player 2, and so on. Prefer that
            // exact numbered marker instead of distance-based selection.
            if (clientId < (ulong)candidates.Count)
            {
                int preferredIndex = (int)clientId;
                if (!usedIndices.Contains(preferredIndex)) selectedIndex = preferredIndex;
            }

            if (selectedIndex < 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (usedIndices.Contains(i)) continue;
                    selectedIndex = i;
                    break;
                }
            }

            // More simultaneous players than markers: reuse deterministically.
            if (selectedIndex < 0) selectedIndex = (int)(clientId % (ulong)candidates.Count);

            selected = candidates[selectedIndex];
            return true;
        }

        private static int CompareSpawnPoints(NetworkSpawnPoint left,
            NetworkSpawnPoint right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            return string.CompareOrdinal(left.name, right.name);
        }

        private readonly struct AssignedSpawnPose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly int SpawnIndex;

            public AssignedSpawnPose(Vector3 position, Quaternion rotation, int spawnIndex)
            {
                Position = position;
                Rotation = rotation;
                SpawnIndex = spawnIndex;
            }
        }
    }
}
