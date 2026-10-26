// Optional convenience: builds and saves a Play scene containing the simulator and adds it to
// Build Settings, so you can make a standalone player. Not required - pressing Play on any
// scene already starts the simulator via AvtokrobatBootstrap.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Avtokrobat
{
    public static class AvtokrobatMenu
    {
        [MenuItem("Avtokrobat/Create Play Scene")]
        public static void CreatePlayScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Avtokrobat").AddComponent<UnityPhysicsController>();

            const string dir = "Assets/Scenes";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            string path = dir + "/Main.unity";
            EditorSceneManager.SaveScene(scene, path);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            EditorUtility.DisplayDialog("Avtokrobat",
                "Created " + path + " and added it to Build Settings.\nPress Play to start the simulator.", "OK");
        }
    }
}
