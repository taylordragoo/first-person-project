using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace FPSProject.Multiplayer.Core.Diagnostics
{
    /// <summary>
    /// Repeatable Step 11 harness. F8 toggles the target adverse network profile and
    /// F10 writes a performance report. Command-line flags support unattended
    /// multi-process runs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPerformanceHarness : MonoBehaviour
    {
        [Header("Target adverse conditions")]
        [SerializeField] private int packetDelayMs = 75;
        [SerializeField] private int packetJitterMs = 20;
        [SerializeField, Range(0, 100)] private int packetLossPercent = 5;

        [Header("Capture")]
        [SerializeField] private int expectedPlayers = 8;
        [SerializeField] private float captureSeconds = 30f;
        [SerializeField] private float playerWaitTimeoutSeconds = 60f;
        [SerializeField] private bool enableHotkeys = true;
        [SerializeField] private NetworkSimulator networkSimulator;

        private readonly List<float> _frameTimesMs = new List<float>(4096);
        private ProfilerRecorder _gcAllocatedRecorder;
        private ProfilerRecorder _bytesSentRecorder;
        private ProfilerRecorder _bytesReceivedRecorder;
        private ProfilerRecorder _drawCallsRecorder;
        private ProfilerRecorder _mainThreadRecorder;
        private ProfilerRecorder _animationRecorder;
        private float _captureElapsed;
        private float _waitElapsed;
        private float _frameTimeTotal;
        private float _frameTimeMax;
        private long _gcAllocatedBytes;
        private long _bytesSent;
        private long _bytesReceived;
        private long _drawCallsTotal;
        private long _mainThreadNanoseconds;
        private long _mainThreadMaxNanoseconds;
        private long _animationNanoseconds;
        private long _animationMaxNanoseconds;
        private int _maxConnectedPlayers;
        private int _gcCollectionStart;
        private long _allocatedMemoryStart;
        private long _monoMemoryStart;
        private bool _autoCapture;
        private bool _exitAfterReport;
        private bool _adverseConditionsApplied;
        private string _reportDirectory = string.Empty;

        public bool IsCapturing { get; private set; }
        public string LastReportPath { get; private set; } = string.Empty;

        private void Awake()
        {
            if (networkSimulator == null) networkSimulator = GetComponent<NetworkSimulator>();
            if (networkSimulator == null) networkSimulator = gameObject.AddComponent<NetworkSimulator>();
            CreateRecorders();
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            string profile = Bootstrap.SessionBootstrapUtility.GetCommandLineValue(
                args, "-fpsNetworkProfile");
            if (string.Equals(profile, "adverse", StringComparison.OrdinalIgnoreCase))
                ApplyAdverseConditions();

            captureSeconds = Bootstrap.SessionBootstrapUtility.GetPositiveCommandLineInt(
                args, "-fpsProfileSeconds", Mathf.CeilToInt(captureSeconds));
            expectedPlayers = Bootstrap.SessionBootstrapUtility.GetPositiveCommandLineInt(
                args, "-fpsProfilePlayers", expectedPlayers);
            playerWaitTimeoutSeconds = Bootstrap.SessionBootstrapUtility.GetPositiveCommandLineInt(
                args, "-fpsProfileWaitSeconds", Mathf.CeilToInt(playerWaitTimeoutSeconds));
            _autoCapture = !string.IsNullOrEmpty(
                Bootstrap.SessionBootstrapUtility.GetCommandLineValue(args, "-fpsProfileSeconds"));
            _exitAfterReport = Bootstrap.SessionBootstrapUtility.HasCommandLineFlag(
                args, "-fpsExitAfterReport");
            _reportDirectory = Bootstrap.SessionBootstrapUtility.GetCommandLineValue(
                args, "-fpsReportDirectory") ?? string.Empty;
        }

        private void Update()
        {
            if (enableHotkeys && Input.GetKeyDown(KeyCode.F8))
            {
                if (_adverseConditionsApplied) ClearNetworkConditions();
                else ApplyAdverseConditions();
            }

            if (enableHotkeys && Input.GetKeyDown(KeyCode.F10))
            {
                if (IsCapturing) EndCapture();
                else BeginCapture();
            }

            if (_autoCapture && !IsCapturing)
            {
                _waitElapsed += Time.unscaledDeltaTime;
                int connected = GetConnectedPlayerCount();
                if (connected >= expectedPlayers || _waitElapsed >= playerWaitTimeoutSeconds)
                {
                    _autoCapture = false;
                    BeginCapture();
                }
            }

            if (!IsCapturing) return;

            float frameMs = Time.unscaledDeltaTime * 1000f;
            _frameTimesMs.Add(frameMs);
            _frameTimeTotal += frameMs;
            _frameTimeMax = Mathf.Max(_frameTimeMax, frameMs);
            _captureElapsed += Time.unscaledDeltaTime;
            _maxConnectedPlayers = Mathf.Max(_maxConnectedPlayers, GetConnectedPlayerCount());

            if (_gcAllocatedRecorder.Valid) _gcAllocatedBytes += Math.Max(0, _gcAllocatedRecorder.LastValue);
            if (_bytesSentRecorder.Valid) _bytesSent += Math.Max(0, _bytesSentRecorder.LastValue);
            if (_bytesReceivedRecorder.Valid) _bytesReceived += Math.Max(0, _bytesReceivedRecorder.LastValue);
            if (_drawCallsRecorder.Valid) _drawCallsTotal += Math.Max(0, _drawCallsRecorder.LastValue);
            if (_mainThreadRecorder.Valid)
            {
                long value = Math.Max(0, _mainThreadRecorder.LastValue);
                _mainThreadNanoseconds += value;
                _mainThreadMaxNanoseconds = Math.Max(_mainThreadMaxNanoseconds, value);
            }
            if (_animationRecorder.Valid)
            {
                long value = Math.Max(0, _animationRecorder.LastValue);
                _animationNanoseconds += value;
                _animationMaxNanoseconds = Math.Max(_animationMaxNanoseconds, value);
            }

            if (_captureElapsed >= captureSeconds) EndCapture();
        }

        public void ApplyAdverseConditions()
        {
            networkSimulator.ConnectionPreset = NetworkSimulatorPreset.Create(
                "CAS Target Adverse",
                "150 ms target RTT, jitter, and 5% loss",
                packetDelayMs,
                packetJitterMs,
                0,
                packetLossPercent);
            _adverseConditionsApplied = true;
            Debug.Log($"[{nameof(NetworkPerformanceHarness)}] Applied target profile: "
                + $"~{packetDelayMs * 2} ms RTT, {packetJitterMs} ms jitter, "
                + $"{packetLossPercent}% loss.", this);
        }

        public void ClearNetworkConditions()
        {
            networkSimulator.ConnectionPreset = NetworkSimulatorPresets.None;
            _adverseConditionsApplied = false;
            Debug.Log($"[{nameof(NetworkPerformanceHarness)}] Cleared network simulation.", this);
        }

        public void BeginCapture()
        {
            _frameTimesMs.Clear();
            _captureElapsed = 0f;
            _frameTimeTotal = 0f;
            _frameTimeMax = 0f;
            _gcAllocatedBytes = 0;
            _bytesSent = 0;
            _bytesReceived = 0;
            _drawCallsTotal = 0;
            _mainThreadNanoseconds = 0;
            _mainThreadMaxNanoseconds = 0;
            _animationNanoseconds = 0;
            _animationMaxNanoseconds = 0;
            _maxConnectedPlayers = GetConnectedPlayerCount();
            _gcCollectionStart = GC.CollectionCount(0);
            _allocatedMemoryStart = Profiler.GetTotalAllocatedMemoryLong();
            _monoMemoryStart = Profiler.GetMonoUsedSizeLong();
            IsCapturing = true;
        }

        public void EndCapture()
        {
            if (!IsCapturing) return;
            IsCapturing = false;

            var manager = NetworkManager.Singleton;
            int frameCount = _frameTimesMs.Count;
            var report = new MultiplayerPerformanceReport
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"),
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                role = manager == null ? "offline" : manager.IsHost ? "host" : "client",
                connectedPlayers = _maxConnectedPlayers,
                expectedPlayers = expectedPlayers,
                captureSeconds = _captureElapsed,
                frameCount = frameCount,
                averageFrameMs = frameCount > 0 ? _frameTimeTotal / frameCount : 0f,
                p95FrameMs = MultiplayerPerformanceMath.Percentile(_frameTimesMs, 0.95f),
                maxFrameMs = _frameTimeMax,
                averageDrawCalls = frameCount > 0 ? (float)_drawCallsTotal / frameCount : 0f,
                averageMainThreadMs = frameCount > 0
                    ? _mainThreadNanoseconds / (frameCount * 1000000f) : 0f,
                maxMainThreadMs = _mainThreadMaxNanoseconds / 1000000f,
                averageAnimationMs = frameCount > 0
                    ? _animationNanoseconds / (frameCount * 1000000f) : 0f,
                maxAnimationMs = _animationMaxNanoseconds / 1000000f,
                gcAllocatedBytes = _gcAllocatedBytes,
                gcCollections = GC.CollectionCount(0) - _gcCollectionStart,
                allocatedMemoryStart = _allocatedMemoryStart,
                allocatedMemoryEnd = Profiler.GetTotalAllocatedMemoryLong(),
                monoMemoryStart = _monoMemoryStart,
                monoMemoryEnd = Profiler.GetMonoUsedSizeLong(),
                totalBytesSent = _bytesSent,
                totalBytesReceived = _bytesReceived,
                sentBytesPerSecond = _captureElapsed > 0f ? _bytesSent / _captureElapsed : 0f,
                receivedBytesPerSecond = _captureElapsed > 0f ? _bytesReceived / _captureElapsed : 0f,
                networkCountersAvailable = _bytesSentRecorder.Valid && _bytesReceivedRecorder.Valid,
                cpuCounterAvailable = _mainThreadRecorder.Valid,
                animationCounterAvailable = _animationRecorder.Valid,
                drawCallsCounterAvailable = _drawCallsRecorder.Valid,
                packetDelayMs = _adverseConditionsApplied ? packetDelayMs : 0,
                packetJitterMs = _adverseConditionsApplied ? packetJitterMs : 0,
                packetLossPercent = _adverseConditionsApplied ? packetLossPercent : 0
            };

            LastReportPath = WriteReport(report);
            Debug.Log($"[{nameof(NetworkPerformanceHarness)}] PERF_REPORT "
                + JsonUtility.ToJson(report), this);

            if (_exitAfterReport) StartCoroutine(ExitAfterReport());
        }

        private IEnumerator ExitAfterReport()
        {
            var services = GetComponent<Bootstrap.UnityServicesSessionBootstrap>();
            if (services != null && services.IsStarted)
            {
                // In unattended two-peer runs, let clients leave before the host destroys
                // the Relay allocation and disposes their session network handler.
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
                    yield return new WaitForSecondsRealtime(1f);

                services.Stop();
                float waitStarted = Time.realtimeSinceStartup;
                while (!services.CurrentOperation.IsCompleted
                    && Time.realtimeSinceStartup - waitStarted < 10f)
                {
                    yield return null;
                }
            }

            Application.Quit();
        }

        private int GetConnectedPlayerCount()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return 0;
            if (manager.IsServer) return manager.ConnectedClients.Count;
            return manager.IsConnectedClient ? 1 : 0;
        }

        private string WriteReport(MultiplayerPerformanceReport report)
        {
            try
            {
                string directory = string.IsNullOrWhiteSpace(_reportDirectory)
                    ? Path.Combine(Application.persistentDataPath, "MultiplayerPerformance")
                    : Path.GetFullPath(_reportDirectory);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory,
                    $"multiplayer-perf-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{report.processId}.json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[{nameof(NetworkPerformanceHarness)}] Could not write report: "
                    + exception.Message, this);
                return string.Empty;
            }
        }

        private void CreateRecorders()
        {
            _gcAllocatedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Allocated In Frame");
            _bytesSentRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Network, "Total Bytes Sent");
            _bytesReceivedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Network, "Total Bytes Received");
            _drawCallsRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render, "Standard Draw Calls Count");
            _mainThreadRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "Main Thread");
            _animationRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Animation, "Animators.Update");
        }

        private void OnDestroy()
        {
            _gcAllocatedRecorder.Dispose();
            _bytesSentRecorder.Dispose();
            _bytesReceivedRecorder.Dispose();
            _drawCallsRecorder.Dispose();
            _mainThreadRecorder.Dispose();
            _animationRecorder.Dispose();
        }
    }

    [Serializable]
    public sealed class MultiplayerPerformanceReport
    {
        public string utcTimestamp;
        public int processId;
        public string role;
        public int connectedPlayers;
        public int expectedPlayers;
        public float captureSeconds;
        public int frameCount;
        public float averageFrameMs;
        public float p95FrameMs;
        public float maxFrameMs;
        public float averageDrawCalls;
        public float averageMainThreadMs;
        public float maxMainThreadMs;
        public float averageAnimationMs;
        public float maxAnimationMs;
        public long gcAllocatedBytes;
        public int gcCollections;
        public long allocatedMemoryStart;
        public long allocatedMemoryEnd;
        public long monoMemoryStart;
        public long monoMemoryEnd;
        public long totalBytesSent;
        public long totalBytesReceived;
        public float sentBytesPerSecond;
        public float receivedBytesPerSecond;
        public bool networkCountersAvailable;
        public bool cpuCounterAvailable;
        public bool animationCounterAvailable;
        public bool drawCallsCounterAvailable;
        public int packetDelayMs;
        public int packetJitterMs;
        public int packetLossPercent;
    }
}
