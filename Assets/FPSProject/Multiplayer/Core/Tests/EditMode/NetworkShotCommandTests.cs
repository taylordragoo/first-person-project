using FPSProject.Multiplayer.Core.Weapons;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class NetworkShotCommandTests
    {
        private static NetworkShotCommand RoundTrip(NetworkShotCommand original)
        {
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteNetworkSerializable(original);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetworkShotCommand result);
            return result;
        }

        private static NetworkShotResult RoundTripResult(NetworkShotResult original)
        {
            using var writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteNetworkSerializable(original);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetworkShotResult result);
            return result;
        }

        [Test]
        public void NetworkSerialize_RoundTripsAllFields()
        {
            var original = new NetworkShotCommand
            {
                WeaponId = 3,
                ShotSequence = 42,
                NetworkTick = 12345,
                AimYaw = 12.5f,
                AimPitch = -30f,
                AimDirection = new Vector3(0.1f, 0.2f, 0.9f),
                IsAiming = true
            };

            var roundtrip = RoundTrip(original);

            Assert.AreEqual(original.WeaponId, roundtrip.WeaponId);
            Assert.AreEqual(original.ShotSequence, roundtrip.ShotSequence);
            Assert.AreEqual(original.NetworkTick, roundtrip.NetworkTick);
            Assert.AreEqual(original.AimYaw, roundtrip.AimYaw);
            Assert.AreEqual(original.AimPitch, roundtrip.AimPitch);
            Assert.AreEqual(original.AimDirection, roundtrip.AimDirection);
            Assert.AreEqual(original.IsAiming, roundtrip.IsAiming);
        }

        [Test]
        public void NetworkShotCommand_DoesNotCarryDamageOrAmmo()
        {
            var fields = typeof(NetworkShotCommand).GetFields();
            var names = new System.Collections.Generic.List<string>();
            foreach (var f in fields) names.Add(f.Name);

            CollectionAssert.Contains(names, "WeaponId");
            CollectionAssert.Contains(names, "ShotSequence");
            CollectionAssert.Contains(names, "NetworkTick");
            CollectionAssert.Contains(names, "AimYaw");
            CollectionAssert.Contains(names, "AimPitch");
            CollectionAssert.Contains(names, "AimDirection");
            CollectionAssert.Contains(names, "IsAiming");

            Assert.IsFalse(names.Contains("Damage"));
            Assert.IsFalse(names.Contains("Ammo"));
            Assert.IsFalse(names.Contains("CurrentAmmo"));
            Assert.IsFalse(names.Contains("HitResult"));
            Assert.IsFalse(names.Contains("Prefab"));
        }
    }

    public class NetworkShotResultTests
    {
        private static NetworkShotResult RoundTrip(NetworkShotResult original)
        {
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteNetworkSerializable(original);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetworkShotResult result);
            return result;
        }

        [Test]
        public void NetworkSerialize_RoundTripsSingleImpact()
        {
            var original = new NetworkShotResult
            {
                WeaponId = 1,
                ShotSequence = 7,
                ShooterClientId = 5,
                MuzzlePosition = new Vector3(1, 2, 3),
                ImpactCount = 1,
                Impact0 = new NetworkShotImpact
                {
                    Point = new Vector3(10, 20, 30),
                    Normal = new Vector3(0, 1, 0),
                    HitTargetNetworkId = 99,
                    IsPlayerHit = true,
                    SurfaceType = 5
                }
            };

            var roundtrip = RoundTrip(original);

            Assert.AreEqual(original.WeaponId, roundtrip.WeaponId);
            Assert.AreEqual(original.ShotSequence, roundtrip.ShotSequence);
            Assert.AreEqual(original.ShooterClientId, roundtrip.ShooterClientId);
            Assert.AreEqual(original.MuzzlePosition, roundtrip.MuzzlePosition);
            Assert.AreEqual(original.ImpactCount, roundtrip.ImpactCount);
            Assert.AreEqual(original.Impact0.Point, roundtrip.Impact0.Point);
            Assert.AreEqual(original.Impact0.Normal, roundtrip.Impact0.Normal);
            Assert.AreEqual(original.Impact0.HitTargetNetworkId, roundtrip.Impact0.HitTargetNetworkId);
            Assert.AreEqual(original.Impact0.IsPlayerHit, roundtrip.Impact0.IsPlayerHit);
            Assert.AreEqual(original.Impact0.SurfaceType, roundtrip.Impact0.SurfaceType);
            Assert.AreEqual(default(NetworkShotImpact), roundtrip.Impact1);
        }

        [Test]
        public void NetworkSerialize_RoundTripsEightImpacts()
        {
            var original = new NetworkShotResult
            {
                WeaponId = 3,
                ShotSequence = 100,
                ShooterClientId = 2,
                MuzzlePosition = Vector3.zero,
                ImpactCount = 8
            };
            original.Impact0 = new NetworkShotImpact { Point = new Vector3(1, 0, 0) };
            original.Impact1 = new NetworkShotImpact { Point = new Vector3(2, 0, 0) };
            original.Impact2 = new NetworkShotImpact { Point = new Vector3(3, 0, 0) };
            original.Impact3 = new NetworkShotImpact { Point = new Vector3(4, 0, 0) };
            original.Impact4 = new NetworkShotImpact { Point = new Vector3(5, 0, 0) };
            original.Impact5 = new NetworkShotImpact { Point = new Vector3(6, 0, 0) };
            original.Impact6 = new NetworkShotImpact { Point = new Vector3(7, 0, 0) };
            original.Impact7 = new NetworkShotImpact { Point = new Vector3(8, 0, 0) };

            var roundtrip = RoundTrip(original);

            Assert.AreEqual(8, roundtrip.ImpactCount);
            Assert.AreEqual(new Vector3(1, 0, 0), roundtrip.Impact0.Point);
            Assert.AreEqual(new Vector3(2, 0, 0), roundtrip.Impact1.Point);
            Assert.AreEqual(new Vector3(3, 0, 0), roundtrip.Impact2.Point);
            Assert.AreEqual(new Vector3(4, 0, 0), roundtrip.Impact3.Point);
            Assert.AreEqual(new Vector3(5, 0, 0), roundtrip.Impact4.Point);
            Assert.AreEqual(new Vector3(6, 0, 0), roundtrip.Impact5.Point);
            Assert.AreEqual(new Vector3(7, 0, 0), roundtrip.Impact6.Point);
            Assert.AreEqual(new Vector3(8, 0, 0), roundtrip.Impact7.Point);
        }

        [Test]
        public void Capacity_IsEightForShotgun()
        {
            Assert.AreEqual(8, NetworkShotResult.Capacity);
        }
    }
}
