using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Overrun.Core;
using Overrun.Data;
using Overrun.Net;
using Overrun.Simulation;

namespace Overrun.EditorTools
{
    /// <summary>
    /// VS001 combat pass: weapon and enemy definitions, the enemy prefab, navmesh, spawn
    /// zones and the wave director. Idempotent — safe to re-run.
    ///
    /// -executeMethod Overrun.EditorTools.VS001Combat.RunAll
    /// </summary>
    public static class VS001Combat
    {
        private const string DefDir    = "Assets/Content/Definitions";
        private const string PrefabDir = "Assets/Content/Prefabs";
        private const string SceneDir  = "Assets/Content/Scenes";

        private const string WeaponPath = DefDir + "/WeaponDef_Sidearm.asset";
        private const string EnemyDefPath = DefDir + "/EnemyDef_Basic.asset";
        private const string EnemyPrefabPath = PrefabDir + "/Enemy_Basic.prefab";
        private const string PawnPath = PrefabDir + "/PlayerPawn.prefab";

        [MenuItem("Overrun/Run VS001 Combat Pass")]
        public static void RunAll()
        {
            if (!Directory.Exists(DefDir)) Directory.CreateDirectory(DefDir);

            WeaponDefinition weapon = EnsureWeapon();
            EnemyDefinition enemyDef = EnsureEnemyDefinition();
            GameObject enemyPrefab = EnsureEnemyPrefab(enemyDef);

            ArmPawnPrefab(weapon);
            WireWorldScene(enemyPrefab, enemyDef);
            RegisterEnemyPrefab(enemyPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Overrun] VS001 combat pass complete.");
        }

        // ------------------------------------------------------------------ definitions

        private static WeaponDefinition EnsureWeapon()
        {
            var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(def, WeaponPath);
                Debug.Log($"[Overrun] Created {WeaponPath}");
            }

            def.DefinitionId = StableId("weapon.sidearm");
            def.DisplayName = "Service Sidearm";
            def.Tags = Tag.Weapon | Tag.Hitscan;
            def.Damage = 26f;
            def.CritChance = 0.08f;
            def.CritMultiplier = 2f;
            def.FireInterval = 0.15f;
            def.PelletCount = 1;
            def.Spread = 0.5f;
            def.Range = 120f;
            def.IsHitscan = true;
            def.MagazineSize = 12;
            def.ReserveAmmo = 144;
            def.ReloadSeconds = 1.3f;

            EditorUtility.SetDirty(def);
            return def;
        }

        private static EnemyDefinition EnsureEnemyDefinition()
        {
            var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyDefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<EnemyDefinition>();
                AssetDatabase.CreateAsset(def, EnemyDefPath);
                Debug.Log($"[Overrun] Created {EnemyDefPath}");
            }

            def.DefinitionId = StableId("enemy.basic");
            def.DisplayName = "Sundered";
            def.Archetype = EnemyArchetype.Basic;
            def.MaxHealth = 60f;
            def.Armor = 0f;
            def.MoveSpeed = 3.2f;
            def.TurnSpeed = 240f;
            def.AttackDamage = 12f;
            def.AttackRange = 1.9f;
            def.AttackInterval = 1.1f;
            def.AttackTags = Tag.Melee;
            def.ScripReward = 10;
            def.BudgetCost = 1f;
            def.SelectionWeight = 1f;

