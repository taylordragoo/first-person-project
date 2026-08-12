using System.Linq;
using System.Reflection;
using FirstPersonProject.Integrations.Kinemation.Presentation;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
using FPSProject.Multiplayer.Core.Movement;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public sealed class PassiveTargetBotPresentationPrefabTests
    {
        private const string PrefabPath =
            "Assets/FPSProject/Multiplayer/Prefabs/PassiveTargetBot.prefab";
        private const string PlayerPrefabPath =
            "Assets/Integrations/KINEMATION/CAS_Player_Example_FPS_Tactical.prefab";

        [Test]
        public void PlayerPrefab_UsesFasterPresentationResponseWithoutChangingBotDefaults()
        {
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            GameObject botRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(playerRoot, Is.Not.Null);
            Assert.That(botRoot, Is.Not.Null);

            try
            {
                Component playerBridge = FindPoseBridge(playerRoot);
                Component botBridge = FindPoseBridge(botRoot);
                var playerBridgeData = new SerializedObject(playerBridge);
                var botBridgeData = new SerializedObject(botBridge);

                Assert.That(playerBridgeData.FindProperty("tacticalGaitBlendSpeed").floatValue,
                    Is.EqualTo(12f));
                Assert.That(playerBridgeData.FindProperty("tacticalAimSpeedMultiplier").floatValue,
                    Is.EqualTo(2f));
                Assert.That(botBridgeData.FindProperty("tacticalGaitBlendSpeed").floatValue,
                    Is.EqualTo(6f));
                Assert.That(botBridgeData.FindProperty("tacticalAimSpeedMultiplier").floatValue,
                    Is.EqualTo(1f));

                Component playerController = playerRoot.GetComponentsInChildren<Component>(true)
                    .Single(component => component != null && component.GetType().FullName ==
                        "CAS_Demo.Scripts.FPS.FPSExampleController");
                var playerControllerData = new SerializedObject(playerController);
                Assert.That(playerControllerData.FindProperty("sprintGait")
                    .FindPropertyRelative("deceleration").floatValue, Is.EqualTo(4f));

                MethodInfo mapGait = playerBridge.GetType().GetMethod("MapTacticalGait",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(mapGait, Is.Not.Null);
                Assert.That(InvokeGaitMap(mapGait, 3f, false, true), Is.EqualTo(2f));
                Assert.That(InvokeGaitMap(mapGait, 3f, false, false), Is.EqualTo(1f));
                Assert.That(InvokeGaitMap(mapGait, 3f, true, true), Is.EqualTo(1f));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
                PrefabUtility.UnloadPrefabContents(botRoot);
            }
        }

        [Test]
        public void Prefab_PreservesCasTacticalProxyPipelineWithoutPlayerMotor()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(root, Is.Not.Null);

            try
            {
                Component[] components = root.GetComponentsInChildren<Component>(true);
                string[] typeNames = components.Where(component => component != null)
                    .Select(component => component.GetType().FullName)
                    .ToArray();

                Assert.That(root.GetComponentsInChildren<NetworkObject>(true), Has.Length.EqualTo(1));
                Assert.That(root.GetComponent<NetworkObject>(), Is.Not.Null);
                Assert.That(root.transform.Find("Skeleton_Kinemation_Mannequin"), Is.Not.Null);
                Assert.That(root.transform.Find("Tactical Presentation"), Is.Not.Null);

                Assert.That(typeNames, Does.Contain(
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkFPSExampleController"));
                Assert.That(typeNames, Does.Contain(
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.BotCasPresentationAdapter"));
                Assert.That(typeNames, Does.Contain(
                    "FirstPersonProject.Integrations.Kinemation.CasTacticalPlayerBridge"));
                Assert.That(typeNames, Does.Not.Contain(
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkCasPlayer"));
                Assert.That(typeNames, Does.Not.Contain(
                    "FPSProject.Multiplayer.Core.Match.PassiveTargetBotAnimationDriver"));

                Component poseBridge = components.Single(component => component != null
                    && component.GetType().FullName ==
                    "FirstPersonProject.Integrations.Kinemation.CasTacticalPlayerBridge");
                var serializedPoseBridge = new SerializedObject(poseBridge);
                Assert.That(serializedPoseBridge.FindProperty("drawSkeletonComparison").boolValue,
                    Is.True);

                Component tacticalPlayer = components.Single(component => component != null
                    && component.GetType().FullName ==
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkTacticalShooterPlayer");
                var serializedTacticalPlayer = new SerializedObject(tacticalPlayer);
                SerializedProperty weapons = serializedTacticalPlayer.FindProperty("weaponPrefabs");
                Assert.That(weapons.arraySize, Is.EqualTo(1));
                Assert.That(AssetDatabase.GetAssetPath(
                        weapons.GetArrayElementAtIndex(0).objectReferenceValue),
                    Is.EqualTo(
                        "Assets/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_AK105.prefab"));
                Component fpsCamera = serializedTacticalPlayer.FindProperty("fpsCamera")
                    .objectReferenceValue as Component;
                Assert.That(fpsCamera, Is.Not.Null);
                Assert.That(EditorUtility.IsPersistent(fpsCamera), Is.False);
                Assert.That(fpsCamera.transform.IsChildOf(root.transform), Is.True);

                AssertAllComponentReferencesAreLocal(root, components);

                Assert.That(root.GetComponentsInChildren<Camera>(true).Any(camera => camera.enabled),
                    Is.False);
                Assert.That(root.GetComponentsInChildren<AudioListener>(true)
                    .Any(listener => listener.enabled), Is.False);
                Assert.That(root.GetComponentsInChildren<CharacterController>(true)
                    .Any(controller => controller.enabled), Is.False);
                NavMeshAgent navAgent = root.GetComponent<NavMeshAgent>();
                Assert.That(navAgent, Is.Not.Null);
                Assert.That(NavMesh.GetSettingsNameFromID(navAgent.agentTypeID),
                    Is.EqualTo("Bot"));
                PassiveTargetBotNavigator navigator =
                    root.GetComponent<PassiveTargetBotNavigator>();
                FieldInfo navigatorAgent = typeof(PassiveTargetBotNavigator).GetField(
                    "_agent", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(navigatorAgent, Is.Not.Null);
                navigatorAgent.SetValue(navigator, navAgent);
                MethodInfo buildQueryFilter = typeof(PassiveTargetBotNavigator).GetMethod(
                    "BuildQueryFilter", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(buildQueryFilter, Is.Not.Null);
                var queryFilter = (NavMeshQueryFilter)buildQueryFilter.Invoke(navigator, null);
                Assert.That(queryFilter.agentTypeID, Is.EqualTo(navAgent.agentTypeID));
                Assert.That(queryFilter.areaMask, Is.EqualTo(navAgent.areaMask));
                CapsuleCollider hitCollider = root.GetComponent<CapsuleCollider>();
                Assert.That(hitCollider, Is.Not.Null);
                Assert.That(hitCollider.enabled, Is.True);
                Assert.That(root.GetComponentsInChildren<Collider>(true)
                    .Count(collider => collider.transform != root.transform), Is.Zero);

                Transform casSource = root.transform.Find("Skeleton_Kinemation_Mannequin");
                Assert.That(casSource.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => renderer.enabled), Is.False);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void PresentationAdapter_ReactivatesProxyModeAfterDespawnState()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(root, Is.Not.Null);

            try
            {
                Component adapter = root.GetComponents<Component>().Single(component =>
                    component.GetType().FullName ==
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.BotCasPresentationAdapter");
                var serializedAdapter = new SerializedObject(adapter);
                Component controller = serializedAdapter.FindProperty("controller")
                    .objectReferenceValue as Component;
                Assert.That(controller, Is.Not.Null);

                MethodInfo prepare = adapter.GetType().GetMethod("PrepareControllerForSpawn",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo setMode = controller.GetType().GetMethod("SetSimulationMode",
                    BindingFlags.Instance | BindingFlags.Public);
                PropertyInfo simulationMode = controller.GetType().GetProperty("SimulationMode",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(prepare, Is.Not.Null);
                Assert.That(setMode, Is.Not.Null);
                Assert.That(simulationMode, Is.Not.Null);

                Assert.That(prepare.Invoke(adapter, null), Is.EqualTo(true));
                Assert.That(simulationMode.GetValue(controller).ToString(),
                    Is.EqualTo("RemoteProxy"));

                object disabled = System.Enum.Parse(setMode.GetParameters()[0].ParameterType,
                    "Disabled");
                setMode.Invoke(controller, new[] { disabled });
                Assert.That(simulationMode.GetValue(controller).ToString(),
                    Is.EqualTo("Disabled"));

                Assert.That(prepare.Invoke(adapter, null), Is.EqualTo(true));
                Assert.That(simulationMode.GetValue(controller).ToString(),
                    Is.EqualTo("RemoteProxy"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void GroundingFilter_RequiresSustainedProbeMissBeforeEnteringAir()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(root, Is.Not.Null);

            try
            {
                Component adapter = root.GetComponents<Component>().Single(component =>
                    component.GetType().FullName ==
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.BotCasPresentationAdapter");
                var serializedAdapter = new SerializedObject(adapter);
                float probeDistance = serializedAdapter.FindProperty("groundProbeDistance")
                    .floatValue;
                float probeRadiusFactor = serializedAdapter.FindProperty("groundProbeRadiusFactor")
                    .floatValue;
                float fallDelay = serializedAdapter.FindProperty("fallDelay").floatValue;

                Assert.That(probeDistance, Is.GreaterThanOrEqualTo(0.2f));
                Assert.That(probeRadiusFactor, Is.GreaterThanOrEqualTo(0.75f));
                Assert.That(fallDelay, Is.GreaterThan(0f));

                MethodInfo resolveGrounded = adapter.GetType().GetMethod(
                    "ResolvePresentationGrounded",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(resolveGrounded, Is.Not.Null);

                Assert.That(resolveGrounded.Invoke(adapter, new object[] { true, 0.016f }),
                    Is.EqualTo(true));
                Assert.That(resolveGrounded.Invoke(adapter,
                    new object[] { false, fallDelay * 0.5f }), Is.EqualTo(true));
                Assert.That(resolveGrounded.Invoke(adapter,
                    new object[] { false, fallDelay * 0.51f }), Is.EqualTo(false));

                Assert.That(resolveGrounded.Invoke(adapter, new object[] { true, 0.016f }),
                    Is.EqualTo(true));
                Assert.That(resolveGrounded.Invoke(adapter,
                    new object[] { false, fallDelay * 0.5f }), Is.EqualTo(true));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void GroundProbe_CoversCapsuleFootprintAndRejectsEmptySpace()
        {
            System.Type adapterType = System.Type.GetType(
                "FirstPersonProject.Integrations.Kinemation.Multiplayer.BotCasPresentationAdapter, Assembly-CSharp");
            Assert.That(adapterType, Is.Not.Null);

            var bot = new GameObject("GroundProbeBot");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                CapsuleCollider capsule = bot.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.87f, 0f);
                capsule.radius = 0.3f;
                capsule.height = 1.75f;
                Component adapter = bot.AddComponent(adapterType);
                bot.transform.position = Vector3.up * 0.05f;

                ground.name = "GroundProbeSurface";
                ground.transform.position = new Vector3(0.2f, -0.05f, 0f);
                ground.transform.localScale = new Vector3(0.2f, 0.1f, 2f);
                Physics.SyncTransforms();

                MethodInfo resolveReferences = adapterType.GetMethod("ResolveReferences",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo probe = adapterType.GetMethod("ProbePhysicalGrounded",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(resolveReferences, Is.Not.Null);
                Assert.That(probe, Is.Not.Null);
                resolveReferences.Invoke(adapter, null);
                Assert.That(probe.Invoke(adapter, null), Is.EqualTo(true),
                    "Footprint probe should find offset support that a center ray misses.");

                ground.transform.position = Vector3.down * 2f;
                Physics.SyncTransforms();
                Assert.That(probe.Invoke(adapter, null), Is.EqualTo(false));
            }
            finally
            {
                Object.DestroyImmediate(ground);
                Object.DestroyImmediate(bot);
            }
        }

        [Test]
        public void ProxyInitialization_SelectsOneCasPropWithPoseSettings()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(root, Is.Not.Null);

            try
            {
                Component adapter = root.GetComponents<Component>().Single(component =>
                    component.GetType().FullName ==
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.BotCasPresentationAdapter");
                var serializedAdapter = new SerializedObject(adapter);
                Component controller = serializedAdapter.FindProperty("controller")
                    .objectReferenceValue as Component;
                Assert.That(controller, Is.Not.Null);

                MethodInfo prepare = adapter.GetType().GetMethod("PrepareControllerForSpawn",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo getActiveItem = controller.GetType().GetMethod("GetActiveItem",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(prepare, Is.Not.Null);
                Assert.That(getActiveItem, Is.Not.Null);
                Assert.That(prepare.Invoke(adapter, null), Is.EqualTo(true));

                Component activeItem = getActiveItem.Invoke(controller, null) as Component;
                Assert.That(activeItem, Is.Not.Null);
                Assert.That(activeItem.gameObject.activeSelf, Is.True);
                Assert.That(new SerializedObject(activeItem).FindProperty("animationSettings")
                    .objectReferenceValue, Is.Not.Null);

                Component[] casProps = root.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null
                        && activeItem.GetType().BaseType.IsAssignableFrom(component.GetType()))
                    .ToArray();
                Assert.That(casProps.Count(prop => prop.gameObject.activeSelf), Is.EqualTo(1));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void BotHitCollider_ResolvesAsNetworkCombatActor()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(root, Is.Not.Null);

            try
            {
                System.Type networkPlayerType = System.Type.GetType(
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkCasPlayer, Assembly-CSharp");
                Assert.That(networkPlayerType, Is.Not.Null);
                MethodInfo findTarget = networkPlayerType.GetMethod("FindNetworkHealthTarget",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(findTarget, Is.Not.Null);

                CapsuleCollider hitCollider = root.GetComponent<CapsuleCollider>();
                NetworkHealth health = root.GetComponent<NetworkHealth>();
                Assert.That(hitCollider, Is.Not.Null);
                Assert.That(health, Is.Not.Null);
                Assert.That(findTarget.Invoke(null, new object[] { hitCollider }), Is.SameAs(health));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void NetworkWeapons_RouteReloadStartAndCompletionToOwner()
        {
            AssertReloadOverrides(
                "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkTacticalShooterWeapon, Assembly-CSharp");
            AssertReloadOverrides(
                "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkTacticalShotgun, Assembly-CSharp");

            System.Type routerType = System.Type.GetType(
                "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkWeaponShotRouter, Assembly-CSharp");
            Assert.That(routerType, Is.Not.Null);
            Assert.That(routerType.GetMethod("RequestReload",
                BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(routerType.GetMethod("CompleteReload",
                BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);

            System.Type playerType = System.Type.GetType(
                "FirstPersonProject.Integrations.Kinemation.Multiplayer.NetworkCasPlayer, Assembly-CSharp");
            Assert.That(playerType, Is.Not.Null);
            Assert.That(playerType.GetMethod("RequestReload",
                BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(playerType.GetMethod("CompleteReload",
                BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        }

        [Test]
        public void TopNavigationSpeed_MapsToFullJogPresentation()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(root, Is.Not.Null);

            try
            {
                NavMeshAgent navAgent = root.GetComponent<NavMeshAgent>();
                Assert.That(navAgent, Is.Not.Null);

                Component adapter = root.GetComponents<Component>().Single(component =>
                    component.GetType().FullName ==
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.BotCasPresentationAdapter");
                var serializedAdapter = new SerializedObject(adapter);
                Component controller = serializedAdapter.FindProperty("controller")
                    .objectReferenceValue as Component;
                Assert.That(controller, Is.Not.Null);

                var serializedController = new SerializedObject(controller);
                float jogPresentationSpeed = serializedController.FindProperty("jogGait")
                    .FindPropertyRelative("velocity").floatValue;
                Assert.That(jogPresentationSpeed, Is.EqualTo(navAgent.speed).Within(0.0001f));

                MethodInfo prepare = adapter.GetType().GetMethod("PrepareControllerForSpawn",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo applyPresentation = controller.GetType().GetMethod(
                    "ApplySharedPresentationWithoutRootMotion",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(LocomotionPresentationInput).MakeByRefType(), typeof(float) },
                    null);
                PropertyInfo gait = controller.GetType().GetProperty("Gait",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(prepare, Is.Not.Null);
                Assert.That(applyPresentation, Is.Not.Null);
                Assert.That(gait, Is.Not.Null);
                Assert.That(prepare.Invoke(adapter, null), Is.EqualTo(true));

                var input = new LocomotionPresentationInput
                {
                    GaitSource = GaitSource.ObservedPlanarSpeed,
                    ObservedPlanarSpeed = navAgent.speed,
                    MoveAxes = Vector2.up,
                    IsMoving = true,
                    IsGrounded = true,
                    IsAlive = true
                };
                applyPresentation.Invoke(controller, new object[] { input, 0.016f });

                Assert.That((float)gait.GetValue(controller), Is.EqualTo(2f).Within(0.0001f));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void PresentationOnlyState_PreservesNavigationOwnedRoot()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Assert.That(root, Is.Not.Null);

            try
            {
                Component adapter = root.GetComponents<Component>().Single(component =>
                    component.GetType().FullName ==
                    "FirstPersonProject.Integrations.Kinemation.Multiplayer.BotCasPresentationAdapter");
                var serializedAdapter = new SerializedObject(adapter);
                Component controller = serializedAdapter.FindProperty("controller")
                    .objectReferenceValue as Component;
                Assert.That(controller, Is.Not.Null);

                MethodInfo prepare = adapter.GetType().GetMethod("PrepareControllerForSpawn",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo applyWithRoot = controller.GetType().GetMethod(
                    "ApplyRemotePresentationState", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo applyWithoutRoot = controller.GetType().GetMethod(
                    "ApplyRemotePresentationStateWithoutRootMotion",
                    BindingFlags.Instance | BindingFlags.Public);
                MethodInfo sharedPresentation = controller.GetType().GetMethod(
                    "ApplySharedPresentationWithoutRootMotion",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(LocomotionPresentationInput).MakeByRefType(), typeof(float) },
                    null);
                Assert.That(prepare, Is.Not.Null);
                Assert.That(applyWithRoot, Is.Not.Null);
                Assert.That(applyWithoutRoot, Is.Not.Null);
                Assert.That(sharedPresentation, Is.Not.Null);
                Assert.That(prepare.Invoke(adapter, null), Is.EqualTo(true));

                var state = new ProxyInterpolationBuffer.SampledState
                {
                    Position = new Vector3(80f, 2f, -40f),
                    BodyYaw = 137f,
                    AimYaw = 137f,
                    IsGrounded = true,
                    IsAlive = true
                };
                Vector3 navigationPosition = new Vector3(12f, 3f, 8f);
                Quaternion navigationRotation = Quaternion.Euler(0f, 25f, 0f);

                controller.transform.SetPositionAndRotation(navigationPosition,
                    navigationRotation);
                applyWithRoot.Invoke(controller, new object[] { state });
                Assert.That(controller.transform.position, Is.EqualTo(state.Position));
                Assert.That(controller.transform.eulerAngles.y,
                    Is.EqualTo(state.BodyYaw).Within(0.01f));

                controller.transform.SetPositionAndRotation(navigationPosition,
                    navigationRotation);
                applyWithoutRoot.Invoke(controller, new object[] { state });
                Assert.That(controller.transform.position, Is.EqualTo(navigationPosition));
                Assert.That(Quaternion.Angle(controller.transform.rotation, navigationRotation),
                    Is.LessThan(0.01f));

                var sharedInput = new LocomotionPresentationInput
                {
                    GaitSource = GaitSource.ObservedPlanarSpeed,
                    ObservedPlanarSpeed = 2.25f,
                    MoveAxes = Vector2.up,
                    IsMoving = true,
                    IsGrounded = true,
                    IsAlive = true
                };
                sharedPresentation.Invoke(controller, new object[] { sharedInput, 0.016f });
                Assert.That(controller.transform.position, Is.EqualTo(navigationPosition));
                Assert.That(Quaternion.Angle(controller.transform.rotation, navigationRotation),
                    Is.LessThan(0.01f));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Component FindPoseBridge(GameObject root)
        {
            return root.GetComponentsInChildren<Component>(true).Single(component =>
                component != null && component.GetType().FullName ==
                "FirstPersonProject.Integrations.Kinemation.CasTacticalPlayerBridge");
        }

        private static float InvokeGaitMap(MethodInfo method, float gait, bool isAiming,
            bool isSprinting)
        {
            return (float)method.Invoke(null, new object[] { gait, isAiming, isSprinting });
        }

        private static void AssertAllComponentReferencesAreLocal(GameObject root,
            Component[] components)
        {
            foreach (Component owner in components)
            {
                if (owner == null) continue;
                var serialized = new SerializedObject(owner);
                SerializedProperty property = serialized.GetIterator();
                if (!property.NextVisible(true)) continue;

                do
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference
                        || property.objectReferenceValue == null)
                    {
                        continue;
                    }

                    Object reference = property.objectReferenceValue;
                    Component referencedComponent = reference as Component;
                    GameObject referencedObject = reference as GameObject;
                    if (referencedComponent == null && referencedObject == null) continue;
                    if (referencedComponent == null && EditorUtility.IsPersistent(reference))
                        continue;

                    Transform target = referencedComponent != null
                        ? referencedComponent.transform
                        : referencedObject.transform;
                    bool local = !EditorUtility.IsPersistent(reference)
                        && (target == root.transform || target.IsChildOf(root.transform));
                    Assert.That(local, Is.True,
                        $"{owner.GetType().Name}.{property.propertyPath} references external "
                        + $"object {reference.name}.");
                }
                while (property.NextVisible(true));
            }
        }

        private static void AssertReloadOverrides(string assemblyQualifiedTypeName)
        {
            System.Type weaponType = System.Type.GetType(assemblyQualifiedTypeName);
            Assert.That(weaponType, Is.Not.Null);

            MethodInfo reload = weaponType.GetMethod("Reload",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo reloadWeapon = weaponType.GetMethod("ReloadWeapon",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(reload, Is.Not.Null);
            Assert.That(reloadWeapon, Is.Not.Null);
            Assert.That(reload.DeclaringType, Is.EqualTo(weaponType));
            Assert.That(reloadWeapon.DeclaringType, Is.EqualTo(weaponType));
        }
    }
}
