namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Marker for moving damage targets that should receive transient hit VFX but never a
    /// persistent world decal. Character health components implement this contract so their
    /// colliders cannot leave bullet holes floating behind after the character moves.
    /// </summary>
    public interface IImpactDecalSuppressor
    {
    }

    public static class ImpactDecalPolicy
    {
        public static bool ShouldSpawnDecal(UnityEngine.Collider collider)
        {
            if (collider == null) return true;

            return collider.GetComponent<IImpactDecalSuppressor>() == null
                && collider.GetComponentInParent<IImpactDecalSuppressor>() == null;
        }
    }
}
