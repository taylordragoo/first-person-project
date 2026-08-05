using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Presentation-only API exposed by networked Tactical weapons. These methods drive fire,
    /// reload, ammunition, and fire-mode presentation without independently scheduling fire or
    /// deciding ammunition. The host owns authoritative state; presentation components only
    /// replay what the host has accepted.
    /// </summary>
    public interface INetworkTacticalWeaponPresentation
    {
        /// <summary>Play one fire presentation frame (muzzle flash, casing, animation, audio, recoil).</summary>
        void PlayNetworkFirePresentation();

        /// <summary>Play the reload-start presentation animation/sound. Does not change ammo.</summary>
        void PlayNetworkReloadPresentation();

        /// <summary>Play the reload-end / per-shell reload loop presentation. Does not change ammo.</summary>
        void PlayNetworkReloadEndPresentation();

        /// <summary>Set the visible ammunition counter on the presentation weapon (host-authoritative).</summary>
        void SetNetworkAmmo(int currentAmmo, int capacity);

        /// <summary>Set the fire-mode indicator and recoil config (host-authoritative).</summary>
        void SetNetworkFireMode(FireMode fireMode);

        /// <summary>Stop any in-progress firing presentation.</summary>
        void StopNetworkFiring();
    }
}