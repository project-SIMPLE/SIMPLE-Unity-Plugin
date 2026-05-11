using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tests.Editor
{
    public class ProjectSimpleGamaUnityEditorTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [TearDown]
        public void Teardown()
        {
        }

        [Test]
        public void SetupScene_RebuildsMinimalGamaSceneFromExistingScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("XR Origin (XR Rig)");
            new GameObject("Template Environment");
            new GameObject("Main Camera").AddComponent<Camera>();

            GAMAMenu.SetupScene();

            Assert.IsNull(GameObject.Find("Template Environment"));
            Assert.IsNull(GameObject.Find("XR Origin (XR Rig)"));
            Assert.IsNotNull(GameObject.Find("Directional Light"));
            Assert.IsNotNull(GameObject.Find("Teleport Area/Ground"));
            Assert.IsNotNull(GameObject.Find("FPSPlayer"));
            Assert.IsNotNull(GameObject.Find("ManagersSolo/Connection Manager"));
            Assert.IsNotNull(GameObject.Find("ManagersSolo/Game Manager"));
        }
    }
}
