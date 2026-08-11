using UnityEngine;

namespace FPSProject.Multiplayer.Core.Match
{
    [DisallowMultipleComponent]
    public sealed class BotExplorationArea : MonoBehaviour
    {
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(30f, 10f, 30f);
        [SerializeField] private string mapName;

        public Bounds Bounds => new Bounds(transform.TransformPoint(center), size);

        public string MapName => mapName;

        public void ConfigureWorldBounds(Bounds worldBounds, string activeMapName)
        {
            center = transform.InverseTransformPoint(worldBounds.center);
            size = worldBounds.size;
            mapName = activeMapName;
        }

        public bool IsActiveForMap(string activeMapName)
        {
            return !string.IsNullOrEmpty(mapName)
                && string.Equals(mapName, activeMapName,
                    System.StringComparison.OrdinalIgnoreCase);
        }

        public Vector3 GetRandomPoint()
        {
            Bounds bounds = Bounds;
            return new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                bounds.center.y,
                Random.Range(bounds.min.z, bounds.max.z));
        }

        public bool Contains(Vector3 point)
        {
            Bounds bounds = Bounds;
            return Mathf.Abs(point.x - bounds.center.x) <= bounds.extents.x
                && Mathf.Abs(point.z - bounds.center.z) <= bounds.extents.z;
        }

        private void OnDrawGizmosSelected()
        {
            Bounds bounds = Bounds;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
