using System;
using System.Collections.Generic;
using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
using Unity.Netcode;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Scene-local Operations bootstrap. It reuses the multiplayer bot prefab's navigation and
    /// presentation, but never starts a network session or spawns a NetworkObject.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class OperationsBotSpawner : MonoBehaviour
    {
        private const int GroundHitCapacity = 32;

        [SerializeField] private GameObject passiveBotPrefab;
        [SerializeField] private Transform spawnRoot;
        [SerializeField] private string spawnRootName = MatchSpawnCatalog.Dust2SpawnRootName;
        [SerializeField] private MatchTeam enemyTeam = MatchTeam.Bravo;
        [SerializeField, Min(0f)] private float explorationPadding = 20f;
        [SerializeField, Min(0f)] private float botGroundClearance = 0.01f;
        [SerializeField, Min(0.1f)] private float botGroundProbeHeight = 0.5f;
        [SerializeField, Min(1f)] private float botGroundProbeDistance = 10f;

        private readonly List<PassiveTargetBot> _spawnedBots =
            new List<PassiveTargetBot>();
        private readonly RaycastHit[] _groundHits = new RaycastHit[GroundHitCapacity];
        private BotExplorationArea _explorationArea;

        public int SpawnedBotCount => _spawnedBots.Count;
        public IReadOnlyList<PassiveTargetBot> SpawnedBots => _spawnedBots;

        private void Start()
        {
            SpawnAll();
        }

        public void SpawnAll()
        {
            if (_spawnedBots.Count > 0) return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning($"[{nameof(OperationsBotSpawner)}] Skipping standalone bots "
                    + "because a network session is already listening.", this);
                return;
            }

            if (passiveBotPrefab == null)
            {
                Debug.LogError($"[{nameof(OperationsBotSpawner)}] Passive bot prefab is not assigned.",
                    this);
                return;
            }

            ResolveSpawnRoot();
            if (spawnRoot == null)
            {
                Debug.LogError($"[{nameof(OperationsBotSpawner)}] Could not find spawn root "
                    + $"'{spawnRootName}'.", this);
                return;
            }

            NetworkSpawnPoint[] spawnPoints =
                spawnRoot.GetComponentsInChildren<NetworkSpawnPoint>(true);
            Array.Sort(spawnPoints, CompareSpawnPoints);
            if (spawnPoints.Length == 0)
            {
                Debug.LogError($"[{nameof(OperationsBotSpawner)}] Spawn root has no spawn points.",
                    this);
                return;
            }

            _explorationArea = BuildExplorationArea(spawnPoints);
            for (int i = 0; i < spawnPoints.Length; i++)
                SpawnBot(spawnPoints[i], i);

            Debug.Log($"[{nameof(OperationsBotSpawner)}] Spawned {_spawnedBots.Count} "
                + "standalone Operations bots.", this);
        }

        private void SpawnBot(NetworkSpawnPoint spawnPoint, int slot)
        {
            GameObject instance = Instantiate(passiveBotPrefab, spawnPoint.Position,
                spawnPoint.Rotation);
            instance.name = $"Operations Bot {slot + 1}";
            instance.transform.position = GetGroundedBotPosition(instance, spawnPoint);

            NetworkHealth health = instance.GetComponent<NetworkHealth>();
            PassiveTargetBot bot = instance.GetComponent<PassiveTargetBot>();
            PassiveTargetBotNavigator navigator =
                instance.GetComponent<PassiveTargetBotNavigator>();
            BotCasPresentationAdapter presentation =
                instance.GetComponent<BotCasPresentationAdapter>();
            BotRagdollController ragdoll =
                instance.GetComponent<BotRagdollController>();
            if (ragdoll == null) ragdoll = instance.AddComponent<BotRagdollController>();
            ragdoll.Initialize();

            if (health == null || bot == null || navigator == null || presentation == null
                || !ragdoll.IsReady)
            {
                Debug.LogError($"[{nameof(OperationsBotSpawner)}] Bot prefab is missing its "
                    + "health, bot, navigation, CAS presentation, or ragdoll rig.", instance);
                Destroy(instance);
                return;
            }

            health.InitializeStandalone();
            bot.InitializeStandalone(enemyTeam, slot);
            bot.OnStandaloneKilled += HandleBotKilled;
            navigator.InitializeStandalone(_explorationArea);
            presentation.InitializeStandalone();
            _spawnedBots.Add(bot);
        }

        private void HandleBotKilled(PassiveTargetBot bot, DamageInfo damageInfo)
        {
            if (bot == null) return;
            bot.OnStandaloneKilled -= HandleBotKilled;
            _spawnedBots.Remove(bot);
            BotRagdollController ragdoll = bot.GetComponent<BotRagdollController>();
            if (ragdoll == null || !ragdoll.Activate(damageInfo))
                Destroy(bot.gameObject);
        }

        private BotExplorationArea BuildExplorationArea(NetworkSpawnPoint[] spawnPoints)
        {
            BotExplorationArea area = GetComponent<BotExplorationArea>();
            if (area == null) area = gameObject.AddComponent<BotExplorationArea>();

            Bounds bounds = new Bounds(spawnPoints[0].Position, Vector3.zero);
            for (int i = 1; i < spawnPoints.Length; i++)
                bounds.Encapsulate(spawnPoints[i].Position);

            bounds.Expand(new Vector3(explorationPadding * 2f, 20f,
                explorationPadding * 2f));
            area.ConfigureWorldBounds(bounds, "Dust2");
            return area;
        }

        private void ResolveSpawnRoot()
        {
            if (spawnRoot != null) return;

            Transform[] candidates = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            foreach (Transform candidate in candidates)
            {
                if (candidate.parent == null && candidate.name == spawnRootName)
                {
                    spawnRoot = candidate;
                    return;
                }
            }
        }

        private Vector3 GetGroundedBotPosition(GameObject instance,
            NetworkSpawnPoint spawnPoint)
        {
            Vector3 origin = spawnPoint.Position + Vector3.up * botGroundProbeHeight;
            float distance = botGroundProbeHeight + botGroundProbeDistance;
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits,
                distance, ~0, QueryTriggerInteraction.Ignore);

            bool foundGround = false;
            float closestDistance = float.MaxValue;
            RaycastHit closestHit = default;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.collider == null || hit.point.y > spawnPoint.Position.y + 0.05f)
                    continue;
                if (hit.collider.transform.IsChildOf(instance.transform)) continue;
                if (hit.collider.GetComponentInParent<NetworkSpawnPoint>() != null) continue;
                if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
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
            else
            {
                CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    Vector3 localBottom = capsule.center
                        - Vector3.up * (capsule.height * 0.5f);
                    colliderBottom = instance.transform.TransformPoint(localBottom).y;
                }
            }

            Vector3 groundedPosition = instance.transform.position;
            groundedPosition.y += closestHit.point.y + botGroundClearance - colliderBottom;
            return groundedPosition;
        }

        private static int CompareSpawnPoints(NetworkSpawnPoint left,
            NetworkSpawnPoint right)
        {
            return ReadTrailingNumber(left != null ? left.name : string.Empty)
                .CompareTo(ReadTrailingNumber(right != null ? right.name : string.Empty));
        }

        private static int ReadTrailingNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return int.MaxValue;
            int separator = value.LastIndexOf(' ');
            return int.TryParse(value.Substring(separator + 1), out int number)
                ? number
                : int.MaxValue;
        }

        private void OnDestroy()
        {
            foreach (PassiveTargetBot bot in _spawnedBots)
            {
                if (bot != null) bot.OnStandaloneKilled -= HandleBotKilled;
            }
            _spawnedBots.Clear();
        }
    }
}
