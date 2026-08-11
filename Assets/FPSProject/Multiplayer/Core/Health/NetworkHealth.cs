using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Match;
using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Health
{
    [DisallowMultipleComponent]
    public class NetworkHealth : NetworkBehaviour, IDamageable, IImpactDecalSuppressor
    {
        [SerializeField] private float maxHealth = 100f;

        public NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float MaxHealth => maxHealth;
        public bool IsDead => CurrentHealth.Value <= 0f;
        public bool HasStandaloneAuthority => !IsSpawned
            && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);

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

        /// <summary>
        /// Resets this health receiver for a scene-local bot. Standalone authority is available
        /// only while no network session is listening, so an unspawned network prefab can be
        /// reused safely by Operations without weakening the server-authoritative path.
        /// </summary>
        public void InitializeStandalone()
        {
            if (!HasStandaloneAuthority) return;
            CurrentHealth.Value = maxHealth;
        }

        public void ApplyDamage(in DamageInfo damageInfo)
        {
            if (!IsServer && !HasStandaloneAuthority) return;
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
