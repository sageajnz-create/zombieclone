using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Overrun.Net;
using Overrun.Presentation;

namespace Overrun.EditorTools
{
    /// <summary>
    /// One-shot VS000 project setup. Idempotent — safe to re-run.
    /// Invoked headlessly via -executeMethod Overrun.EditorTools.OverrunBootstrap.RunAll
    /// </summary>
    public static class OverrunBootstrap
    {
        private const string RenderDir = "Assets/Content/Rendering";
        private const string SceneDir  = "Assets/Content/Scenes";

        private const string RendererPath = RenderDir + "/OverrunUniversalRenderer.asset";
        private const string PipelinePath = RenderDir + "/OverrunURPAsset.asset";

        [MenuItem("Overrun/Run VS000 Bootstrap")]
        public static void RunAll()
        {
            EnsureDir(RenderDir);
            EnsureDir(SceneDir);

            ConfigureUrp();
            CreateScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Overrun] VS000 bootstrap complete.");
        }

        // ------------------------------------------------------------------ URP

        private static void ConfigureUrp()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                Debug.Log($"[Overrun] Created renderer data: {RendererPath}");
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                Debug.Log($"[Overrun] Created URP asset: {PipelinePath}");
            }

            AssetDatabase.SaveAssets();

            // Assign as the default pipeline, and on every quality level — otherwise a
            // level left on Built-in silently renders differently.
            GraphicsSettings.defaultRenderPipeline = pipeline;

            int original = QualitySettings.GetQualityLevel();
            string[] levels = QualitySettings.names;
            for (int i = 0; i < levels.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(original, false);

            Debug.Log($"[Overrun] URP assigned as default pipeline and on {levels.Length} quality level(s).");
        }

        // --------------------------------------------------------------- Scenes

        private static void CreateScenes()
        {
            string bootstrap = SceneDir + "/Bootstrap.unity";
            string world     = SceneDir + "/World.unity";
            string localRigs = SceneDir + "/LocalRigs.unity";

            BuildScene(bootstrap, scene =>
            {
                // NetworkManager + transport.
                var nmGo = new GameObject("NetworkManager");
                var nm = nmGo.AddComponent<NetworkManager>();
                var utp = nmGo.AddComponent<UnityTransport>();
                if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
                nm.NetworkConfig.NetworkTransport = utp;

                // Session: the only place client ids are translated to PlayerIds.
                var sessionGo = new GameObject("NetSession");
                sessionGo.AddComponent<NetSession>();

                // EXACTLY ONE AudioListener in the entire game (ARCHITECTURE.md §4).
                // Split-screen cannot give each viewport its own listener; this rig gets
                // driven to the centroid of local players in VS002.
                var listener = new GameObject("AudioListenerRig");
                listener.AddComponent<AudioListener>();
            });

            BuildScene(world, _ =>
            {
                // Simulation root. Structure is identical on every client; only authority
                // differs. Populated in VS001.
                new GameObject("Arena");
                new GameObject("EnemyRoot");
                new GameObject("PlayerPawns");
            });

            BuildScene(localRigs, _ =>
            {
                // Presentation root. Differs per machine.
                var localPlayersGo = new GameObject("LocalPlayers");
                var localPlayers = localPlayersGo.AddComponent<LocalPlayers>();

                var joinGo = new GameObject("PlayerJoin");
                joinGo.AddComponent<UnityEngine.InputSystem.PlayerInputManager>();
                var handler = joinGo.AddComponent<LocalPlayerJoinHandler>();

                var so = new SerializedObject(handler);
                var prop = so.FindProperty("_localPlayers");
                if (prop != null)
                {
                    prop.objectReferenceValue = localPlayers;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                new GameObject("SharedUI");
            });

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(bootstrap, true),
                new EditorBuildSettingsScene(world,     true),
                new EditorBuildSettingsScene(localRigs, true),
            };

            Debug.Log("[Overrun] Scenes created and registered in Build Settings.");
        }

        private static void BuildScene(string path, System.Action<Scene> populate)
        {
            if (File.Exists(path))
            {
                Debug.Log($"[Overrun] Scene already exists, leaving alone: {path}");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            populate(scene);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[Overrun] Created scene: {path}");
        }

        private static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