            EditorUtility.SetDirty(def);
            return def;
        }

        /// <summary>Deterministic id from a name, so ids stay stable across machines.</summary>
        private static int StableId(string key)
        {
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < key.Length; i++) hash = hash * 31 + key[i];
                return hash;
            }
        }

        // ----------------------------------------------------------------- enemy prefab

        private static GameObject EnsureEnemyPrefab(EnemyDefinition def)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            if (existing != null) return existing;

            var root = new GameObject("Enemy_Basic");

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);

            // Keep the capsule collider: hitscan needs something to trace against, and
            // GetComponentInParent<IDamageable> walks up to the Health on the root.
            var col = body.GetComponent<CapsuleCollider>();
            if (col != null) col.isTrigger = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.height = 2f;
            agent.radius = 0.4f;
            agent.speed = def.MoveSpeed;
            agent.angularSpeed = def.TurnSpeed;
            agent.acceleration = 12f;
            agent.stoppingDistance = 1.4f;

            root.AddComponent<NetworkObject>();

            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;

            var health = root.AddComponent<Health>();
            SetFloat(health, "_maxHealth", def.MaxHealth);

            var enemy = root.AddComponent<Enemy>();
            SetObject(enemy, "_definition", def);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath, out bool ok);
            if (!ok) Debug.LogError($"[Overrun] Failed to save {EnemyPrefabPath}");
            else Debug.Log($"[Overrun] Prefab: {EnemyPrefabPath}");

            Object.DestroyImmediate(root);
            return saved;
        }

        private static void ArmPawnPrefab(WeaponDefinition weapon)
        {
            var pawnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PawnPath);
            if (pawnPrefab == null) { Debug.LogError("[Overrun] PlayerPawn prefab missing."); return; }

            GameObject instance = PrefabUtility.LoadPrefabContents(PawnPath);

            var weaponRuntime = instance.GetComponentInChildren<WeaponRuntime>();
            if (weaponRuntime == null) weaponRuntime = instance.AddComponent<WeaponRuntime>();
            SetObject(weaponRuntime, "_definition", weapon);

            var pawn = instance.GetComponent<PlayerPawn>();
            if (pawn != null)
            {
                SetObject(pawn, "_weapon", weaponRuntime);
                SetObject(pawn, "_startingWeapon", weapon);
            }

            if (instance.GetComponent<Health>() == null) instance.AddComponent<Health>();

            PrefabUtility.SaveAsPrefabAsset(instance, PawnPath);
            PrefabUtility.UnloadPrefabContents(instance);
            Debug.Log("[Overrun] PlayerPawn armed with sidearm.");
        }

        // ------------------------------------------------------------------ world scene

        private static void WireWorldScene(GameObject enemyPrefab, EnemyDefinition enemyDef)
        {
            Scene scene = EditorSceneManager.OpenScene(SceneDir + "/World.unity", OpenSceneMode.Single);

            // The old ArenaSpawnRegistrar class was deleted; strip the orphaned component
            // so the scene does not carry a missing-script reference forever.
            int stripped = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                stripped += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            }
            if (stripped > 0) Debug.Log($"[Overrun] Removed {stripped} missing-script component(s).");

            // Navmesh over the greybox geometry, baked at load.
            GameObject arena = Find(scene, "Arena");
            if (arena != null)
            {
                if (arena.GetComponent<NavMeshSurface>() == null)
                {
                    var surface = arena.AddComponent<NavMeshSurface>();
                    surface.collectObjects = CollectObjects.Children;
                    surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
                }
                if (arena.GetComponent<ArenaNavMesh>() == null) arena.AddComponent<ArenaNavMesh>();
            }

            // Enemy spawn zones: one per room, away from the player start.
            GameObject zonesGo = FindOrCreate(scene, "EnemySpawnZones");
            if (zonesGo.transform.childCount == 0)
            {
                CreateZone(zonesGo.transform, "EnemyZone_A_North", new Vector3(-5f, 0.2f, 6f));
                CreateZone(zonesGo.transform, "EnemyZone_A_East",  new Vector3( 6f, 0.2f, 2f));
                CreateZone(zonesGo.transform, "EnemyZone_B_North", new Vector3( 0f, 0.2f, 28f));
                CreateZone(zonesGo.transform, "EnemyZone_B_West",  new Vector3(-6f, 0.2f, 20f));
            }

            // Wave director.
            GameObject directorGo = FindOrCreate(scene, "WaveDirector");
            var director = directorGo.GetComponent<WaveDirector>();
            if (director == null) director = directorGo.AddComponent<WaveDirector>();
            SetObject(director, "_enemyPrefab", enemyPrefab);
            SetArray(director, "_pool", new Object[] { enemyDef });

            // Registrar ties it all to the session at runtime.
            GameObject spawnsGo = FindOrCreate(scene, "SpawnPoints");
            var registrar = spawnsGo.GetComponent<ArenaRegistrar>();
            if (registrar == null) registrar = spawnsGo.AddComponent<ArenaRegistrar>();

            SetArray(registrar, "_playerSpawns", Children(spawnsGo.transform));
            SetArray(registrar, "_enemySpawnZones", Children(zonesGo.transform));
            SetObject(registrar, "_waveDirector", director);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Overrun] World wired: navmesh, 4 enemy zones, wave director, registrar.");
        }

        private static void RegisterEnemyPrefab(GameObject enemyPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(SceneDir + "/Bootstrap.unity", OpenSceneMode.Single);

            GameObject nmGo = Find(scene, "NetworkManager");
            var nm = nmGo != null ? nmGo.GetComponent<NetworkManager>() : null;
            if (nm == null || enemyPrefab == null) return;

            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();

            bool present = false;
            foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
            {
                if (p != null && p.Prefab == enemyPrefab) { present = true; break; }
            }
            if (!present) nm.AddNetworkPrefab(enemyPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Overrun] Enemy prefab registered as a network prefab.");
        }

        // ---------------------------------------------------------------------- helpers

        private static void CreateZone(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
        }

        private static Object[] Children(Transform t)
        {
            var list = new List<Object>();
            for (int i = 0; i < t.childCount; i++) list.Add(t.GetChild(i));
            return list.ToArray();
        }

        private static GameObject Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        private static GameObject FindOrCreate(Scene scene, string name)
        {
            GameObject existing = Find(scene, name);
            if (existing != null) return existing;

            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static void SetObject(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Overrun] {target.GetType().Name}.{field} not found"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Overrun] {target.GetType().Name}.{field} not found"); return; }
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Overrun] {target.GetType().Name}.{field} not found"); return; }

            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
