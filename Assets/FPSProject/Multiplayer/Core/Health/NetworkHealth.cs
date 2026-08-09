using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Match;
using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Health
{
    [DisallowMultipleComponent]
    public class NetworkHealth : NetworkBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        public NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float MaxHealth => maxHealth;
        public bool IsDead => CurrentHealth.Value <= 0f;

        public event System.Action OnDeath;
        public event System.Action<DamageInfo> OnKilled;
        public event System.Action OnRespawn;
        public event System.Action<float, float> OnHealthChanged;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentHealth.Value = maxHealth;
            }
        }

        public void ApplyDamage(in DamageInfo damageInfo)
        {
            if (!IsServer) return;
            if (IsDead) return;

            if (damageInfo.InstigatorOwner == gameObject) return;
            if (MatchTeamResolver.AreFriendly(damageInfo.InstigatorOwner, gameObject)) return;

            float newHealth = Mathf.Max(0f, CurrentHealth.Value - damageInfo.Amount);
            CurrentHealth.Value = newHealth;

            OnHealthChanged?.Invoke(newHealth, maxHealth);

            if (newHealth <= 0f)
            {
                OnKilled?.Invoke(damageInfo);
                OnDeath?.Invoke();
            }
        }

        public void ServerRespawn()
        {
            if (!IsServer) return;
            CurrentHealth.Value = maxHealth;
            OnRespawn?.Invoke();
            OnHealthChanged?.Invoke(maxHealth, maxHealth);
        }
    }
}
