using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSProject.Multiplayer.Core.Diagnostics
{
    /// <summary>
    /// Runtime-only wireframe visualization of the bot's real damage colliders.
    /// F9 toggles the overlay globally for every bot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotHitboxDebugVisualizer : MonoBehaviour
    {
        private const int CircleSegments = 24;
        private const int HemisphereSegments = 6;
        private const int MeridianCount = 8;

        private static readonly Color HeadColor = new Color(1f, 0.15f, 0.1f, 1f);
        private static readonly Color TorsoColor = new Color(0f, 0.9f, 1f, 1f);
        private static readonly Color LimbColor = new Color(1f, 0.8f, 0f, 1f);

        private static readonly HashSet<BotHitboxDebugVisualizer> Instances =
            new HashSet<BotHitboxDebugVisualizer>();
        private static Material _headMaterial;
        private static Material _torsoMaterial;
        private static Material _limbMaterial;
        private static int _lastToggleFrame = -1;

        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>(12);
        private readonly List<Mesh> _meshes = new List<Mesh>(12);
        private bool _initialized;

        public static bool IsVisible { get; private set; }
        public int VisualCount => _renderers.Count;
        public int VisibleVisualCount
        {
            get
            {
                int count = 0;
                foreach (MeshRenderer renderer in _renderers)
                {
                    if (renderer != null && renderer.enabled) count++;
                }
                return count;
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F9) || _lastToggleFrame == Time.frameCount)
            {
                return;
            }

            _lastToggleFrame = Time.frameCount;
            SetVisible(!IsVisible);
            Debug.Log($"[Bot hitboxes] {(IsVisible ? "ON" : "OFF")} (F9)");
        }

        public void Initialize(IReadOnlyList<Collider> hitboxColliders)
        {
            if (_initialized || hitboxColliders == null) return;
            _initialized = true;
            Instances.Add(this);

            for (int index = 0; index < hitboxColliders.Count; index++)
            {
                Collider collider = hitboxColliders[index];
                if (collider == null) continue;

                Mesh mesh = BuildWireMesh(collider);
                if (mesh == null) continue;

                var visual = new GameObject($"{collider.name}_HitboxDebug");
                visual.transform.SetParent(collider.transform, false);
                var filter = visual.AddComponent<MeshFilter>();
                var renderer = visual.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = GetMaterial(collider.GetComponent<BotDamageHitbox>());
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = IsVisible;

                _meshes.Add(mesh);
                _renderers.Add(renderer);
            }
        }

        public static void SetVisible(bool visible)
        {
            IsVisible = visible;
            foreach (BotHitboxDebugVisualizer visualizer in Instances)
            {
                if (visualizer != null) visualizer.SetLocalVisibility(visible);
            }
        }

        private void SetLocalVisibility(bool visible)
        {
            foreach (MeshRenderer renderer in _renderers)
            {
                if (renderer != null) renderer.enabled = visible;
            }
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
            foreach (Mesh mesh in _meshes)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }

        private static Material GetMaterial(BotDamageHitbox hitbox)
        {
            if (hitbox != null && hitbox.IsGuaranteedLethal)
                return _headMaterial ??= CreateMaterial(HeadColor);
            if (hitbox != null && hitbox.DamageMultiplier >= 0.99f)
                return _torsoMaterial ??= CreateMaterial(TorsoColor);
            return _limbMaterial ??= CreateMaterial(LimbColor);
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("FPSProject/Debug/Hitbox Overlay");
            if (shader == null)
            {
                Debug.LogError("[Bot hitboxes] Missing debug overlay shader.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            var material = new Material(shader)
            {
                color = color,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_BaseColor", color);
            return material;
        }

        private static Mesh BuildWireMesh(Collider collider)
        {
            var vertices = new List<Vector3>(512);
            if (collider is CapsuleCollider capsule)
                BuildCapsule(vertices, capsule);
            else if (collider is SphereCollider sphere)
                BuildSphere(vertices, sphere.center, sphere.radius);
            else
                return null;

            var indices = new int[vertices.Count];
            for (int index = 0; index < indices.Length; index++) indices[index] = index;

            var mesh = new Mesh
            {
                name = $"{collider.name}_HitboxDebugMesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildSphere(List<Vector3> vertices, Vector3 center, float radius)
        {
            AddCircle(vertices, center, Vector3.right, Vector3.up, radius);
            AddCircle(vertices, center, Vector3.right, Vector3.forward, radius);
            AddCircle(vertices, center, Vector3.up, Vector3.forward, radius);
        }

        private static void BuildCapsule(List<Vector3> vertices, CapsuleCollider capsule)
        {
            GetAxes(capsule.direction, out Vector3 axis, out Vector3 radialA,
                out Vector3 radialB);
            float radius = capsule.radius;
            float cylinderHalf = Mathf.Max(0f, capsule.height * 0.5f - radius);
            Vector3 bottom = capsule.center - axis * cylinderHalf;
            Vector3 top = capsule.center + axis * cylinderHalf;

            AddCircle(vertices, bottom, radialA, radialB, radius);
            AddCircle(vertices, top, radialA, radialB, radius);

            for (int meridian = 0; meridian < MeridianCount; meridian++)
            {
                float angle = meridian * Mathf.PI * 2f / MeridianCount;
                Vector3 radial = radialA * Mathf.Cos(angle) + radialB * Mathf.Sin(angle);
                AddLine(vertices, bottom + radial * radius, top + radial * radius);

                Vector3 previousTop = top + radial * radius;
                Vector3 previousBottom = bottom + radial * radius;
                for (int segment = 1; segment <= HemisphereSegments; segment++)
                {
                    float arc = segment * Mathf.PI * 0.5f / HemisphereSegments;
                    Vector3 nextTop = top + radial * (Mathf.Cos(arc) * radius)
                        + axis * (Mathf.Sin(arc) * radius);
                    Vector3 nextBottom = bottom + radial * (Mathf.Cos(arc) * radius)
                        - axis * (Mathf.Sin(arc) * radius);
                    AddLine(vertices, previousTop, nextTop);
                    AddLine(vertices, previousBottom, nextBottom);
                    previousTop = nextTop;
                    previousBottom = nextBottom;
                }
            }
        }

        private static void AddCircle(List<Vector3> vertices, Vector3 center,
            Vector3 axisA, Vector3 axisB, float radius)
        {
            for (int segment = 0; segment < CircleSegments; segment++)
            {
                float angleA = segment * Mathf.PI * 2f / CircleSegments;
                float angleB = (segment + 1) * Mathf.PI * 2f / CircleSegments;
                AddLine(vertices,
                    center + (axisA * Mathf.Cos(angleA) + axisB * Mathf.Sin(angleA)) * radius,
                    center + (axisA * Mathf.Cos(angleB) + axisB * Mathf.Sin(angleB)) * radius);
            }
        }

        private static void AddLine(List<Vector3> vertices, Vector3 start, Vector3 end)
        {
            vertices.Add(start);
            vertices.Add(end);
        }

        private static void GetAxes(int direction, out Vector3 axis,
            out Vector3 radialA, out Vector3 radialB)
        {
            if (direction == 0)
            {
                axis = Vector3.right;
                radialA = Vector3.up;
                radialB = Vector3.forward;
            }
            else if (direction == 2)
            {
                axis = Vector3.forward;
                radialA = Vector3.right;
                radialB = Vector3.up;
            }
            else
            {
                axis = Vector3.up;
                radialA = Vector3.right;
                radialB = Vector3.forward;
            }
        }
    }
}
