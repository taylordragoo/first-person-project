using System.Collections;
using System.Linq;
using FPSProject.Multiplayer.Core.Bootstrap;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPSProject.Multiplayer.PlayModeTests
{
    public class MultiplayerNetworkSpawnPlayTests
    {
        private const string TestSceneName = "MultiplayerTest";

        private NetworkManager _networkManager;
        private Scene _multiplayerScene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Single);
            Assert.IsNotNull(load, $"Could not load the '{TestSceneName}' scene from Build Settings.");
            yield return load;

            _multiplayerScene = SceneManager.GetSceneByName(TestSceneName);
            Assert.IsTrue(_multiplayerScene.IsValid() && _multiplayerScene.isLoaded);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
                yield return null;
            }

            if (_networkManager != null)
            {
                Object.Destroy(_networkManager.gameObject);
                yield return null;
            }

            if (_multiplayerScene.IsValid() && _multiplayerScene.isLoaded)
            {
                Scene cleanupScene = SceneManager.CreateScene(nameof(MultiplayerNetworkSpawnPlayTests)
                    + "_Cleanup");
                SceneManager.SetActiveScene(cleanupScene);
                yield return SceneManager.UnloadSceneAsync(_multiplayerScene);
            }
        }

        [UnityTest]
        public IEnumerator HostSpawn_KeepsTacticalRigFiniteAndOwnerSystemsCorrect()
        {
            // Starting after a complete frame reproduces the manual H-key workflow and guards
            // against inserting Tactical animation playables into an already-running graph.
            yield return null;

            _networkManager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.IsNotNull(_networkManager);

            INetworkSessionBootstrap bootstrap = Object
                .FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .OfType<INetworkSessionBootstrap>()
                .FirstOrDefault();
            Assert.IsNotNull(bootstrap);
            Assert.IsTrue(bootstrap.StartHost());

            for (int frame = 0; frame < 5; frame++) yield return null;

            Assert.IsTrue(_networkManager.IsHost);
            Assert.AreEqual(1, _networkManager.ConnectedClients.Count);

            NetworkObject localPlayer = _networkManager.SpawnManager.GetLocalPlayerObject();
            Assert.IsNotNull(localPlayer);

            GameObject player = localPlayer.gameObject;
            Transform tacticalPresentation = player.transform.Find("Tactical Presentation");
            Assert.IsNotNull(tacticalPresentation);

            AssertFiniteHierarchy(tacticalPresentation);
            AssertOwnerSystems(player, tacticalPresentation.gameObject);
        }

        private static void AssertFiniteHierarchy(Transform root)
        {
            foreach (Transform target in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.IsTrue(IsFinite(target.localPosition),
                    $"{GetPath(target)} has invalid local position {target.localPosition}.");
                Assert.IsTrue(IsFinite(target.position),
                    $"{GetPath(target)} has invalid world position {target.position}.");
                Assert.IsTrue(IsFinite(target.localRotation),
                    $"{GetPath(target)} has invalid local rotation {target.localRotation}.");
                Assert.IsTrue(IsFinite(target.rotation),
                    $"{GetPath(target)} has invalid world rotation {target.rotation}.");
            }
        }

        private static void AssertOwnerSystems(GameObject player, GameObject tacticalPresentation)
        {
            Behaviour controller = FindBehaviour(player, "NetworkFPSExampleController", rootOnly: true);
            Assert.IsNotNull(controller);
            Assert.AreEqual("LocalOwner", GetProperty(controller, "SimulationMode")?.ToString());
            Assert.AreEqual(true, GetProperty(controller, "IsOwnerInitialized"));

            Behaviour ownerInput = FindBehaviour(player, "PlayerInput", rootOnly: true);
            Assert.IsNotNull(ownerInput);
            Assert.IsTrue(ownerInput.enabled);

            Behaviour ownerRecoil = FindBehaviour(player, "RecoilAnimation", rootOnly: true);
            Assert.IsNotNull(ownerRecoil);
            Assert.IsTrue(ownerRecoil.enabled);

            Assert.IsTrue(player.GetComponent<CharacterController>().enabled);

            Behaviour characterCamera = FindBehaviour(player, "CharacterCamera", rootOnly: false);
            Assert.IsNotNull(characterCamera);
            Assert.IsTrue(characterCamera.GetComponent<Camera>().enabled);
            Assert.IsTrue(characterCamera.GetComponent<AudioListener>().enabled);
            Assert.AreEqual(1, player.GetComponentsInChildren<AudioListener>(true)
                .Count(listener => listener.enabled));

            Behaviour tacticalInput = FindBehaviour(tacticalPresentation, "PlayerInput",
                rootOnly: true);
            Assert.IsNotNull(tacticalInput);
            Assert.IsFalse(tacticalInput.enabled);

            Behaviour tacticalAnimation = FindBehaviour(tacticalPresentation,
                "TacticalProceduralAnimation", rootOnly: true);
            Behaviour tacticalPlayer = FindBehaviour(tacticalPresentation,
                "TacticalShooterPlayer", rootOnly: true);
            Assert.IsNotNull(tacticalAnimation);
            Assert.IsNotNull(tacticalPlayer);
            Assert.IsTrue(tacticalAnimation.enabled);
            Assert.IsTrue(tacticalPlayer.enabled);
        }

        private static Behaviour FindBehaviour(GameObject root, string typeName, bool rootOnly)
        {
            Behaviour[] behaviours = rootOnly
                ? root.GetComponents<Behaviour>()
                : root.GetComponentsInChildren<Behaviour>(true);
            return behaviours.FirstOrDefault(component => component.GetType().Name == typeName);
        }

        private static object GetProperty(object target, string propertyName)
        {
            return target.GetType().GetProperty(propertyName)?.GetValue(target);
        }

        private static string GetPath(Transform target)
        {
            string path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }

            return path;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y)
                || !float.IsFinite(value.z) || !float.IsFinite(value.w))
            {
                return false;
            }

            float magnitudeSquared = value.x * value.x + value.y * value.y
                + value.z * value.z + value.w * value.w;
            return magnitudeSquared > Mathf.Epsilon;
        }
    }
}
