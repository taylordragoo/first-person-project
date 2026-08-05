namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Reusable damage contract. Resolve the first implementation on the hit collider,
    /// then its closest parent, and invoke it at most once per contact.
    /// </summary>
    public interface IDamageable
    {
        void ApplyDamage(in DamageInfo damageInfo);
    }
}
