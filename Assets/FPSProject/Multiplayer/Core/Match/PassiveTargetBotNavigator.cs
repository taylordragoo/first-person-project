using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FPSProject.Multiplayer.Core.Match
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(NetworkObject))]
    public sealed class PassiveTargetBotNavigator : NetworkBehaviour
    {
        [SerializeField] private BotExplorationArea explorationArea;

        [SerializeField, Min(0f)] private float agentSpeed = 2.4f;
        [SerializeField, Min(0f)] private float agentAcceleration = 7f;
        [SerializeField, Min(0f)] private float agentAngularSpeed = 300f;
        [SerializeField, Min(0f)] private float agentStoppingDistance = 0.5f;
        [SerializeField, Min(0f)] private float minDestinationDistance = 8f;
        [SerializeField, Min(0f)] private float maxDestinationRadius = 28f;
        [SerializeField, Min(0f)] private float arrivalPauseMin = 0.5f;
        [SerializeField, Min(0f)] private float arrivalPauseMax = 2f;
        [SerializeField, Min(1)] private int candidateAttempts = 10;
        [SerializeField, Min(0f)] private float stuckTimeout = 2.5f;
        [SerializeField, Min(0f)] private float minProgress = 0.3f;

        private NavMeshAgent _agent;

        private enum State
        {
            Idle,
            Moving,
            Reeling
        }

        private State _currentState;
        private float _pauseTimer;
        private float _reelTimer;
        private float _stuckTimer;
        private Vector3 _lastPosition;
        private Vector3 _lastDestination;
        private bool _hasLastDestination;
        private bool _isStandalone;

        public bool IsNavigating => _currentState == State.Moving;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();

            if (_agent != null)
            {
                _agent.speed = agentSpeed;
                _agent.acceleration = agentAcceleration;
                _agent.angularSpeed = agentAngularSpeed;
                _agent.stoppingDistance = agentStoppingDistance;
                _agent.autoBraking = true;
                _agent.enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            BeginNavigation();
        }

        public void InitializeStandalone(BotExplorationArea area)
        {
            if (IsSpawned || (NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening)) return;

            _isStandalone = true;
            explorationArea = area;
            BeginNavigation();
        }

        private void BeginNavigation()
        {

            if (_agent != null)
            {
                _agent.enabled = true;
                WarpToValidPosition();
            }

            ResolveExplorationArea();
            _currentState = State.Idle;
            _pauseTimer = Random.Range(arrivalPauseMin, arrivalPauseMax);
        }

        public override void OnNetworkDespawn()
        {
            if (_agent != null)
            {
                if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false;
            }

            _currentState = State.Idle;
        }

        private void Update()
        {
            if ((!IsServer && !_isStandalone) || _agent == null || !_agent.enabled
                || !_agent.isOnNavMesh
                || !gameObject.activeInHierarchy) return;

            switch (_currentState)
            {
                case State.Idle:
                    TickIdle();
                    break;
                case State.Moving:
                    TickMoving();
                    break;
                case State.Reeling:
                    TickReeling();
                    break;
            }
        }

        private void TickIdle()
        {
            _agent.isStopped = true;
            _pauseTimer -= Time.deltaTime;
            if (_pauseTimer > 0f) return;

            if (PickDestination())
            {
                _currentState = State.Moving;
                _stuckTimer = 0f;
                _lastPosition = transform.position;
            }
            else
            {
                _currentState = State.Reeling;
                _reelTimer = 0.25f;
            }
        }

        private void TickMoving()
        {
            _agent.isStopped = false;

            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
            {
                OnArrival();
                return;
            }

            if (_agent.pathPending) return;

            if (_agent.isPathStale || (_agent.pathStatus == NavMeshPathStatus.PathPartial && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f))
            {
                OnArrival();
                return;
            }

            if (CheckStuck())
            {
                _currentState = State.Reeling;
                _reelTimer = 0.25f;
            }
        }

        private void TickReeling()
        {
            if (_agent != null) _agent.isStopped = true;
            _reelTimer -= Time.deltaTime;
            if (_reelTimer > 0f) return;

            if (PickDestination())
            {
                _currentState = State.Moving;
                _stuckTimer = 0f;
                _lastPosition = transform.position;
            }
            else
            {
                _currentState = State.Idle;
                _pauseTimer = Random.Range(arrivalPauseMin, arrivalPauseMax);
            }
        }

        private void OnArrival()
        {
            if (_agent != null)
            {
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }

            _currentState = State.Idle;
            _pauseTimer = Random.Range(arrivalPauseMin, arrivalPauseMax);
        }

        private bool PickDestination()
        {
            if (explorationArea == null) return false;

            NavMeshQueryFilter queryFilter = BuildQueryFilter();
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit originHit, 2f,
                    queryFilter)) return false;

            for (int i = 0; i < candidateAttempts; i++)
            {
                Vector3 candidate = explorationArea.GetRandomPoint();
                candidate.y = originHit.position.y;

                if (Vector3.Distance(candidate, originHit.position) < minDestinationDistance) continue;
                if (Vector3.Distance(candidate, originHit.position) > maxDestinationRadius) continue;
                if (_hasLastDestination && Vector3.Distance(candidate, _lastDestination) < 0.25f) continue;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f,
                        queryFilter)) continue;

                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(originHit.position, hit.position, queryFilter,
                        path)) continue;
                if (path.status != NavMeshPathStatus.PathComplete) continue;

                if (!_agent.SetDestination(hit.position)) continue;

                _lastDestination = hit.position;
                _hasLastDestination = true;
                return true;
            }

            return false;
        }

        private bool CheckStuck()
        {
            float moved = Vector3.Distance(transform.position, _lastPosition);
            if (moved < minProgress)
                _stuckTimer += Time.deltaTime;
            else
                _stuckTimer = 0f;

            _lastPosition = transform.position;
            return _stuckTimer >= stuckTimeout;
        }

        private void WarpToValidPosition()
        {
            if (_agent == null) return;

            Vector3 position = transform.position;
            NavMeshQueryFilter queryFilter = BuildQueryFilter();
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, queryFilter))
            {
                Debug.LogError($"[{nameof(PassiveTargetBotNavigator)}] Could not sample "
                    + $"agent type {_agent.agentTypeID} near {position}.", this);
                return;
            }

            if (!_agent.Warp(hit.position))
                Debug.LogError($"[{nameof(PassiveTargetBotNavigator)}] NavMeshAgent.Warp "
                    + $"failed for agent type {_agent.agentTypeID} at {hit.position}.", this);
        }

        private NavMeshQueryFilter BuildQueryFilter()
        {
            return new NavMeshQueryFilter
            {
                agentTypeID = _agent.agentTypeID,
                areaMask = _agent.areaMask
            };
        }

        private void ResolveExplorationArea()
        {
            if (explorationArea != null) return;

            explorationArea = GetComponentInParent<BotExplorationArea>();
            if (explorationArea != null) return;

            string activeMapName = TeamDeathmatchManager.Instance != null
                ? TeamDeathmatchManager.Instance.ActiveMap.Value.ToString()
                : string.Empty;

            if (string.IsNullOrEmpty(activeMapName)) return;

            BotExplorationArea[] areas = FindObjectsByType<BotExplorationArea>(FindObjectsInactive.Include);
            foreach (BotExplorationArea area in areas)
            {
                if (area.IsActiveForMap(activeMapName))
                {
                    explorationArea = area;
                    return;
                }
            }
        }

        private void OnDisable()
        {
            if (!_isStandalone || _agent == null) return;
            if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            _agent.enabled = false;
            _currentState = State.Idle;
        }
    }
}
