using KINEMATION.TacticalShooterPack.Scripts;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer
{
    /// <summary>
    /// Networked Tactical Shooter Weapon. Derives from the vendor <see cref="TacticalShooterWeapon"/>
    /// and implements <see cref="INetworkTacticalWeaponPresentation"/> so the network path can drive
    /// fire, reload, ammo, and fire-mode presentation without independently scheduling fire or
    /// deciding ammunition. The host owns authoritative ammo and cadence; this class only replays
    /// what the host has accepted.
    /// </summary>
    public class NetworkTacticalShooterWeapon : TacticalShooterWeapon, INetworkTacticalWeaponPresentation
    {
        /// <summary>
        /// Play one fire presentation frame: muzzle flash, casing, recoil, camera shake, fire
        /// animation, and fire audio. Does NOT decrement ammo and does NOT schedule the next
        /// shot via Invoke. The host owns cadence and ammunition.
        /// </summary>
        public void PlayNetworkFirePresentation()
        {
            // Mirror the vendor Fire() presentation path but stop before the ammo decrement and
            // the Invoke-based cadence loop. We do not call base.Fire() because it would mutate
            // _activeAmmo and self-schedule the next shot.
            if (muzzleFlash != null && !isSuppressed) muzzleFlash.Play();
            if (muzzleFlashSuppressed != null && isSuppressed) muzzleFlashSuppressed.Play();
            if (emptyCasing != null) emptyCasing.Play();

            if (_recoilAnimation != null) _recoilAnimation.Play();
            if (_fpsCamera != null) _fpsCamera.PlayCameraShake(tacWeaponSettings.recoilShake);
            PlayFireSound();

            // Play the fire animation clip. Use the FireOut variant only as a visual cue when the
            // authoritative ammo count reaches zero; the network path keeps _activeAmmo in sync via
            // SetNetworkAmmo so this branch reflects the real magazine state.
            PlayCharacterWeaponAnimation(_activeAmmo > 0
                ? TacShooterUtility.Animator_Fire.hash
                : TacShooterUtility.Animator_FireOut.hash);
        }

        /// <summary>
        /// Play the reload-start presentation. The host owns reload state; this method only plays
        /// animation and sound and does not change ammo.
        /// </summary>
        public void PlayNetworkReloadPresentation()
        {
            // Mirror the vendor Reload() presentation without changing _activeAmmo. The host
            // decrements ammunition authoritatively; presentation reads it via SetNetworkAmmo.
            PlayCharacterWeaponAnimation(_activeAmmo == 0
                ? TacShooterUtility.Animator_ReloadEmpty.hash
                : TacShooterUtility.Animator_ReloadTac.hash);

            PlaySound(_activeAmmo == 0 ? tacWeaponSettings.reloadEmptySound : tacWeaponSettings.reloadTacSound);
        }

        /// <summary>
        /// Play the reload-end / per-shell reload loop presentation. The host owns reload state.
        /// </summary>
        public void PlayNetworkReloadEndPresentation()
        {
            // Shotgun per-shell reload loops use ReloadLoop / ReloadEnd; standard weapons use the
            // tac reload animation already played by PlayNetworkReloadPresentation. This is a
            // no-op for non-shotgun weapons; the shotgun subclass overrides the reload path.
            if (this is TacticalShotgun)
            {
                PlayCharacterWeaponAnimation(
                    _activeAmmo == tacWeaponSettings.ammoCapacity
                        ? TacShooterUtility.Animator_ReloadEnd.hash
                        : TacShooterUtility.Animator_ReloadLoop.hash);
                PlaySound(_activeAmmo == tacWeaponSettings.ammoCapacity
                    ? tacWeaponSettings.reloadEndSound
                    : tacWeaponSettings.reloadLoopSound);
            }
        }

        /// <summary>
        /// Set the visible ammunition counter on the presentation weapon. The host owns ammo;
        /// this method keeps the presentation's _activeAmmo in sync with the authoritative value.
        /// </summary>
        public void SetNetworkAmmo(int currentAmmo, int capacity)
        {
            _activeAmmo = Mathf.Clamp(currentAmmo, 0, capacity);
        }

        /// <summary>
        /// Set the fire-mode indicator and recoil config. The host owns the active fire mode;
        /// this method updates the presentation-side fireMode and the recoil animation config.
        /// </summary>
        public void SetNetworkFireMode(FireMode fireMode)
        {
            if (fireMode == this.fireMode) return;
            this.fireMode = fireMode;
            if (_recoilAnimation != null) _recoilAnimation.fireMode = fireMode;
        }

        /// <summary>Stop any in-progress firing presentation. Does not change ammo.</summary>
        public void StopNetworkFiring()
        {
            _isFiring = false;
            if (_recoilAnimation != null) _recoilAnimation.Stop();
            CancelInvoke(nameof(Fire));
        }
    }
}