using FPSProject.Combat.Runtime;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class BotRagdollControllerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void InitializeAndActivate_BuildsLiveHitboxesThenDynamicRagdoll()
        {
            _root = BuildBotSkeleton();
            var health = _root.AddComponent<NetworkHealth>();
            var broadCapsule = _root.AddComponent<CapsuleCollider>();
            var ragdoll = _root.AddComponent<BotRagdollController>();
            ragdoll.Initialize();

            Assert.IsTrue(ragdoll.IsReady);
            Assert.AreEqual(11, ragdoll.BodyCount);
            Assert.IsTrue(broadCapsule.enabled);
            Assert.IsTrue(CombatRaycastPolicy.ShouldSkip(broadCapsule));
            Assert.AreEqual(11, _root.GetComponentsInChildren<BotDamageHitbox>(true).Length);
            foreach (Rigidbody body in _root.GetComponentsInChildren<Rigidbody>(true))
            {
                Assert.IsTrue(body.isKinematic);
                Assert.IsTrue(body.detectCollisions);
                Assert.IsTrue(body.GetComponent<Collider>().enabled);
            }

            Transform rigRoot = _root.transform.Find("Tactical Presentation/SKM_Operator/root");
            BotDamageHitbox headHitbox = rigRoot.Find(
                "pelvis/spine_01/spine_02/spine_03/spine_04/spine_05/neck_01/neck_02/head")
                .GetComponent<BotDamageHitbox>();
            BotDamageHitbox chestHitbox = rigRoot.Find(
                "pelvis/spine_01/spine_02/spine_03").GetComponent<BotDamageHitbox>();
            BotDamageHitbox armHitbox = rigRoot.Find(
                "pelvis/spine_01/spine_02/spine_03/spine_04/spine_05/"
                + "clavicle_l/upperarm_l").GetComponent<BotDamageHitbox>();
            Assert.AreEqual(2f, headHitbox.DamageMultiplier);
            Assert.IsTrue(headHitbox.IsGuaranteedLethal);
            Assert.AreEqual(1f, chestHitbox.DamageMultiplier);
            Assert.IsFalse(chestHitbox.IsGuaranteedLethal);
            Assert.AreEqual(0.75f, armHitbox.DamageMultiplier);
            Assert.IsFalse(armHitbox.IsGuaranteedLethal);

            var zoneDamage = new DamageInfo(10f, Vector3.up, Vector3.up,
                Vector3.forward, null, null);
            armHitbox.ApplyDamage(zoneDamage);
            Assert.AreEqual(92.5f, health.CurrentHealth.Value);
            headHitbox.ApplyDamage(zoneDamage);
            Assert.AreEqual(0f, health.CurrentHealth.Value);

            Transform weapon = _root.transform.Find(
                "Tactical Presentation/SKM_Operator/root/ik_hand_root/ik_hand_gun/Weapon");
            Vector3 weaponWorldPosition = weapon.position;

            var damage = new DamageInfo(25f, Vector3.up, Vector3.up,
                Vector3.forward, null, null);
            Assert.IsTrue(ragdoll.Activate(damage));
            Assert.IsTrue(ragdoll.IsRagdollActive);
            Assert.AreEqual("hand_r", weapon.parent.name);
            Assert.That(Vector3.Distance(weaponWorldPosition, weapon.position), Is.LessThan(0.0001f));
            foreach (Rigidbody body in _root.GetComponentsInChildren<Rigidbody>(true))
            {
                Assert.IsFalse(body.isKinematic);
                Assert.IsTrue(body.detectCollisions);
            }
        }

        private static GameObject BuildBotSkeleton()
        {
            var root = new GameObject("Bot");
            Transform rig = Child(Child(Child(root.transform, "Tactical Presentation"),
                "SKM_Operator"), "root");
            Transform pelvis = Child(rig, "pelvis", new Vector3(0f, 1f, 0f));
            Transform spine1 = Child(pelvis, "spine_01", Vector3.up * 0.12f);
            Transform spine2 = Child(spine1, "spine_02", Vector3.up * 0.12f);
            Transform spine3 = Child(spine2, "spine_03", Vector3.up * 0.12f);
            Transform spine4 = Child(spine3, "spine_04", Vector3.up * 0.12f);
            Transform spine5 = Child(spine4, "spine_05", Vector3.up * 0.12f);
            Transform neck1 = Child(spine5, "neck_01", Vector3.up * 0.1f);
            Transform neck2 = Child(neck1, "neck_02", Vector3.up * 0.08f);
            Child(neck2, "head", Vector3.up * 0.1f);

            BuildArm(spine5, "l", -1f);
            BuildArm(spine5, "r", 1f);
            BuildLeg(pelvis, "l", -1f);
            BuildLeg(pelvis, "r", 1f);

            Transform ikHandRoot = Child(rig, "ik_hand_root");
            Transform weaponSocket = Child(ikHandRoot, "ik_hand_gun");
            Transform weapon = Child(weaponSocket, "Weapon");
            weapon.gameObject.AddComponent<MeshRenderer>();
            return root;
        }

        private static void BuildArm(Transform chest, string side, float sign)
        {
            Transform clavicle = Child(chest, $"clavicle_{side}",
                new Vector3(sign * 0.08f, 0.03f, 0f));
            Transform upper = Child(clavicle, $"upperarm_{side}",
                new Vector3(sign * 0.08f, 0f, 0f));
            Transform lower = Child(upper, $"lowerarm_{side}",
                new Vector3(sign * 0.25f, 0f, 0f));
            Child(lower, $"hand_{side}", new Vector3(sign * 0.22f, 0f, 0f));
        }

        private static void BuildLeg(Transform pelvis, string side, float sign)
        {
            Transform thigh = Child(pelvis, $"thigh_{side}",
                new Vector3(sign * 0.1f, -0.08f, 0f));
            Transform calf = Child(thigh, $"calf_{side}", Vector3.down * 0.42f);
            Child(calf, $"foot_{side}", new Vector3(0f, -0.4f, 0.1f));
        }

        private static Transform Child(Transform parent, string name)
        {
            return Child(parent, name, Vector3.zero);
        }

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }
    }
}
