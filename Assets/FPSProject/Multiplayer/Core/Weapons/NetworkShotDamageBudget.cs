using System.Collections.Generic;

namespace FPSProject.Multiplayer.Core.Weapons
{
    /// <summary>
    /// Tracks a shot-wide damage cap while deduplicating repeated target resolution only within
    /// the current pellet/contact path. Call <see cref="BeginContactPath"/> before each pellet.
    /// </summary>
    public sealed class NetworkShotDamageBudget<TTarget> where TTarget : class
    {
        private readonly float _damageCap;
        private readonly HashSet<TTarget> _resolvedTargetsThisContact = new HashSet<TTarget>();

        public float TotalDamage { get; private set; }

        public NetworkShotDamageBudget(float damageCap)
        {
            _damageCap = damageCap;
        }

        public void BeginContactPath()
        {
            _resolvedTargetsThisContact.Clear();
        }

        public bool TryReserve(TTarget target, float damage)
        {
            if (target == null || damage <= 0f || float.IsNaN(damage)
                || float.IsInfinity(damage))
            {
                return false;
            }

            if (_resolvedTargetsThisContact.Contains(target)) return false;
            if (TotalDamage + damage > _damageCap) return false;

            _resolvedTargetsThisContact.Add(target);
            TotalDamage += damage;
            return true;
        }
    }
}
