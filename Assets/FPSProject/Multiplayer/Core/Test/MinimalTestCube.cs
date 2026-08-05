using UnityEngine;
using Unity.Netcode;

namespace FPSProject.Multiplayer.Core.Test
{
    /// <summary>
    /// Minimal NetworkBehaviour used only to verify that the core assembly,
    /// NetworkManager, prefab registration, and spawn pipeline function end to
    /// end. Removed once the real player prefab is wired up.
    /// </summary>
    public class MinimalTestCube : NetworkBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Color ownerColor = new Color(0f, 1f, 0f, 1f);
        [SerializeField] private Color remoteColor = new Color(1f, 0f, 0f, 1f);

        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            ApplyColor(IsOwner ? ownerColor : remoteColor);
            if (IsServer)
            {
                transform.position = new Vector3(
                    Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
            }
        }

        private void ApplyColor(Color color)
        {
            if (_renderer == null) return;
            _block.SetColor(EmissionColor, color * 2f);
            _renderer.SetPropertyBlock(_block);
        }
    }
}