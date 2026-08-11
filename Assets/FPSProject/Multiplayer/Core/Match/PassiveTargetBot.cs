using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Health;
using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Match
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(NetworkHealth))]
    public sealed class PassiveTargetBot : NetworkBehaviour, IMatchTeamProvider
    {
        [SerializeField] private NetworkHealth networkHealth;
        [SerializeField] private Renderer[] renderers;

        public NetworkVariable<MatchTeam> Team = new NetworkVariable<MatchTeam>(
            MatchTeam.Unassigned, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<byte> SpawnSlot = new NetworkVariable<byte>(
            byte.MaxValue, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public MatchTeam TeamValue => Team.Value;
        public bool IsStandalone { get; private set; }

        public event System.Action<PassiveTargetBot, DamageInfo> OnStandaloneKilled;

        private void Awake()
        {
            if (networkHealth == null) networkHealth = GetComponent<NetworkHealth>();
            ForceCharacterPartsVisible();
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);
        }

        public override void OnNetworkSpawn()
        {
            Team.OnValueChanged += HandleTeamChanged;
            ForceCharacterPartsVisible();
            ApplyTeamTint(Team.Value);
            if (IsServer && networkHealth != null) networkHealth.OnKilled += HandleKilled;
        }

        public override void OnNetworkDespawn()
        {
            Team.OnValueChanged -= HandleTeamChanged;
            if (networkHealth != null) networkHealth.OnKilled -= HandleKilled;
        }

        public void ServerInitialize(MatchTeam team, int spawnSlot)
        {
            if (!IsServer || !IsSpawned) return;
            Team.Value = team;
            SpawnSlot.Value = (byte)Mathf.Clamp(spawnSlot, 0,
                MatchLaunchSettings.MaxHumansPerTeam - 1);
        }

        public void InitializeStandalone(MatchTeam team, int spawnSlot)
        {
            if (IsSpawned || (NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening)) return;

            IsStandalone = true;
            Team.Value = team;
            SpawnSlot.Value = (byte)Mathf.Clamp(spawnSlot, 0, byte.MaxValue - 1);
            ApplyTeamTint(team);

            if (networkHealth != null)
            {
                networkHealth.OnKilled -= HandleKilled;
                networkHealth.OnKilled += HandleKilled;
            }
        }

        private void HandleKilled(DamageInfo damageInfo)
        {
            if (IsServer)
            {
                TeamDeathmatchManager.Instance?.ServerHandleBotKilled(this, damageInfo);
                return;
            }

            if (IsStandalone) OnStandaloneKilled?.Invoke(this, damageInfo);
        }

        private void OnDisable()
        {
            if (IsStandalone && networkHealth != null)
                networkHealth.OnKilled -= HandleKilled;
        }

        private void HandleTeamChanged(MatchTeam previous, MatchTeam current)
        {
            ApplyTeamTint(current);
        }

        private void ApplyTeamTint(MatchTeam team)
        {
            Color tint = team == MatchTeam.Bravo
                ? new Color(1f, 0.28f, 0.05f, 1f)
                : new Color(0.05f, 0.72f, 1f, 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);

            if (renderers == null) return;
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer != null) targetRenderer.SetPropertyBlock(block);
            }
        }

        private void ForceCharacterPartsVisible()
        {
            foreach (SkinnedMeshRenderer candidate in
                     GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.name != "SK_Head_01a.001"
                    && candidate.name != "SK_Helmet_01a.001"
                    && candidate.name != "Headset_Helmet_Fixed01.001")
                {
                    continue;
                }

                Transform current = candidate.transform;
                while (current != null && current != transform)
                {
                    current.gameObject.SetActive(true);
                    current = current.parent;
                }

                candidate.enabled = true;
                candidate.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }
    }
}
