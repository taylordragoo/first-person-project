using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.TacticalShooterPack.Scripts;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Networked Tactical Shotgun. Derives from <see cref="TacticalShotgun"/> (the vendor per-shell
    /// reload shotgun) and implements <see cref="INetworkTacticalWeaponPresentation"/> so the
    /// network path can drive fire and per-shell reload presentation without mutating ammo or
    /// scheduling cadence. The host owns authoritative state.
    /// </summary>
    public class NetworkTacticalShotgun : TacticalShotgun, INetworkTacticalWeaponPresentation
    {
        /// <summary>
        /// Play one fire presentation frame. The Herrington Police consumes one shell and resolves
        /// eight pellets on the host; this method only plays the visible/audible fire presentation.
        /// </summary>
        public void PlayNetworkFirePresentation()
        {
            if (muzzleFlash != null && !isSuppressed) muzzleFlash.Play();
            if (muzzleFlashSuppressed != null && isSuppressed) muzzleFlashSuppressed.Play();
            if (emptyCasing != null) emptyCasing.Play();

            if (_recoilAnimation != null) _recoilAnimation.Play();
            if (_fpsCamera != null) _fpsCamera.PlayCameraShake(tacWeaponSettings.recoilShake);
            PlayFireSound();

            PlayCharacterWeaponAnimation(_activeAmmo > 0
                ? TacShooterUtility.Animator_Fire.hash
                : TacShooterUtility.Animator_FireOut.hash);
        }

        public void PlayNetworkReloadPresentation()
        {
            // The vendor TacticalShotgun.Reload starts the per-shell loop. The host owns the
            // reload state and drives per-shell increments via PlayNetworkReloadEndPresentation.
            _skipFirstShell = _activeAmmo > 0;

            PlayCharacterWeaponAnimation(_activeAmmo == 0
                ? TacShooterUtility.Animator_ReloadStartEmpty.hash
                : TacShooterUtility.Animator_ReloadStart.hash);

            PlaySound(_activeAmmo == 0 ? tacWeaponSettings.reloadEmptySound : tacWeaponSettings.reloadTacSound);
        }

        public void PlayNetworkReloadEndPresentation()
        {
            // Per-shell reload loop. The host decrements authoritatively; presentation plays the
            // loop/end animation each time the host signals a shell has been loaded.
            bool isFull = _activeAmmo == tacWeaponSettings.ammoCapacity;
            PlayCharacterWeaponAnimation(isFull
                ? TacShooterUtility.Animator_ReloadEnd.hash
                : TacShooterUtility.Animator_ReloadLoop.hash);
            PlaySound(isFull ? tacWeaponSettings.reloadEndSound : tacWeaponSettings.reloadLoopSound);
        }

        public void SetNetworkAmmo(int currentAmmo, int capacity)
        {
            _activeAmmo = Mathf.Clamp(currentAmmo, 0, capacity);
        }

        public void SetNetworkFireMode(FireMode fireMode)
        {
            if (fireMode == this.fireMode) return;
            this.fireMode = fireMode;
            if (_recoilAnimation != null) _recoilAnimation.fireMode = fireMode;
        }

        public void StopNetworkFiring()
        {
            _isFiring = false;
            if (_recoilAnimation != null) _recoilAnimation.Stop();
            CancelInvoke(nameof(Fire));
        }
    }
}