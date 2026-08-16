using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using Overrun.Net;
using Overrun.Presentation;
using Overrun.Simulation;

namespace Overrun.EditorTools
{
    /// <summary>
    /// Builds the VS001 playable slice: pawn prefab, local rig prefab, greybox arena, and
    /// the scene wiring that connects them. Idempotent — safe to re-run.
    ///
    /// Invoked headlessly via -executeMethod Overrun.EditorTools.VS001Bootstrap.RunAll
    /// </summary>
    public static class VS001Bootstrap
    {
        private const string PrefabDir = "Assets/Content/Prefabs";
        private const string SceneDir  = "Assets/Content/Scenes";
        private const string InputPath = "Assets/Content/Input/OverrunControls.inputactions";

        private const string PawnPath = PrefabDir + "/PlayerPawn.prefab";
        private const string RigPath  = PrefabDir + "/LocalPlayerRig.prefab";

        [MenuItem("Overrun/Run VS001 Bootstrap")]
        public static void RunAll()
        {
            if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);

            GameObject pawn = BuildPawnPrefab();
            GameObject rig = BuildRigPrefab();

            BuildArena();
            WireBootstrapScene(pawn);
            WireLocalRigsScene(rig);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Overrun] VS001 bootstrap complete.");
        }

        // ------------------------------------------------------------------ pawn prefab

        private static GameObject BuildPawnPrefab()
        {
            var root = new GameObject("PlayerPawn");

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;

            // Greybox body so the pawn is visible to the other split-screen player.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Object.DestroyImmediate(body.GetComponent<Collider>());   // CharacterController owns collision

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            root.AddComponent<NetworkObject>();

            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;   // ADR-003
            netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;

            var pawn = root.AddComponent<PlayerPawn>();
            SetPrivateField(pawn, "_head", head.transform);

            root.AddComponent<Health>();

            GameObject saved = SaveAsPrefab(root, PawnPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // ------------------------------------------------------------------- rig prefab

        private static GameObject BuildRigPrefab()
        {
            var root = new GameObject("LocalPlayerRig");

            // Camera: NO AudioListener. Unity permits exactly one in the entire game and it
            // lives on AudioListenerRig in Bootstrap (ARCHITECTURE §4 / ADR-019).
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 75f;

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(root.transform, false);
            var canvas = hudGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;

            var input = root.AddComponent<PlayerInput>();
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            if (actions != null)
            {
                input.actions = actions;
                input.defaultActionMap = "Player";
            }
            else
            {
                Debug.LogError($"[Overrun] Missing input actions at {InputPath}");
            }
            input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            input.camera = cam;   // split-screen viewport rects are assigned through this

            var context = root.AddComponent<PlayerContext>();
            SetPrivateField(context, "_camera", cam);
            SetPrivateField(context, "_hud", canvas);
            SetPrivateField(context, "_interactionOrigin", camGo.transform);
            SetPrivateField(context, "_input", input);

            var router = root.AddComponent<LocalInputRouter>();
            SetPrivateField(router, "_context", context);

            var rig = root.AddComponent<PlayerCameraRig>();
            SetPrivateField(rig, "_context", context);
            SetPrivateField(rig, "_cameraPivot", camGo.transform);

            GameObject saved = SaveAsPrefab(root, RigPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // ----------------------------------------------------------------------- arena

        private static void BuildArena()
        {
            string path = SceneDir + "/World.unity";
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            Transform arena = FindOrCreate(scene, "Arena").transform;

            // Guard ONLY the geometry. An early return here would also skip spawn points,
            // the registrar and lighting — which is exactly what happened the first time
            // this ran after those were added.
            if (arena.childCount > 0)
            {
                Debug.Log("[Overrun] Arena geometry already present, skipping boxes.");
            }
            else
            {
                BuildArenaGeometry(arena);
            }

            EnsureArenaFixtures(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Overrun] Arena fixtures ensured in World.unity");
        }

        private static void BuildArenaGeometry(Transform arena)
        {
            // Two rooms joined by a doorway. Room A: 16x16 at origin. Room B: 16x16 at z=+22.
            Box(arena, "Floor_A",   new Vector3(0f, -0.5f, 0f),   new Vector3(16f, 1f, 16f));
            Box(arena, "Floor_Gap", new Vector3(0f, -0.5f, 11f),  new Vector3(4f, 1f, 6f));
            Box(arena, "Floor_B",   new Vector3(0f, -0.5f, 22f),  new Vector3(16f, 1f, 16f));

            const float h = 3f;
            // Room A walls
            Box(arena, "A_West",  new Vector3(-8f, h * 0.5f, 0f), new Vector3(0.5f, h, 16f));
            Box(arena, "A_East",  new Vector3( 8f, h * 0.5f, 0f), new Vector3(0.5f, h, 16f));
            Box(arena, "A_South", new Vector3(0f, h * 0.5f, -8f), new Vector3(16f, h, 0.5f));
            Box(arena, "A_North_L", new Vector3(-5f, h * 0.5f, 8f), new Vector3(6f, h, 0.5f));
            Box(arena, "A_North_R", new Vector3( 5f, h * 0.5f, 8f), new Vector3(6f, h, 0.5f));

            // Room B walls
            Box(arena, "B_West",  new Vector3(-8f, h * 0.5f, 22f), new Vector3(0.5f, h, 16f));
            Box(arena, "B_East",  new Vector3( 8f, h * 0.5f, 22f), new Vector3(0.5f, h, 16f));
            Box(arena, "B_North", new Vector3(0f, h * 0.5f, 30f),  new Vector3(16f, h, 0.5f));
            Box(arena, "B_South_L", new Vector3(-5f, h * 0.5f, 14f), new Vector3(6f, h, 0.5f));
            Box(arena, "B_South_R", new Vector3( 5f, h * 0.5f, 14f), new Vector3(6f, h, 0.5f));

            // Corridor sides
            Box(arena, "Gap_West", new Vector3(-2f, h * 0.5f, 11f), new Vector3(0.5f, h, 6f));
            Box(arena, "Gap_East", new Vector3( 2f, h * 0.5f, 11f), new Vector3(0.5f, h, 6f));

            Debug.Log("[Overrun] Greybox arena geometry built.");
        }

        /// <summary>
        /// Everything the arena needs that is NOT the box geometry. Run unconditionally so
        /// adding a fixture later actually lands on an already-greyboxed scene.
        /// </summary>
        private static void EnsureArenaFixtures(Scene scene)
        {
            GameObject light = FindOrCreate(scene, "Sun");
            Light dir = light.GetComponent<Light>() != null ? light.GetComponent<Light>() : light.AddComponent<Light>();
            dir.type = LightType.Directional;
            dir.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject spawnsGo = FindOrCreate(scene, "SpawnPoints");
            Transform spawns = spawnsGo.transform;
            if (spawns.childCount == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    var p = new GameObject($"PlayerSpawn_{i}");
                    p.transform.SetParent(spawns, false);
                    p.transform.position = new Vector3(-3f + i * 2f, 0.2f, -5f);
                }
            }

            // Register at runtime — a serialized reference from NetSession (Bootstrap
            // scene) to these transforms cannot survive, since Unity has no cross-scene
            // object references.
            if (spawnsGo.GetComponent<ArenaSpawnRegistrar>() == null)
                spawnsGo.AddComponent<ArenaSpawnRegistrar>();

            FindOrCreate(scene, "EnemyRoot");
            FindOrCreate(scene, "PlayerPawns");
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.isStatic = true;
        }

        // ------------------------------------------------------------------- scene wiring

        private static void WireBootstrapScene(GameObject pawnPrefab)
        {
            string path = SceneDir + "/Bootstrap.unity";
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            GameObject nmGo = FindInScene(scene, "NetworkManager");
            GameObject sessionGo = FindInScene(scene, "NetSession");
            if (nmGo == null || sessionGo == null)
            {
                Debug.LogError("[Overrun] Bootstrap scene missing NetworkManager or NetSession.");
                return;
            }

            // NetSession is an in-scene NetworkObject; NGO tracks those automatically.
            if (sessionGo.GetComponent<NetworkObject>() == null) sessionGo.AddComponent<NetworkObject>();

            // Without this nothing ever calls StartHost(), so NetSession never spawns and
            // joining a local player silently does nothing.
            if (nmGo.GetComponent<SessionBootstrapper>() == null) nmGo.AddComponent<SessionBootstrapper>();

            var session = sessionGo.GetComponent<NetSession>();
            if (session != null && pawnPrefab != null)
            {
                SetPrivateField(session, "_pawnPrefab", pawnPrefab);
            }

            // Register the pawn as a network prefab — NGO requires pre-registration, and a
            // hash mismatch between peers fails the connection outright.
            var nm = nmGo.GetComponent<NetworkManager>();
            if (nm != null && pawnPrefab != null)
            {
                if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
                bool present = false;
                foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
                {
                    if (p != null && p.Prefab == pawnPrefab) { present = true; break; }
                }
                if (!present) nm.AddNetworkPrefab(pawnPrefab);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Overrun] Bootstrap wired: NetworkObject + pawn prefab registered.");
        }

        private static void WireLocalRigsScene(GameObject rigPrefab)
        {
            string path = SceneDir + "/LocalRigs.unity";
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            GameObject joinGo = FindInScene(scene, "PlayerJoin");
            GameObject localPlayersGo = FindInScene(scene, "LocalPlayers");
            if (joinGo == null || localPlayersGo == null)
            {
                Debug.LogError("[Overrun] LocalRigs scene missing PlayerJoin or LocalPlayers.");
                return;
            }

            var pim = joinGo.GetComponent<UnityEngine.InputSystem.PlayerInputManager>();
            if (pim != null && rigPrefab != null)
            {
                var so = new SerializedObject(pim);

                if (!TrySetObject(so, "m_PlayerPrefab", rigPrefab))
                {
                    // Field names are internal to the Input System package; if they ever
                    // change, say so loudly instead of null-referencing.
                    Debug.LogError("[Overrun] Could not set PlayerInputManager player prefab — " +
                                   "serialized field 'm_PlayerPrefab' not found. Assign it manually.");
                    DumpProperties(so);
                }

                TrySetEnum(so, "m_JoinBehavior", 0);   // JoinPlayersWhenButtonIsPressed
                TrySetBool(so, "m_SplitScreen", true);
                TrySetInt(so, "m_MaxPlayerCount", LocalPlayers.MaxLocalPlayers);

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var handler = joinGo.GetComponent<LocalPlayerJoinHandler>();
            if (handler != null)
            {
                SetPrivateField(handler, "_localPlayers", localPlayersGo.GetComponent<LocalPlayers>());
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Overrun] LocalRigs wired: split-screen join with rig prefab.");
        }

        // ---------------------------------------------------------------------- helpers

        private static GameObject SaveAsPrefab(GameObject source, string path)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(source, path, out bool ok);
            if (!ok) Debug.LogError($"[Overrun] Failed to save prefab: {path}");
            else Debug.Log($"[Overrun] Prefab: {path}");
            return saved;
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
            }
            return null;
        }

        private static GameObject FindOrCreate(Scene scene, string name)
        {
            GameObject existing = FindInScene(scene, name);
            if (existing != null) return existing;

            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static bool TrySetObject(SerializedObject so, string name, Object value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) return false;
            p.objectReferenceValue = value;
            return true;
        }

        private static bool TrySetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Overrun] no serialized bool '{name}'"); return false; }
            p.boolValue = value;
            return true;
        }

        private static bool TrySetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Overrun] no serialized int '{name}'"); return false; }
            p.intValue = value;
            return true;
        }

        private static bool TrySetEnum(SerializedObject so, string name, int index)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Overrun] no serialized enum '{name}'"); return false; }
            p.enumValueIndex = index;
            return true;
        }

        private static void DumpProperties(SerializedObject so)
        {
            SerializedProperty it = so.GetIterator();
            var names = new List<string>();
            while (it.NextVisible(true)) names.Add(it.propertyPath);
            Debug.Log("[Overrun] available properties: " + string.Join(", ", names));
        }

        /// <summary>
        /// Assign a [SerializeField] private field. Editor-time wiring only — gameplay code
        /// never does this.
        /// </summary>
        private static void SetPrivateField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[Overrun] {target.GetType().Name} has no serialized field '{field}'");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
