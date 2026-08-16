using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Overrun.EditorTools
{
    /// <summary>
    /// Forces Play Mode to always begin at Bootstrap, whatever scene happens to be open.
    ///
    /// Without this the game only works if you remember to open Bootstrap first: Play runs
    /// the *current* scene, and on a fresh checkout that is an empty untitled scene, so the
    /// game appears completely blank with no error to explain why. Bootstrap is the only
    /// scene that starts the host and additively loads World and LocalRigs.
    ///
    /// Toggle from the Overrun menu if you ever need to run a scene in isolation.
    /// </summary>
    [InitializeOnLoad]
    public static class OverrunPlayModeStart
    {
        private const string BootstrapPath = "Assets/Content/Scenes/Bootstrap.unity";
        private const string MenuPath = "Overrun/Always Start Play From Bootstrap";
        private const string PrefKey = "Overrun.ForceBootstrapPlayMode";

        static OverrunPlayModeStart()
        {
            // Deferred: playModeStartScene is not reliably settable during static init,
            // and the AssetDatabase may not be ready yet on a fresh import.
            EditorApplication.delayCall += Apply;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        private static void Apply()
        {
            if (!Enabled)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath);
            if (scene == null)
            {
                Debug.LogWarning($"[Overrun] {BootstrapPath} not found; Play will use the open scene.");
                return;
            }

            if (EditorSceneManager.playModeStartScene != scene)
            {
                EditorSceneManager.playModeStartScene = scene;
                Debug.Log("[Overrun] Play Mode will start from Bootstrap.");
            }
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
