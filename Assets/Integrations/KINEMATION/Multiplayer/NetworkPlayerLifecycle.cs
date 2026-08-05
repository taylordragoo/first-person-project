using System.Collections;
using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    [DisallowMultipleComponent]
    public class NetworkPlayerLifecycle : NetworkBehaviour
    {
        [SerializeField] private NetworkHealth networkHealth;
        [SerializeField] private NetworkWeaponState weaponState;
        [SerializeField] private NetworkCasPlayer networkCasPlayer;
        [SerializeField] private NetworkWeaponShotRouter shotRouter;
        [SerializeField] private float respawnDelay = 3f;

        private CharacterController _characterController;
        private Collider[] _allColliders;
        private GameObject _tacticalPresentationRoot;
        private Coroutine _respawnCoroutine;
        private Coroutine _initialSpawnCoroutine;

        public bool IsDead => networkHealth != null && networkHealth.IsDead;

        private void Awake()
        {
            if (networkHealth == null) networkHealth = GetComponent<NetworkHealth>();
            if (weaponState == null) weaponState = GetComponent<NetworkWeaponState>();
            if (networkCasPlayer == null) networkCasPlayer = GetComponent<NetworkCasPlayer>();
            if (shotRouter == null) shotRouter = GetComponent<NetworkWeaponShotRouter>();
        }

        public override void OnNetworkSpawn()
        {
            _characterController = GetComponent<CharacterController>();
            _allColliders = GetComponentsInChildren<Collider>(true);

            Transform tacticalChild = transform.Find("Tactical Presentation");
            if (tacticalChild != null)
                _tacticalPresentationRoot = tacticalChild.gameObject;

            if (networkHealth != null)
            {
                networkHealth.OnDeath += HandleDeath;
                networkHealth.OnRespawn += HandleRespawn;
            }

            if (IsServer)
                _initialSpawnCoroutine = StartCoroutine(InitialSpawnSequence());
        }

        public override void OnNetworkDespawn()
        {
            if (networkHealth != null)
            {
                networkHealth.OnDeath -= HandleDeath;
                networkHealth.OnRespawn -= HandleRespawn;
            }

            if (_respawnCoroutine != null)
            {
                StopCoroutine(_respawnCoroutine);
                _respawnCoroutine = null;
            }

            if (_initialSpawnCoroutine != null)
            {
                StopCoroutine(_initialSpawnCoroutine);
                _initialSpawnCoroutine = null;
            }
        }

        private void HandleDeath()
        {
            if (!IsServer) return;

            if (weaponState != null)
            {
                weaponState.ServerSetReloadState(ReloadState.None);
                weaponState.ServerSetLifeState(PlayerLifeState.Dead);
            }

            CancelFiringClientRpc();
            DisablePlayerClientRpc();

            _respawnCoroutine = StartCoroutine(RespawnSequence());
        }

        private void HandleRespawn()
        {
            if (!IsServer) return;
        }

        [ClientRpc]
        private void CancelFiringClientRpc()
        {
            if (shotRouter != null)
                shotRouter.ClearPredictedShots();

            if (networkCasPlayer != null && networkCasPlayer.TacticalPlayer != null)
            {
                var presentation = networkCasPlayer.TacticalPlayer.GetActiveNetworkWeaponPresentation();
                presentation?.StopNetworkFiring();
            }
        }

        [ClientRpc]
        private void DisablePlayerClientRpc()
        {
            SetPlayerEnabled(false);
        }

        [ClientRpc]
        private void EnablePlayerClientRpc()
        {
            SetPlayerEnabled(true);
        }

        private void SetPlayerEnabled(bool enabled)
        {
            if (_allColliders != null)
            {
                foreach (var col in _allColliders)
                {
                    if (col != null && col != _characterController)
                        col.enabled = enabled;
                }
            }

            // Remote proxies must never run CharacterController movement/collision locally.
            if (_characterController != null)
                _characterController.enabled = enabled
                    && networkCasPlayer != null
                    && networkCasPlayer.IsOwner;

            if (_tacticalPresentationRoot != null)
                _tacticalPresentationRoot.SetActive(enabled);
        }

        private IEnumerator RespawnSequence()
        {
            yield return new WaitForSeconds(respawnDelay);

            if (!IsServer || !IsSpawned) yield break;

            Vector3 spawnPosition = SelectRespawnPosition();

            if (weaponState != null)
                weaponState.ServerResetForRespawn(1);

            if (networkHealth != null)
                networkHealth.ServerRespawn();

            ClearPredictedShotsClientRpc();

            RespawnAtPositionClientRpc(spawnPosition);
            _respawnCoroutine = null;
        }

        private IEnumerator InitialSpawnSequence()
        {
            // Wait until all NetworkBehaviours on the player have completed OnNetworkSpawn,
            // then place the player through the same owner/proxy-safe path as a respawn.
            yield return null;

            if (IsServer && IsSpawned)
                RespawnAtPositionClientRpc(SelectRespawnPosition());

            _initialSpawnCoroutine = null;
        }

        [ClientRpc]
        private void RespawnAtPositionClientRpc(Vector3 spawnPosition)
        {
            if (networkCasPlayer != null)
                networkCasPlayer.NotifyRespawn(spawnPosition);

            SetPlayerEnabled(true);
        }

        [ClientRpc]
        private void ClearPredictedShotsClientRpc()
        {
            if (shotRouter != null)
                shotRouter.ClearPredictedShots();
        }

        private Vector3 SelectRespawnPosition()
        {
            var spawnPoints = new List<NetworkSpawnPoint>();
            var livingPositions = new List<Vector3>();

            var allSpawnPoints = FindObjectsByType<NetworkSpawnPoint>(FindObjectsSortMode.None);
            foreach (var sp in allSpawnPoints)
            {
                if (sp != null && sp.isActiveAndEnabled)
                    spawnPoints.Add(sp);
            }

            if (NetworkManager != null)
            {
                foreach (var kvp in NetworkManager.SpawnManager.SpawnedObjects)
                {
                    var otherHealth = kvp.Value.GetComponent<NetworkHealth>();
                    if (otherHealth != null && !otherHealth.IsDead
                        && kvp.Value.NetworkObjectId != NetworkObjectId)
                    {
                        livingPositions.Add(kvp.Value.transform.position);
                    }
                }
            }

            NetworkSpawnPoint selected = NetworkSpawnPoint.SelectSpawnPoint(
                spawnPoints, livingPositions);

            if (selected != null)
                return selected.Position;

            return transform.position + Random.insideUnitSphere * 3f;
        }
    }
}
