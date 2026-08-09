using System.Collections;
using System.Collections.Generic;
using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Bootstrap;
using FPSProject.Multiplayer.Core.Health;
using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Match
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class TeamDeathmatchManager : NetworkBehaviour
    {
        [SerializeField] private GameObject passiveBotPrefab;
        [SerializeField, Min(0.1f)] private float botRespawnDelay = 3f;
        [SerializeField, Min(0f)] private float botGroundClearance = 0.01f;
        [SerializeField, Min(0.1f)] private float botGroundProbeHeight = 0.5f;
        [SerializeField, Min(1f)] private float botGroundProbeDistance = 10f;
        [SerializeField, Min(0.5f)] private float rosterReconcileCooldown = 2f;
        [SerializeField] private string dust2LevelRootName = "de_dust2";
        [SerializeField] private string officeLevelRootName = "1";

        public NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(
            MatchPhase.Waiting, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<MatchMap> ActiveMap = new NetworkVariable<MatchMap>(
            MatchMap.Dust2, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AlphaScore = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BravoScore = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> RemainingSeconds = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ConfiguredAlphaBotCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ConfiguredBravoBotCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ActiveBotCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ActiveAlphaBotCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ActiveBravoBotCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AlphaHumanCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BravoHumanCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<MatchTeam> WinningTeam = new NetworkVariable<MatchTeam>(
            MatchTeam.Unassigned, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public NetworkVariable<ushort> DefaultWeaponId = new NetworkVariable<ushort>(
            1, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<PassiveTargetBot> _bots = new List<PassiveTargetBot>();
        private double _matchEndTime;
        private Coroutine _reconcileCoroutine;
        private double _lastRosterReconcileTime;
        private RaycastHit[] _groundProbeHits;

        public static TeamDeathmatchManager Instance { get; private set; }
        public GameObject PassiveBotPrefab => passiveBotPrefab;

        private void Awake()
        {
            Instance = this;
            _groundProbeHits = new RaycastHit[16];
        }

        public override void OnNetworkSpawn()
        {
            ActiveMap.OnValueChanged += HandleMapChanged;
            ApplyMapVisibility(ActiveMap.Value);

            if (!IsServer) return;

            MatchLaunchSettings settings = MultiplayerSceneLauncher.Instance != null
                ? MultiplayerSceneLauncher.Instance.CurrentMatchSettings
                : MatchLaunchSettings.Default;
            settings = MatchRules.Sanitize(settings);

            ActiveMap.Value = settings.Map;
            ConfiguredAlphaBotCount.Value = settings.alphaBotCount;
            ConfiguredBravoBotCount.Value = settings.bravoBotCount;
            DefaultWeaponId.Value = settings.DefaultWeaponId;
            AlphaScore.Value = 0;
            BravoScore.Value = 0;
            WinningTeam.Value = MatchTeam.Unassigned;
            RemainingSeconds.Value = settings.durationSeconds;
            _matchEndTime = ServerNow + settings.durationSeconds;
            Phase.Value = MatchPhase.Running;
            ApplyMapVisibility(settings.Map);

            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback += HandleClientRosterChanged;
                NetworkManager.OnClientDisconnectCallback += HandleClientRosterChanged;
            }

            _lastRosterReconcileTime = ServerNow - rosterReconcileCooldown;
            RequestBotReconcile();
        }

        public override void OnNetworkDespawn()
        {
            ActiveMap.OnValueChanged -= HandleMapChanged;
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientRosterChanged;
                NetworkManager.OnClientDisconnectCallback -= HandleClientRosterChanged;
            }

            if (_reconcileCoroutine != null)
            {
                StopCoroutine(_reconcileCoroutine);
                _reconcileCoroutine = null;
            }

            _bots.Clear();
            if (Instance == this) Instance = null;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsServer || Phase.Value != MatchPhase.Running) return;

            int remaining = Mathf.Max(0, Mathf.CeilToInt((float)(_matchEndTime - ServerNow)));
            if (remaining != RemainingSeconds.Value) RemainingSeconds.Value = remaining;
            if (remaining > 0) return;

            WinningTeam.Value = MatchRules.GetWinner(AlphaScore.Value, BravoScore.Value);
            Phase.Value = MatchPhase.Finished;
        }

        public void ServerRecordElimination(MatchTeam victimTeam, GameObject instigator)
        {
            if (!IsServer || Phase.Value != MatchPhase.Running) return;
            if (!MatchTeamResolver.TryGetTeam(instigator, out MatchTeam attackerTeam)) return;
            if (!MatchRules.IsPlayableTeam(attackerTeam) || attackerTeam == victimTeam) return;

            if (attackerTeam == MatchTeam.Alpha)
                AlphaScore.Value++;
            else
                BravoScore.Value++;
        }

        public void ServerHandleBotKilled(PassiveTargetBot bot, DamageInfo damageInfo)
        {
            if (!IsServer || bot == null) return;
            MatchTeam victimTeam = bot.Team.Value;
            ServerRecordElimination(victimTeam, damageInfo.InstigatorOwner);
            _bots.Remove(bot);
            RefreshActiveBotCounts();
            StartCoroutine(RecycleBotAfterDelay(bot));
        }

        public MatchTeam GetLocalPlayerTeam()
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
                return MatchTeam.Unassigned;

            NetworkObject player = NetworkManager.SpawnManager.GetLocalPlayerObject();
            NetworkTeamMember member = player != null
                ? player.GetComponent<NetworkTeamMember>()
                : null;
            return member != null ? member.Team.Value : MatchTeam.Unassigned;
        }

        private IEnumerator RecycleBotAfterDelay(PassiveTargetBot bot)
        {
            yield return null;
            if (bot != null && bot.NetworkObject != null && bot.NetworkObject.IsSpawned)
                bot.NetworkObject.Despawn(true);

            yield return new WaitForSecondsRealtime(botRespawnDelay);
            if (IsServer && Phase.Value == MatchPhase.Running) ReconcileBots();
        }

        private void HandleClientRosterChanged(ulong clientId)
        {
            RequestBotReconcile();
        }

        private void RequestBotReconcile()
        {
            if (!IsServer || _reconcileCoroutine != null) return;
            double now = ServerNow;
            if (now - _lastRosterReconcileTime < rosterReconcileCooldown) return;
            _lastRosterReconcileTime = now;
            _reconcileCoroutine = StartCoroutine(ReconcileBotsNextFrame());
        }

        private IEnumerator ReconcileBotsNextFrame()
        {
            yield return null;
            yield return null;
            _reconcileCoroutine = null;
            if (IsServer && Phase.Value == MatchPhase.Running) ReconcileBots();
        }

        private void ReconcileBots()
        {
            _bots.RemoveAll(bot => bot == null || !bot.IsSpawned);

            MultiplayerSceneLauncher launcher = MultiplayerSceneLauncher.Instance;
            int alphaHumans = launcher != null
                ? launcher.GetAssignedHumanCount(MatchTeam.Alpha)
                : 0;
            int bravoHumans = launcher != null
                ? launcher.GetAssignedHumanCount(MatchTeam.Bravo)
                : 0;
            AlphaHumanCount.Value = alphaHumans;
            BravoHumanCount.Value = bravoHumans;

            int alphaTarget = MatchRules.GetBotTarget(
                ConfiguredAlphaBotCount.Value, alphaHumans);
            int bravoTarget = MatchRules.GetBotTarget(
                ConfiguredBravoBotCount.Value, bravoHumans);

            TrimBots(MatchTeam.Alpha, alphaTarget);
            TrimBots(MatchTeam.Bravo, bravoTarget);
            SpawnMissingBots(MatchTeam.Alpha, alphaTarget);
            SpawnMissingBots(MatchTeam.Bravo, bravoTarget);
            RefreshActiveBotCounts();
        }

        private void TrimBots(MatchTeam team, int targetCount)
        {
            int count = CountBots(team);
            for (int i = _bots.Count - 1; i >= 0 && count > targetCount; i--)
            {
                PassiveTargetBot bot = _bots[i];
                if (bot == null || bot.Team.Value != team) continue;
                _bots.RemoveAt(i);
                if (bot.NetworkObject != null && bot.NetworkObject.IsSpawned)
                    bot.NetworkObject.Despawn(true);
                count--;
            }
        }

        private void SpawnMissingBots(MatchTeam team, int targetCount)
        {
            int count = CountBots(team);
            while (count < targetCount)
            {
                if (!TryFindFreeSpawnSlot(team, out int slot)) break;
                if (!SpawnBot(team, slot)) break;
                count++;
            }
        }

        private bool SpawnBot(MatchTeam team, int slot)
        {
            if (passiveBotPrefab == null)
            {
                Debug.LogError("[TeamDeathmatchManager] Passive bot prefab is not assigned.", this);
                return false;
            }

            if (!MatchSpawnCatalog.TryGetPoint(ActiveMap.Value, team, slot,
                    out NetworkSpawnPoint spawnPoint))
            {
                return false;
            }

            GameObject instance = Instantiate(passiveBotPrefab, spawnPoint.Position,
                spawnPoint.Rotation);
            instance.transform.position = GetGroundedBotPosition(instance, spawnPoint);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            PassiveTargetBot bot = instance.GetComponent<PassiveTargetBot>();
            if (networkObject == null || bot == null)
            {
                Destroy(instance);
                return false;
            }

            networkObject.Spawn(true);
            bot.ServerInitialize(team, slot);
            _bots.Add(bot);
            return true;
        }

        private Vector3 GetGroundedBotPosition(GameObject instance,
            NetworkSpawnPoint spawnPoint)
        {
            Vector3 origin = spawnPoint.Position
                + Vector3.up * botGroundProbeHeight;
            float distance = botGroundProbeHeight + botGroundProbeDistance;

            bool foundGround = false;
            float closestDistance = float.MaxValue;
            RaycastHit closestHit = default;

            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _groundProbeHits, distance, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _groundProbeHits[i];
                if (hit.collider == null) continue;
                if (hit.point.y > spawnPoint.Position.y + 0.05f) continue;
                if (hit.collider.transform.IsChildOf(instance.transform)) continue;
                if (hit.collider.GetComponentInParent<NetworkSpawnPoint>() != null)
                    continue;
                if (hit.collider.GetComponentInParent<NetworkObject>() != null)
                    continue;
                if (hit.distance >= closestDistance) continue;

                closestDistance = hit.distance;
                closestHit = hit;
                foundGround = true;
            }

            if (!foundGround) return instance.transform.position;

            float colliderBottom = instance.transform.position.y;
            CharacterController controller = instance.GetComponent<CharacterController>();
            if (controller != null)
            {
                Vector3 localBottom = controller.center
                    - Vector3.up * (controller.height * 0.5f);
                colliderBottom = instance.transform.TransformPoint(localBottom).y;
            }

            Vector3 groundedPosition = instance.transform.position;
            groundedPosition.y += closestHit.point.y + botGroundClearance
                - colliderBottom;
            return groundedPosition;
        }

        private bool TryFindFreeSpawnSlot(MatchTeam team, out int slot)
        {
            MultiplayerSceneLauncher launcher = MultiplayerSceneLauncher.Instance;
            for (int candidate = MatchLaunchSettings.MaxHumansPerTeam - 1;
                 candidate >= 0; candidate--)
            {
                if (launcher != null && launcher.IsHumanSpawnSlotAssigned(team, candidate))
                    continue;

                bool botUsesSlot = false;
                foreach (PassiveTargetBot bot in _bots)
                {
                    if (bot != null && bot.Team.Value == team
                        && bot.SpawnSlot.Value == candidate)
                    {
                        botUsesSlot = true;
                        break;
                    }
                }

                if (botUsesSlot) continue;
                if (!MatchSpawnCatalog.TryGetPoint(ActiveMap.Value, team, candidate, out _))
                    continue;

                slot = candidate;
                return true;
            }

            slot = -1;
            return false;
        }

        private int CountBots(MatchTeam team)
        {
            int count = 0;
            foreach (PassiveTargetBot bot in _bots)
            {
                if (bot != null && bot.IsSpawned && bot.Team.Value == team) count++;
            }
            return count;
        }

        private int CountLiveBots()
        {
            int count = 0;
            foreach (PassiveTargetBot bot in _bots)
            {
                if (bot != null && bot.IsSpawned) count++;
            }
            return count;
        }

        private void RefreshActiveBotCounts()
        {
            ActiveAlphaBotCount.Value = CountBots(MatchTeam.Alpha);
            ActiveBravoBotCount.Value = CountBots(MatchTeam.Bravo);
            ActiveBotCount.Value = CountLiveBots();
        }

        private void HandleMapChanged(MatchMap previous, MatchMap current)
        {
            MatchSpawnCatalog.InvalidateCache();
            ApplyMapVisibility(current);
        }

        private void ApplyMapVisibility(MatchMap map)
        {
            SetRootActive(dust2LevelRootName, map == MatchMap.Dust2);
            SetRootActive(officeLevelRootName, map == MatchMap.Office);
            SetRootActive(MatchSpawnCatalog.Dust2SpawnRootName, map == MatchMap.Dust2);
            SetRootActive(MatchSpawnCatalog.OfficeSpawnRootName, map == MatchMap.Office);
        }

        private static void SetRootActive(string objectName, bool active)
        {
            GameObject root = FindRootIncludingInactive(objectName);
            if (root != null && root.activeSelf != active) root.SetActive(active);
        }

        private static GameObject FindRootIncludingInactive(string objectName)
        {
            Transform[] transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include);
            foreach (Transform candidate in transforms)
            {
                if (candidate.parent == null && candidate.name == objectName)
                    return candidate.gameObject;
            }
            return null;
        }

        private double ServerNow => NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time
            : Time.realtimeSinceStartupAsDouble;
    }
}
