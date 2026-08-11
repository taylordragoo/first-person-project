using UnityEngine;

namespace FPSProject.Combat.Runtime
{
    /// <summary>
    /// Marks a collider as physical gameplay geometry that weapon queries should pass through.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatRaycastPassthrough : MonoBehaviour
    {
    }

    public static class CombatRaycastPolicy
    {
        public static bool ShouldSkip(Collider collider)
        {
            return collider != null
                && collider.GetComponent<CombatRaycastPassthrough>() != null;
        }
    }
}
