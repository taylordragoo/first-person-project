using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Bootstrap;
using FPSProject.Multiplayer.Core.Health;
using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Match
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkHealth))]
    public sealed class NetworkTeamMember : NetworkBehaviour, IMatchTeamProvider
    {
        [SerializeField] private NetworkHealth networkHealth;

        public NetworkVariable<MatchTeam> Team = new NetworkVariable<MatchTeam>(
            MatchTeam.Unassigned, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public MatchTeam TeamValue => Team.Value;

        private void Awake()
        {
            if (networkHealth == null) networkHealth = GetComponent<NetworkHealth>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            MultiplayerSceneLauncher launcher = MultiplayerSceneLauncher.Instance;
            if (launcher != null && launcher.TryGetAssignedTeam(OwnerClientId,
                    out MatchTeam assignedTeam))
            {
                Team.Value = assignedTeam;
            }

            if (networkHealth != null) networkHealth.OnKilled += HandleKilled;
        }

        public override void OnNetworkDespawn()
        {
            if (networkHealth != null) networkHealth.OnKilled -= HandleKilled;
        }

        private void HandleKilled(DamageInfo damageInfo)
        {
            if (!IsServer) return;
            TeamDeathmatchManager.Instance?.ServerRecordElimination(
                Team.Value, damageInfo.InstigatorOwner);
        }
    }
}
