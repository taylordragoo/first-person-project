using FPSProject.Combat.Runtime;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Health
{
    /// <summary>
    /// Forwards damage from an animated bone collider to the bot's shared health receiver.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotDamageHitbox : MonoBehaviour, IDamageable, IImpactDecalSuppressor
    {
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;
        [SerializeField] private bool guaranteedLethal;

        private NetworkHealth _health;

        public float DamageMultiplier => damageMultiplier;
        public bool IsGuaranteedLethal => guaranteedLethal;

        public void Initialize(NetworkHealth health, float multiplier, bool lethal)
        {
            _health = health;
            damageMultiplier = Mathf.Max(0f, multiplier);
            guaranteedLethal = lethal;
        }

        public void ApplyDamage(in DamageInfo damageInfo)
        {
            if (damageInfo.Amount <= 0f) return;
            if (_health == null) _health = GetComponentInParent<NetworkHealth>();
            if (_health == null) return;

            float resolvedAmount = damageInfo.Amount * damageMultiplier;
            if (guaranteedLethal && !_health.IsDead)
                resolvedAmount = Mathf.Max(resolvedAmount, _health.CurrentHealth.Value);

            var scaledDamage = new DamageInfo(
                resolvedAmount,
                damageInfo.HitPoint,
                damageInfo.HitNormal,
                damageInfo.TravelDirection,
                damageInfo.InstigatorOwner,
                damageInfo.SourceWeapon);
            _health.ApplyDamage(scaledDamage);
        }
    }
}
