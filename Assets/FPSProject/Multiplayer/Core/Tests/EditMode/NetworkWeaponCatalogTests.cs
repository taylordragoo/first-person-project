using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class NetworkWeaponCatalogTests
    {
        private static NetworkWeaponEntry CreateEntry(ushort id, string name,
            int capacity = 30, bool shotgun = false, int pellets = 1)
        {
            return new NetworkWeaponEntry
            {
                weaponId = id,
                displayName = name,
                magazineCapacity = capacity,
                isShotgun = shotgun,
                pelletCount = pellets,
                supportsSemi = true,
                ballistics = new NetworkWeaponBallistics
                {
                    hipSpreadDegrees = 2f,
                    adsSpreadDegrees = 0.5f
                }
            };
        }

        private static NetworkWeaponCatalog CreateCatalog(params NetworkWeaponEntry[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<NetworkWeaponCatalog>();
            catalog.ClearEntries();
            foreach (var e in entries) catalog.AddEntry(e);
            return catalog;
        }

        [Test]
        public void TryGetEntry_FindsExistingId()
        {
            var catalog = CreateCatalog(
                CreateEntry(1, "TR15"),
                CreateEntry(2, "Viper"),
                CreateEntry(3, "Police", 12, true, 8),
                CreateEntry(4, "Mk14"));

            Assert.IsTrue(catalog.TryGetEntry(1, out var e1));
            Assert.AreEqual("TR15", e1.displayName);
            Assert.IsTrue(catalog.TryGetEntry(3, out var e3));
            Assert.AreEqual(12, e3.magazineCapacity);
            Assert.IsTrue(e3.isShotgun);
            Assert.AreEqual(8, e3.pelletCount);
        }

        [Test]
        public void TryGetEntry_ReturnsFalseForUnknownId()
        {
            var catalog = CreateCatalog(CreateEntry(1, "TR15"));
            Assert.IsFalse(catalog.TryGetEntry(99, out _));
        }

        [Test]
        public void IndexOf_ReturnsIndexOrNegativeOne()
        {
            var catalog = CreateCatalog(
                CreateEntry(1, "TR15"),
                CreateEntry(2, "Viper"),
                CreateEntry(3, "Police"));

            Assert.AreEqual(0, catalog.IndexOf(1));
            Assert.AreEqual(2, catalog.IndexOf(3));
            Assert.AreEqual(-1, catalog.IndexOf(99));
        }

        [Test]
        public void Contains_ReportsPresence()
        {
            var catalog = CreateCatalog(CreateEntry(1, "TR15"), CreateEntry(2, "Viper"));
            Assert.IsTrue(catalog.Contains(1));
            Assert.IsTrue(catalog.Contains(2));
            Assert.IsFalse(catalog.Contains(3));
        }

        [Test]
        public void Entries_AreReadOnly()
        {
            var catalog = CreateCatalog(CreateEntry(1, "TR15"), CreateEntry(2, "Viper"));
            Assert.IsInstanceOf<IReadOnlyList<NetworkWeaponEntry>>(catalog.Entries);
            Assert.AreEqual(2, catalog.Count);
        }

        [Test]
        public void AddEntry_RejectsDuplicateId()
        {
            var catalog = CreateCatalog(CreateEntry(1, "TR15"));
            Assert.IsFalse(catalog.AddEntry(CreateEntry(1, "TR15 duplicate")));
            Assert.AreEqual(1, catalog.Count);
        }

        [Test]
        public void AddEntry_RejectsZeroId()
        {
            var catalog = CreateCatalog();
            Assert.IsFalse(catalog.AddEntry(CreateEntry(0, "Zero")));
            Assert.AreEqual(0, catalog.Count);
        }

        [Test]
        public void ResourcesCatalog_HasFourPlanWeapons()
        {
            var assets = Resources.LoadAll<NetworkWeaponCatalog>("");
            Assert.IsTrue(assets.Length >= 1, "No NetworkWeaponCatalog found in Resources.");
            var catalog = assets[0];

            Assert.AreEqual(4, catalog.Count);
            Assert.IsTrue(catalog.Contains(1), "Missing weapon ID 1 (TR15).");
            Assert.IsTrue(catalog.Contains(2), "Missing weapon ID 2 (WK-11 Viper).");
            Assert.IsTrue(catalog.Contains(3), "Missing weapon ID 3 (Herrington Police).");
            Assert.IsTrue(catalog.Contains(4), "Missing weapon ID 4 (Mk14 EBR).");

            // Plan defaults: TR15 magazine 32, Viper 26, Police 12, Mk14 20.
            Assert.IsTrue(catalog.TryGetEntry(1, out var tr15));
            Assert.AreEqual(32, tr15.magazineCapacity);
            Assert.IsTrue(tr15.supportsBurst);
            Assert.IsTrue(tr15.supportsAuto);

            Assert.IsTrue(catalog.TryGetEntry(2, out var viper));
            Assert.AreEqual(26, viper.magazineCapacity);
            Assert.IsFalse(viper.supportsBurst);
            Assert.IsFalse(viper.supportsAuto);

            Assert.IsTrue(catalog.TryGetEntry(3, out var police));
            Assert.AreEqual(12, police.magazineCapacity);
            Assert.IsTrue(police.isShotgun);
            Assert.AreEqual(8, police.pelletCount);
            Assert.AreEqual(12f, police.ballistics.damage);
            Assert.AreEqual(50f, police.ballistics.maxRange);
            Assert.AreEqual(4f, police.ballistics.hipSpreadDegrees);

            Assert.IsTrue(catalog.TryGetEntry(4, out var mk14));
            Assert.AreEqual(20, mk14.magazineCapacity);
            Assert.IsTrue(mk14.supportsAuto);
            Assert.AreEqual(35f, mk14.ballistics.damage);
        }

        [Test]
        public void ResourcesCatalog_AdsSpreadSmallerThanHip()
        {
            var assets = Resources.LoadAll<NetworkWeaponCatalog>("");
            Assert.IsTrue(assets.Length >= 1);
            var catalog = assets[0];

            for (int i = 0; i < catalog.Count; i++)
            {
                var e = catalog.Entries[i];
                Assert.LessOrEqual(e.ballistics.adsSpreadDegrees, e.ballistics.hipSpreadDegrees,
                    $"Weapon {e.displayName} ADS spread must be <= hip spread.");
            }
        }

        [Test]
        public void WeaponAmmoState_EqualsAndHashCode()
        {
            var a = new WeaponAmmoState { WeaponId = 1, CurrentAmmo = 30, Capacity = 32 };
            var b = new WeaponAmmoState { WeaponId = 1, CurrentAmmo = 30, Capacity = 32 };
            var c = new WeaponAmmoState { WeaponId = 1, CurrentAmmo = 29, Capacity = 32 };

            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsFalse(a.Equals(c));
            Assert.IsTrue(a.Equals((object)b));
            Assert.IsFalse(a.Equals((object)null));
            Assert.IsFalse(a.Equals("not a state"));
        }
    }
}