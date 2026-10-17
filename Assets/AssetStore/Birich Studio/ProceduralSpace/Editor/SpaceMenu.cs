using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BirichStudio.ProceduralSpace.Editor
{
    /// <summary>
    /// Menu entries for setting the package up.
    ///
    /// The prefab in the package folder is a perfectly good way to add a background — for someone
    /// who already knows it exists. A buyer's first minutes are spent looking for the entry point,
    /// and an asset that can only be started by browsing to the right folder loses people there.
    /// </summary>
    internal static class SpaceMenu
    {
        private static string _root;

        /// <summary>
        /// Where the package actually is, found from the shader's own asset path rather than
        /// assumed to be where it shipped.
        ///
        /// Buyers relocate vendor folders as a matter of routine, and when they did, every
        /// hard-coded path in this package quietly stopped matching: the "this is a bundled
        /// preset, duplicate it first" guard never fired again, and the create menu stopped
        /// assigning a material and a preset. Both failures are silent, which is the worst kind.
        /// </summary>
        internal static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_root)) return _root;

                Shader shader = Shader.Find(SpaceBackground.ShaderName);
                string path = shader != null ? AssetDatabase.GetAssetPath(shader) : null;

                // <root>/Shaders/ProceduralSpace.shader → up two.
                _root = string.IsNullOrEmpty(path)
                    ? "Assets/Birich Studio/ProceduralSpace"
                    : System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path))
                            .Replace('\\', '/');
                return _root;
            }
        }

        [MenuItem("GameObject/Birich Studio/Procedural Space/Space Background", false, 10)]
        private static void CreateBackground(MenuCommand command)
        {
            var go = new GameObject("Space Background");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);

            var bg = go.AddComponent<SpaceBackground>();
            go.AddComponent<SpaceCameraFit>();

            var material = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Shaders/ProceduralSpace.mat");
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Ship it with a preset assigned rather than on hidden defaults: the point of the
            // one-click path is that what appears is already a finished sky, not a starting point
            // the user has to go and configure before it looks like anything.
            var preset = AssetDatabase.LoadAssetAtPath<SpaceBackgroundProfile>($"{Root}/Presets/Smooth Cosmos.asset");
            if (preset != null) bg.Profile = preset;

            // Put it where it will actually be seen. Dropping the object at the origin of a scene
            // whose camera is elsewhere produces an empty Game view and the impression that the
            // package does not work.
            Camera cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (cam != null)
                go.transform.position = cam.transform.position + cam.transform.forward * 50f;

            Undo.RegisterCreatedObjectUndo(go, "Create Space Background");
            Selection.activeObject = go;
        }

        [MenuItem("Window/Birich Studio/Procedural Space/Open Demo Scene", false, 20)]
        private static void OpenDemo()
        {
            string scene = Root + "/Demo/DemoScene.unity";
            if (!System.IO.File.Exists(scene))
            {
                EditorUtility.DisplayDialog("Procedural Space",
                    "The demo scene is not in the project.\n\nExpected it at:\n" + scene, "OK");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(scene);
        }

        [MenuItem("Window/Birich Studio/Procedural Space/Documentation", false, 21)]
        private static void OpenDocs()
        {
            // Documentation, not Documentation~. A trailing tilde tells Unity to skip the folder
            // entirely — it gets no .meta files and cannot appear in a package export, so the
            // documentation was never going to reach a buyer and this lookup could never succeed.
            // The convention belongs to UPM packages under Packages/, not to an asset shipped
            // inside Assets/.
            string doc = Root + "/Documentation/Documentation.md";
            if (System.IO.File.Exists(doc))
            {
                EditorUtility.RevealInFinder(doc);
                return;
            }

            // Say where it should be and how to ask, rather than opening a browser. The only
            // way to reach this is a partial import, and a URL cannot fix that.
            EditorUtility.DisplayDialog("Procedural Space",
                "The documentation file is not in the project.\n\nExpected it at:\n" + doc +
                "\n\nIf the package was imported partially, re-import it — or write to " +
                "birichgame@gmail.com.", "OK");
        }
    }
}
