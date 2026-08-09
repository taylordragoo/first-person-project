using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
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
        private static MatchLaunchSettings _pendingMatchSettings = MatchLaunchSettings.Default;
        private static string _lastError = string.Empty;

        public static string LastError => _lastError;

        public static bool LaunchHost()
        {
            return LaunchHost(MatchLaunchSettings.Default.alphaBotCount,
                MatchLaunchSettings.Default.bravoBotCount,
                MatchLaunchSettings.Default.durationSeconds / 60,
                MatchLaunchSettings.Default.preferredTeam,
                MatchLaunchSettings.Default.map);
        }

        public static bool LaunchHost(int botCount, int durationMinutes,
            int preferredTeam, int map)
        {
            int sanitizedTotal = Mathf.Clamp(botCount, 0,
                MatchLaunchSettings.MaxCombatants);
            return LaunchHost((sanitizedTotal + 1) / 2, sanitizedTotal / 2,
                durationMinutes, preferredTeam, map);
        }

        public static bool LaunchHost(int alphaBotCount, int bravoBotCount,
            int durationMinutes, int preferredTeam, int map)
        {
            MatchLaunchSettings settings = MatchLaunchSettings.Default;
            settings.alphaBotCount = alphaBotCount;
            settings.bravoBotCount = bravoBotCount;
            settings.durationSeconds = durationMinutes * 60;
            settings.preferredTeam = preferredTeam;
            settings.map = map;
            settings = MatchRules.SanitizeHostSettings(settings);
            return PrepareLaunch(MultiplayerLaunchMode.Host, string.Empty, settings);
        }

        public static bool LaunchClient(string joinCode)
        {
            return LaunchClient(joinCode, (int)MatchTeam.Unassigned);
        }

        public static bool LaunchClient(string joinCode, int preferredTeam)
        {
            string normalizedCode = SessionBootstrapUtility.NormalizeJoinCode(joinCode);
            if (string.IsNullOrEmpty(normalizedCode))
            {
                _lastError = "Enter a session code before joining.";
                return false;
            }

            MatchLaunchSettings settings = MatchLaunchSettings.Default;
            settings.preferredTeam = preferredTeam;
            return PrepareLaunch(MultiplayerLaunchMode.Client, normalizedCode, settings);
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
                host = false,
                matchActive = false,
                phase = "WAITING",
                map = "DUST2",
                team = "UNASSIGNED",
                alphaScore = 0,
                bravoScore = 0,
                remainingSeconds = 0,
                bots = 0,
                alphaBots = 0,
                bravoBots = 0,
                alphaPlayers = 0,
                bravoPlayers = 0,
                winner = "UNASSIGNED",
                weapon = "LOADOUT 01"
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
            out MultiplayerLaunchMode mode, out string joinCode,
            out MatchLaunchSettings matchSettings)
        {
            mode = _pendingMode;
            joinCode = _pendingJoinCode;
            matchSettings = MatchRules.Sanitize(_pendingMatchSettings);
            _pendingMode = MultiplayerLaunchMode.None;
            _pendingJoinCode = string.Empty;
            _pendingMatchSettings = MatchLaunchSettings.Default;
            return mode != MultiplayerLaunchMode.None;
        }

        internal static void SetLastError(string message)
        {
            _lastError = message ?? string.Empty;
        }

        private static bool PrepareLaunch(MultiplayerLaunchMode mode, string joinCode,
            MatchLaunchSettings matchSettings)
        {
            _lastError = string.Empty;
            if (!Application.CanStreamedLevelBeLoaded(MultiplayerSceneName))
            {
                _lastError = $"Scene '{MultiplayerSceneName}' is not enabled in Build Settings.";
                return false;
            }

            _pendingMode = mode;
            _pendingJoinCode = joinCode;
            _pendingMatchSettings = MatchRules.Sanitize(matchSettings);
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
        public bool matchActive;
        public string phase;
        public string map;
        public string team;
        public int alphaScore;
        public int bravoScore;
        public int remainingSeconds;
        public int bots;
        public int alphaBots;
        public int bravoBots;
        public int alphaPlayers;
        public int bravoPlayers;
        public string winner;
        public string weapon;
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
        private MatchLaunchSettings _matchSettings = MatchLaunchSettings.Default;
        private string _state = "LOADING";
        private string _localError = string.Empty;
        private bool _returningToMenu;
        private readonly Dictionary<ulong, AssignedSpawnPose> _assignedSpawnPoses =
            new Dictionary<ulong, AssignedSpawnPose>();
        private readonly Dictionary<ulong, MatchTeam> _assignedTeams =
            new Dictionary<ulong, MatchTeam>();

        public static MultiplayerSceneLauncher Instance { get; private set; }

        public string CurrentJoinCode => servicesBootstrap != null
            ? servicesBootstrap.CurrentJoinCode
            : string.Empty;
        public MatchLaunchSettings CurrentMatchSettings => _matchSettings;

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

            if (!MultiplayerMenuBridge.TryConsumeLaunch(out _mode, out string joinCode,
                    out _matchSettings))
            {
                _matchSettings = MatchLaunchSettings.Default;
                _state = "DEVELOPMENT READY";
                yield break;
            }

            if (servicesBootstrap == null)
            {
                SetError("Unity Services session bootstrap is missing from NetworkManager.");
                yield break;
            }

            bool accepted;
            ConfigureConnectionPayload(_matchSettings.PreferredTeam);
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
            _assignedTeams.Clear();
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

        public bool TryGetAssignedTeam(ulong clientId, out MatchTeam team)
        {
            return _assignedTeams.TryGetValue(clientId, out team);
        }

        public int GetAssignedHumanCount(MatchTeam team)
        {
            int count = 0;
            foreach (MatchTeam assignedTeam in _assignedTeams.Values)
            {
                if (assignedTeam == team) count++;
            }
            return count;
        }

        public bool IsHumanSpawnSlotAssigned(MatchTeam team, int teamSlot)
        {
            foreach (AssignedSpawnPose pose in _assignedSpawnPoses.Values)
            {
                if (pose.Team == team && pose.TeamSlot == teamSlot) return true;
            }
            return false;
        }

        public string ReadSnapshot()
        {
            string bootstrapError = servicesBootstrap != null
                ? servicesBootstrap.LastError
                : string.Empty;
            string error = !string.IsNullOrEmpty(bootstrapError) ? bootstrapError : _localError;

            bool networkIsHost = networkManager != null && networkManager.IsHost;
            bool networkIsClient = networkManager != null && networkManager.IsConnectedClient;

            string state = _state;
            if (!string.IsNullOrEmpty(error)) state = "CONNECTION FAILED";
            else if (servicesBootstrap != null && servicesBootstrap.IsBusy)
                state = _mode == MultiplayerLaunchMode.Host
                    ? "STARTING RELAY HOST"
                    : "JOINING RELAY SESSION";
            else if (networkIsHost) state = "HOSTING";
            else if (networkIsClient) state = "CONNECTED";

            int players = 0;
            if (networkManager != null && networkManager.IsListening)
                players = networkManager.ConnectedClientsIds.Count;

            TeamDeathmatchManager match = TeamDeathmatchManager.Instance;
            bool matchActive = match != null && match.IsSpawned;
            MatchTeam localTeam = matchActive
                ? match.GetLocalPlayerTeam()
                : MatchTeam.Unassigned;

            var snapshot = new MultiplayerSessionSnapshot
            {
                active = true,
                state = state,
                joinCode = CurrentJoinCode,
                error = error,
                players = players,
                host = networkIsHost || _mode == MultiplayerLaunchMode.Host,
                matchActive = matchActive,
                phase = matchActive ? match.Phase.Value.ToString().ToUpperInvariant() : "WAITING",
                map = matchActive ? match.ActiveMap.Value.ToString().ToUpperInvariant() : "DUST2",
                team = localTeam.ToString().ToUpperInvariant(),
                alphaScore = matchActive ? match.AlphaScore.Value : 0,
                bravoScore = matchActive ? match.BravoScore.Value : 0,
                remainingSeconds = matchActive ? match.RemainingSeconds.Value : 0,
                bots = matchActive ? match.ActiveBotCount.Value : 0,
                alphaBots = matchActive ? match.ActiveAlphaBotCount.Value : 0,
                bravoBots = matchActive ? match.ActiveBravoBotCount.Value : 0,
                alphaPlayers = matchActive ? match.AlphaHumanCount.Value : 0,
                bravoPlayers = matchActive ? match.BravoHumanCount.Value : 0,
                winner = matchActive
                    ? match.WinningTeam.Value.ToString().ToUpperInvariant()
                    : "UNASSIGNED",
                weapon = matchActive ? $"LOADOUT {match.DefaultWeaponId.Value:00}" : "LOADOUT 01"
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
            SceneManager.LoadSceneAsync(MultiplayerMenuBridge.MainMenuSceneName);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            _assignedSpawnPoses.Remove(clientId);
            _assignedTeams.Remove(clientId);

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
            response.Pending = false;
            MatchTeam preferredTeam = ReadRequestedTeam(request.Payload);
            int alphaHumans = GetAssignedHumanCount(MatchTeam.Alpha);
            int bravoHumans = GetAssignedHumanCount(MatchTeam.Bravo);
            MatchTeam assignedTeam = MatchRules.SelectAvailableTeam(preferredTeam,
                alphaHumans, bravoHumans);

            if (!MatchRules.IsPlayableTeam(assignedTeam))
            {
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Reason = MatchRules.IsPlayableTeam(preferredTeam)
                    ? $"{preferredTeam} already has four real players. Choose the other team."
                    : "The match is full.";
                return;
            }

            if (!TrySelectInitialSpawnPoint(assignedTeam, out NetworkSpawnPoint spawnPoint,
                    out int teamSlot))
            {
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Reason = $"No {_matchSettings.Map} spawn point is available for {assignedTeam}.";
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = true;
            _assignedTeams[request.ClientNetworkId] = assignedTeam;
            var pose = new AssignedSpawnPose(spawnPoint.Position, spawnPoint.Rotation,
                _matchSettings.Map, assignedTeam, teamSlot);
            _assignedSpawnPoses[request.ClientNetworkId] = pose;
            response.Position = pose.Position;
            response.Rotation = pose.Rotation;
        }

        private bool TrySelectInitialSpawnPoint(MatchTeam team,
            out NetworkSpawnPoint selected, out int selectedTeamSlot)
        {
            List<NetworkSpawnPoint> candidates = MatchSpawnCatalog.Find(_matchSettings.Map, team);
            selected = null;
            selectedTeamSlot = -1;
            if (candidates.Count == 0) return false;

            var usedSlots = new HashSet<int>();
            foreach (AssignedSpawnPose pose in _assignedSpawnPoses.Values)
            {
                if (pose.Map == _matchSettings.Map && pose.Team == team)
                    usedSlots.Add(pose.TeamSlot);
            }

            foreach (NetworkSpawnPoint candidate in candidates)
            {
                if (!MatchSpawnCatalog.TryClassify(candidate, out _, out _, out int slot))
                    continue;
                if (usedSlots.Contains(slot)) continue;
                selected = candidate;
                selectedTeamSlot = slot;
                return true;
            }

            return false;
        }

        private void ConfigureConnectionPayload(MatchTeam preferredTeam)
        {
            if (networkManager == null) return;
            var payload = new MatchConnectionPayload { team = (int)preferredTeam };
            networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(payload));
        }

        private static MatchTeam ReadRequestedTeam(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return MatchTeam.Unassigned;
            try
            {
                string json = Encoding.UTF8.GetString(payload);
                var request = JsonUtility.FromJson<MatchConnectionPayload>(json);
                MatchTeam team = (MatchTeam)request.team;
                return MatchRules.IsPlayableTeam(team) ? team : MatchTeam.Unassigned;
            }
            catch
            {
                return MatchTeam.Unassigned;
            }
        }

        private readonly struct AssignedSpawnPose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly MatchMap Map;
            public readonly MatchTeam Team;
            public readonly int TeamSlot;

            public AssignedSpawnPose(Vector3 position, Quaternion rotation, MatchMap map,
                MatchTeam team, int teamSlot)
            {
                Position = position;
                Rotation = rotation;
                Map = map;
                Team = team;
                TeamSlot = teamSlot;
            }
        }

        [Serializable]
        private struct MatchConnectionPayload
        {
            public int team;
        }
    }
}
