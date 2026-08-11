using System.Collections.Generic;
using FPSProject.Combat.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace FPSProject.Multiplayer.Core.Match
{
    /// <summary>
    /// Builds a compact physics rig on the visible Tactical operator skeleton while the bot is
    /// alive, then hands its current animated pose to physics when the bot is killed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotRagdollController : MonoBehaviour
    {
        [SerializeField] private string rigRootPath =
            "Tactical Presentation/SKM_Operator/root";
        [SerializeField] private string weaponSocketPath = "ik_hand_root/ik_hand_gun";
        [SerializeField, Min(0f)] private float corpseLifetime = 12f;
        [SerializeField, Min(0f)] private float impactImpulseScale = 0.15f;
        [SerializeField, Min(0f)] private float minimumImpactImpulse = 1.5f;
        [SerializeField, Min(0f)] private float maximumImpactImpulse = 12f;

        private readonly List<Rigidbody> _bodies = new List<Rigidbody>(12);
        private readonly List<Collider> _ragdollColliders = new List<Collider>(12);
        private Collider[] _gameplayColliders;
        private NavMeshAgent _agent;
        private bool _initialized;

        public bool IsReady => _initialized && _bodies.Count > 0;
        public bool IsRagdollActive { get; private set; }
        public int BodyCount => _bodies.Count;

        private void Awake()
        {
            Initialize();
        }

        public bool Initialize()
        {
            if (_initialized) return IsReady;

            Transform rigRoot = transform.Find(rigRootPath);
            if (rigRoot == null) return false;

            _agent = GetComponent<NavMeshAgent>();
            _gameplayColliders = GetComponentsInChildren<Collider>(true);

            Transform pelvis = rigRoot.Find("pelvis");
            Transform chest = rigRoot.Find("pelvis/spine_01/spine_02/spine_03");
            Transform upperChest = rigRoot.Find(
                "pelvis/spine_01/spine_02/spine_03/spine_04/spine_05");
            Transform head = rigRoot.Find(
                "pelvis/spine_01/spine_02/spine_03/spine_04/spine_05/neck_01/neck_02/head");
            Transform leftUpperArm = upperChest != null
                ? upperChest.Find("clavicle_l/upperarm_l")
                : null;
            Transform leftLowerArm = leftUpperArm != null
                ? leftUpperArm.Find("lowerarm_l")
                : null;
            Transform leftHand = leftLowerArm != null ? leftLowerArm.Find("hand_l") : null;
            Transform rightUpperArm = upperChest != null
                ? upperChest.Find("clavicle_r/upperarm_r")
                : null;
            Transform rightLowerArm = rightUpperArm != null
                ? rightUpperArm.Find("lowerarm_r")
                : null;
            Transform rightHand = rightLowerArm != null ? rightLowerArm.Find("hand_r") : null;
            Transform leftThigh = pelvis != null ? pelvis.Find("thigh_l") : null;
            Transform leftCalf = leftThigh != null ? leftThigh.Find("calf_l") : null;
            Transform leftFoot = leftCalf != null ? leftCalf.Find("foot_l") : null;
            Transform rightThigh = pelvis != null ? pelvis.Find("thigh_r") : null;
            Transform rightCalf = rightThigh != null ? rightThigh.Find("calf_r") : null;
            Transform rightFoot = rightCalf != null ? rightCalf.Find("foot_r") : null;

            if (pelvis == null || chest == null || upperChest == null || head == null
                || leftUpperArm == null || leftLowerArm == null || leftHand == null
                || rightUpperArm == null || rightLowerArm == null || rightHand == null
                || leftThigh == null || leftCalf == null || leftFoot == null
                || rightThigh == null || rightCalf == null || rightFoot == null)
            {
                return false;
            }

            Rigidbody pelvisBody = AddCapsuleBody(pelvis, chest, null,
                0.42f, 8f, 20f, 30f);
            Rigidbody chestBody = AddCapsuleBody(chest, upperChest, pelvisBody,
                0.48f, 7f, 20f, 35f);
            AddSphereBody(head, chestBody, 0.13f, 3f, 20f, 25f);

            Rigidbody leftUpperArmBody = AddCapsuleBody(leftUpperArm, leftLowerArm,
                chestBody, 0.28f, 2f, 35f, 60f);
            AddCapsuleBody(leftLowerArm, leftHand, leftUpperArmBody,
                0.24f, 1.5f, 15f, 45f);
            Rigidbody rightUpperArmBody = AddCapsuleBody(rightUpperArm, rightLowerArm,
                chestBody, 0.28f, 2f, 35f, 60f);
            AddCapsuleBody(rightLowerArm, rightHand, rightUpperArmBody,
                0.24f, 1.5f, 15f, 45f);

            Rigidbody leftThighBody = AddCapsuleBody(leftThigh, leftCalf,
                pelvisBody, 0.3f, 5f, 25f, 40f);
            AddCapsuleBody(leftCalf, leftFoot, leftThighBody,
                0.24f, 3f, 10f, 55f);
            Rigidbody rightThighBody = AddCapsuleBody(rightThigh, rightCalf,
                pelvisBody, 0.3f, 5f, 25f, 40f);
            AddCapsuleBody(rightCalf, rightFoot, rightThighBody,
                0.24f, 3f, 10f, 55f);

            IgnoreSelfCollisions();
            _initialized = _bodies.Count == 11;
            return IsReady;
        }

        public bool Activate(in DamageInfo damageInfo)
        {
            if (IsRagdollActive) return true;
            if (!Initialize()) return false;

            IsRagdollActive = true;
            Vector3 inheritedVelocity = _agent != null && _agent.enabled
                && _agent.isOnNavMesh
                ? _agent.velocity
                : Vector3.zero;

            if (_agent != null)
            {
                if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false;
            }

            AttachEquippedWeapons(rigRoot: transform.Find(rigRootPath));

            foreach (Collider gameplayCollider in _gameplayColliders)
            {
                if (gameplayCollider != null) gameplayCollider.enabled = false;
            }

            foreach (Animator animator in GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour != this) behaviour.enabled = false;
            }

            foreach (Collider ragdollCollider in _ragdollColliders)
            {
                if (ragdollCollider != null) ragdollCollider.enabled = true;
            }

            foreach (Rigidbody body in _bodies)
            {
                if (body == null) continue;
                body.detectCollisions = true;
                body.isKinematic = false;
                body.linearVelocity = inheritedVelocity;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }

            ApplyImpactImpulse(damageInfo);
            if (Application.isPlaying && corpseLifetime > 0f)
                Destroy(gameObject, corpseLifetime);
            return true;
        }

        private void AttachEquippedWeapons(Transform rigRoot)
        {
            if (rigRoot == null) return;

            Transform weaponSocket = rigRoot.Find(weaponSocketPath);
            Transform rightHand = rigRoot.Find(
                "pelvis/spine_01/spine_02/spine_03/spine_04/spine_05/"
                + "clavicle_r/upperarm_r/lowerarm_r/hand_r");
            if (weaponSocket == null || rightHand == null) return;

            var equippedWeapons = new List<Transform>(weaponSocket.childCount);
            for (int index = 0; index < weaponSocket.childCount; index++)
            {
                Transform candidate = weaponSocket.GetChild(index);
                if (candidate.gameObject.activeInHierarchy
                    && candidate.GetComponentInChildren<Renderer>(false) != null)
                {
                    equippedWeapons.Add(candidate);
                }
            }

            foreach (Transform weapon in equippedWeapons)
                weapon.SetParent(rightHand, true);
        }

        private Rigidbody AddCapsuleBody(Transform bone, Transform end,
            Rigidbody connectedBody, float radiusScale, float mass,
            float twistLimit, float swingLimit)
        {
            Vector3 localEnd = bone.InverseTransformPoint(end.position);
            float length = localEnd.magnitude;
            float radius = Mathf.Max(0.025f, length * radiusScale);
            int direction = LargestAxis(localEnd);

            CapsuleCollider collider = bone.gameObject.AddComponent<CapsuleCollider>();
            collider.direction = direction;
            collider.center = localEnd * 0.5f;
            collider.radius = radius;
            collider.height = Mathf.Max(radius * 2f, length + radius * 2f);
            collider.enabled = false;
            _ragdollColliders.Add(collider);

            return AddBody(bone, connectedBody, mass, twistLimit, swingLimit);
        }

        private Rigidbody AddSphereBody(Transform bone, Rigidbody connectedBody,
            float radius, float mass, float twistLimit, float swingLimit)
        {
            SphereCollider collider = bone.gameObject.AddComponent<SphereCollider>();
            collider.radius = radius;
            collider.enabled = false;
            _ragdollColliders.Add(collider);

            return AddBody(bone, connectedBody, mass, twistLimit, swingLimit);
        }

        private Rigidbody AddBody(Transform bone, Rigidbody connectedBody,
            float mass, float twistLimit, float swingLimit)
        {
            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = true;
            body.isKinematic = true;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.2f;
            body.maxAngularVelocity = 20f;
            body.maxDepenetrationVelocity = 2f;
            _bodies.Add(body);

            if (connectedBody == null) return body;

            CharacterJoint joint = bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = connectedBody;
            joint.autoConfigureConnectedAnchor = true;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            joint.enableProjection = true;
            joint.projectionDistance = 0.05f;
            joint.projectionAngle = 15f;
            joint.axis = Vector3.right;
            joint.swingAxis = Vector3.forward;
            joint.lowTwistLimit = CreateLimit(-twistLimit);
            joint.highTwistLimit = CreateLimit(twistLimit);
            joint.swing1Limit = CreateLimit(swingLimit);
            joint.swing2Limit = CreateLimit(swingLimit);
            return body;
        }

        private void ApplyImpactImpulse(in DamageInfo damageInfo)
        {
            Rigidbody nearestBody = null;
            float nearestDistance = float.MaxValue;
            foreach (Rigidbody body in _bodies)
            {
                if (body == null) continue;
                float distance = (body.worldCenterOfMass - damageInfo.HitPoint).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestBody = body;
            }

            if (nearestBody == null) return;
            Vector3 direction = damageInfo.TravelDirection.sqrMagnitude > 0.0001f
                ? damageInfo.TravelDirection.normalized
                : -transform.forward;
            float impulse = Mathf.Clamp(damageInfo.Amount * impactImpulseScale,
                minimumImpactImpulse, maximumImpactImpulse);
            nearestBody.AddForceAtPosition(direction * impulse,
                damageInfo.HitPoint, ForceMode.Impulse);
        }

        private void IgnoreSelfCollisions()
        {
            for (int left = 0; left < _ragdollColliders.Count; left++)
            {
                for (int right = left + 1; right < _ragdollColliders.Count; right++)
                {
                    Physics.IgnoreCollision(_ragdollColliders[left],
                        _ragdollColliders[right], true);
                }
            }
        }

        private static int LargestAxis(Vector3 value)
        {
            Vector3 absolute = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y),
                Mathf.Abs(value.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z) return 0;
            return absolute.y >= absolute.z ? 1 : 2;
        }

        private static SoftJointLimit CreateLimit(float value)
        {
            return new SoftJointLimit
            {
                limit = value,
                bounciness = 0f,
                contactDistance = 1f
            };
        }
    }
}
