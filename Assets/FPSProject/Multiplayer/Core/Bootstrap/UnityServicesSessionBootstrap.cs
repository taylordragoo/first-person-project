using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Bootstrap
{
    /// <summary>
    /// Anonymous-authentication Sessions bootstrap backed by Relay. The Multiplayer
    /// Services network module configures Unity Transport and starts NGO after the
    /// session is created or joined.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnityServicesSessionBootstrap : MonoBehaviour,
        IJoinCodeSessionBootstrap
    {
        [Header("Session")]
        [SerializeField] private string sessionName = "CAS Tactical Session";
        [SerializeField, Range(2, 8)] private int maxPlayers = 8;
        [SerializeField] private bool privateSession = true;
        [SerializeField] private string relayRegion = string.Empty;
        [SerializeField] private string joinCodeToJoin = string.Empty;

        [Header("Authentication")]
        [Tooltip("Optional UGS authentication profile. A unique process profile is used when empty.")]
        [SerializeField] private string authenticationProfile = string.Empty;

        [Header("References")]
        [SerializeField] private NetworkManager networkManager;

        private ISession _session;
        private Task _currentOperation;
        private int _operationGeneration;

        public bool IsStarted => _session != null
            && networkManager != null
            && networkManager.IsListening;
        public bool IsBusy { get; private set; }
        public string CurrentJoinCode { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public Task CurrentOperation => _currentOperation ?? Task.CompletedTask;

        public string JoinCodeToJoin
        {
            get => joinCodeToJoin;
            set => joinCodeToJoin = SessionBootstrapUtility.NormalizeJoinCode(value);
        }

        private void Awake()
        {
            ResolveNetworkManager();
            string commandLineCode = SessionBootstrapUtility.GetCommandLineValue(
                Environment.GetCommandLineArgs(), "-fpsSessionCode");
            if (!string.IsNullOrWhiteSpace(commandLineCode))
                JoinCodeToJoin = commandLineCode;
        }

        public bool StartHost()
        {
            if (!CanBeginOperation()) return false;
            int generation = ++_operationGeneration;
            _currentOperation = RunOperationAsync(() => CreateSessionAsync(generation), generation);
            return true;
        }

        public bool StartClient()
        {
            if (!CanBeginOperation()) return false;
            JoinCodeToJoin = joinCodeToJoin;
            if (string.IsNullOrEmpty(joinCodeToJoin))
            {
                LastError = "A session join code is required before starting a Services client.";
                Debug.LogError($"[{nameof(UnityServicesSessionBootstrap)}] {LastError}", this);
                return false;
            }

            int generation = ++_operationGeneration;
            _currentOperation = RunOperationAsync(() => JoinSessionAsync(generation), generation);
            return true;
        }

        public void Stop()
        {
            ++_operationGeneration;
            IsBusy = false;
            CurrentJoinCode = string.Empty;
            ISession session = _session;
            _session = null;

            if (session != null)
            {
                _currentOperation = LeaveSessionAsync(session);
            }
            else if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
        }

        private bool CanBeginOperation()
        {
            LastError = string.Empty;
            if (IsBusy || IsStarted) return false;
            if (ResolveNetworkManager()) return true;

            LastError = "No NetworkManager was found for the Services session bootstrap.";
            Debug.LogError($"[{nameof(UnityServicesSessionBootstrap)}] {LastError}", this);
            return false;
        }

        private bool ResolveNetworkManager()
        {
            if (networkManager == null) networkManager = GetComponent<NetworkManager>();
            if (networkManager == null) networkManager = NetworkManager.Singleton;
            return networkManager != null;
        }

        private async Task RunOperationAsync(Func<Task> operation, int generation)
        {
            IsBusy = true;
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                if (generation != _operationGeneration) return;
                LastError = exception.Message;
                Debug.LogError($"[{nameof(UnityServicesSessionBootstrap)}] {exception}", this);
                _session = null;
                CurrentJoinCode = string.Empty;
            }
            finally
            {
                if (generation == _operationGeneration) IsBusy = false;
            }
        }

        private async Task CreateSessionAsync(int generation)
        {
            await EnsureServicesReadyAsync();
            var options = new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = Mathf.Clamp(maxPlayers, 2, 8),
                IsPrivate = privateSession
            }.WithRelayNetwork(string.IsNullOrWhiteSpace(relayRegion) ? null : relayRegion.Trim());

            ISession created = await MultiplayerService.Instance.CreateSessionAsync(options);
            if (generation != _operationGeneration)
            {
                await created.LeaveAsync();
                return;
            }

            _session = created;
            CurrentJoinCode = created.Code;
            Debug.Log($"[{nameof(UnityServicesSessionBootstrap)}] Relay host ready. "
                + $"Join code: {CurrentJoinCode}", this);
        }

        private async Task JoinSessionAsync(int generation)
        {
            await EnsureServicesReadyAsync();
            ISession joined = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                joinCodeToJoin);
            if (generation != _operationGeneration)
            {
                await joined.LeaveAsync();
                return;
            }

            _session = joined;
            CurrentJoinCode = joined.Code;
            Debug.Log($"[{nameof(UnityServicesSessionBootstrap)}] Joined Relay session "
                + $"{CurrentJoinCode}.", this);
        }

        private async Task EnsureServicesReadyAsync()
        {
            string requestedProfile = SessionBootstrapUtility.GetCommandLineValue(
                Environment.GetCommandLineArgs(), "-fpsServicesProfile");
            string fallbackProfile = "fps-" + System.Diagnostics.Process.GetCurrentProcess().Id;
            string profile = SessionBootstrapUtility.BuildAuthenticationProfile(
                string.IsNullOrWhiteSpace(requestedProfile) ? authenticationProfile : requestedProfile,
                fallbackProfile);

            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions().SetProfile(profile);
                await UnityServices.InitializeAsync(options);
            }
            else if (!AuthenticationService.Instance.IsSignedIn
                && !string.Equals(AuthenticationService.Instance.Profile, profile,
                    StringComparison.Ordinal))
            {
                AuthenticationService.Instance.SwitchProfile(profile);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private async Task LeaveSessionAsync(ISession session)
        {
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogWarning($"[{nameof(UnityServicesSessionBootstrap)}] "
                    + $"Failed to leave session cleanly: {exception.Message}", this);
            }
        }
    }
}
