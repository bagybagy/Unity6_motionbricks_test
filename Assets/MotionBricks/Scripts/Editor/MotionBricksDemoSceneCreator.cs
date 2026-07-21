using MotionBricks.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MotionBricks.Editor
{
    public static class MotionBricksDemoSceneCreator
    {
        public const string ScenePath = "Assets/MotionBricks/Scenes/MotionBricksDemo.unity";

        [MenuItem("MotionBricks/Create or Reset Demo Scene")]
        public static void CreateOrResetDemoScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var player = new GameObject("MotionBricks G1 Player");
            player.AddComponent<MotionBricksRigDriver>();
            player.AddComponent<G1DemoRigBuilder>();
            player.AddComponent<MotionBricksUdpClient>();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(4.5f, 2.5f, -4.5f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.9f, 0f));
            camera.fieldOfView = 50f;

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            EnsureFolder("Assets/MotionBricks/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = player;
            Debug.Log($"MotionBricks demo scene created at {ScenePath}");
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }
    }
}
