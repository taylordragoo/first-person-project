using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FPSProject.Multiplayer.Core.Match
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class PassiveTargetBotAnimationDriver : NetworkBehaviour
    {
        [SerializeField] private string speedParamName = "Speed";
        [SerializeField] private float _smoothingFactor = 8f;
        [SerializeField] private float walkThreshold = 0.5f;
        [SerializeField] private float jogThreshold = 4f;
        [SerializeField] private bool applyWeaponHandIk = true;

        private Animator _animator;
        private NavMeshAgent _agent;
        private int _speedHash;
        private Vector3 _lastPosition;
        private float _smoothedSpeed;

        private ArmIkChain _rightArm;
        private ArmIkChain _leftArm;

        private readonly struct ArmIkChain
        {
            public readonly Transform UpperArm;
            public readonly Transform LowerArm;
            public readonly Transform Hand;
            public readonly Transform Target;

            public ArmIkChain(Transform upperArm, Transform lowerArm, Transform hand,
                Transform target)
            {
                UpperArm = upperArm;
                LowerArm = lowerArm;
                Hand = hand;
                Target = target;
            }

            public bool IsValid => UpperArm != null && LowerArm != null && Hand != null
                && Target != null;
        }

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>(true);
            _agent = GetComponent<NavMeshAgent>();
            _speedHash = Animator.StringToHash(speedParamName);
            ResolveWeaponHandIk();
        }

        public override void OnNetworkSpawn()
        {
            _lastPosition = transform.position;
        }

        private void Update()
        {
            if (_animator == null) return;

            float targetSpeed;
            if (IsServer)
            {
                targetSpeed = _agent != null
                    ? Vector3.Scale(_agent.velocity, new Vector3(1f, 0f, 1f)).magnitude
                    : 0f;
            }
            else
            {
                Vector3 planarDelta = Vector3.Scale(
                    transform.position - _lastPosition,
                    new Vector3(1f, 0f, 1f));
                targetSpeed = planarDelta.magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
            }

            _lastPosition = transform.position;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, targetSpeed,
                Time.deltaTime * _smoothingFactor);
            _animator.SetFloat(_speedHash, _smoothedSpeed);
        }

        private void LateUpdate()
        {
            if (!applyWeaponHandIk || _animator == null || !_animator.enabled) return;

            SolveArm(_rightArm);
            SolveArm(_leftArm);
        }

        private void ResolveWeaponHandIk()
        {
            if (_animator == null) return;

            Transform skeleton = _animator.transform;
            const string spine = "root/pelvis/spine_01/spine_02/spine_03/spine_04/spine_05";
            const string gun = "root/ik_hand_root/ik_hand_gun";

            _rightArm = new ArmIkChain(
                skeleton.Find($"{spine}/clavicle_r/upperarm_r"),
                skeleton.Find($"{spine}/clavicle_r/upperarm_r/lowerarm_r"),
                skeleton.Find($"{spine}/clavicle_r/upperarm_r/lowerarm_r/hand_r"),
                skeleton.Find($"{gun}/ik_hand_r"));

            _leftArm = new ArmIkChain(
                skeleton.Find($"{spine}/clavicle_l/upperarm_l"),
                skeleton.Find($"{spine}/clavicle_l/upperarm_l/lowerarm_l"),
                skeleton.Find($"{spine}/clavicle_l/upperarm_l/lowerarm_l/hand_l"),
                skeleton.Find($"{gun}/ik_hand_l"));
        }

        private static void SolveArm(ArmIkChain chain)
        {
            if (!chain.IsValid) return;

            KTwoBoneIkData ik = new KTwoBoneIkData
            {
                root = new KTransform(chain.UpperArm),
                mid = new KTransform(chain.LowerArm),
                tip = new KTransform(chain.Hand),
                target = new KTransform(chain.Target),
                posWeight = 1f,
                rotWeight = 1f,
                hintWeight = 0f,
                allowStretching = false,
                startStretchRatio = 1f,
                maxStretchScale = 1f,
                hasValidHint = false
            };

            KTwoBoneIK.Solve(ref ik);
            chain.UpperArm.rotation = ik.root.rotation;
            chain.LowerArm.rotation = ik.mid.rotation;
            chain.Hand.rotation = ik.tip.rotation;
        }

        private void OnDisable()
        {
            if (_animator != null) _animator.SetFloat(_speedHash, 0f);
        }

        public override void OnNetworkDespawn()
        {
            if (_animator != null) _animator.SetFloat(_speedHash, 0f);
        }
    }
}
