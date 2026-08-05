using System.Collections.Generic;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Health
{
    public class NetworkSpawnPoint : MonoBehaviour
    {
        [SerializeField] private float clearanceRadius = 2f;
        [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0f, 0.3f);

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public float ClearanceRadius => clearanceRadius;

        public static NetworkSpawnPoint SelectSpawnPoint(
            IReadOnlyList<NetworkSpawnPoint> candidates,
            IReadOnlyList<Vector3> livingPlayerPositions,
            int playerMask = ~0)
        {
            if (candidates == null || candidates.Count == 0) return null;

            float bestScore = float.MinValue;
            NetworkSpawnPoint best = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                var sp = candidates[i];
                if (sp == null) continue;

                float score = ScoreSpawnPoint(sp, livingPlayerPositions);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = sp;
                }
            }

            return best ?? candidates[0];
        }

        private static float ScoreSpawnPoint(
            NetworkSpawnPoint sp,
            IReadOnlyList<Vector3> livingPlayerPositions)
        {
            if (livingPlayerPositions == null || livingPlayerPositions.Count == 0)
                return 1000f;

            float minDistSq = float.MaxValue;
            for (int i = 0; i < livingPlayerPositions.Count; i++)
            {
                float dSq = (sp.Position - livingPlayerPositions[i]).sqrMagnitude;
                if (dSq < minDistSq) minDistSq = dSq;
            }

            return minDistSq;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(transform.position, 0.3f);
            Gizmos.DrawWireSphere(transform.position, clearanceRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, transform.forward * 1f);
        }
    }
}
