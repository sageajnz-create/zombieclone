using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;

namespace Overrun.EditorTools
{
    /// <summary>
    /// Repairs NetworkObject prefab identity for prefabs created by script.
    ///
    /// NGO derives NetworkObject.GlobalObjectIdHash inside OnValidate, which the Editor
    /// calls when a prefab is authored or inspected by hand. A prefab built entirely in
    /// code and saved with PrefabUtility never gets that call, so the hash stays 0 — and
    /// NGO then discards the registration at startup:
    ///
    ///   NetworkPrefab (Enemy_Basic) has a duplicate GlobalObjectIdHash source entry of: 0!
    ///   [Netcode] Removing invalid prefabs from Network Prefab registration
    ///
    /// The consequence is total but silent: nothing spawns. No pawns, no enemies, and no
    /// error at the call site — Spawn() just does nothing useful.
    /// </summary>
    public static class NetworkPrefabTools
    {
        private const string PawnPath = "Assets/Content/Prefabs/PlayerPawn.prefab";
        private const string EnemyPath = "Assets/Content/Prefabs/Enemy_Basic.prefab";

        [MenuItem("Overrun/Repair Network Prefab Hashes")]
        public static void RepairAll()
        {
            EnsureHash(PawnPath);
            EnsureHash(EnemyPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>Returns the resulting hash, or 0 if it could not be established.</summary>
        public static uint EnsureHash(string prefabPath)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (go == null)
            {
                Debug.LogError($"[Overrun] Prefab not found: {prefabPath}");
                return 0u;
            }

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[Overrun] {prefabPath} has no NetworkObject.");
                return 0u;
            }

            string name = System.IO.Path.GetFileName(prefabPath);

            // Check the FILE, not the object. Both the reflected field and SerializedObject
            // mirror the LIVE instance, and OnValidate repopulates the hash every time the
            // Editor loads the prefab — so either of those reports a healthy value while the
            // asset on disk still holds 0. A built player never runs OnValidate, so the 0 is
            // what actually ships.
            uint onDisk = ReadHashFromFile(prefabPath);
            if (onDisk != 0u) return onDisk;

            // The live value is normally already correct; recompute only if it is not.
            uint computed = ReadFieldHash(netObj);
            if (computed == 0u)
            {
                MethodInfo onValidate = typeof(NetworkObject)
                    .GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onValidate != null) onValidate.Invoke(netObj, null);
                computed = ReadFieldHash(netObj);
            }

            if (computed == 0u)
            {
                Debug.LogError($"[Overrun] Could not compute GlobalObjectIdHash for {name}.");
                return 0u;
            }

            // Persist the live state of the prefab asset. SavePrefabAsset is the call that
            // actually writes a prefab asset back out; SetDirty + SaveAssets alone did not.
            EditorUtility.SetDirty(netObj);
            EditorUtility.SetDirty(go);
            PrefabUtility.SavePrefabAsset(go);
            AssetDatabase.SaveAssets();

            uint persisted = ReadHashFromFile(prefabPath);
            if (persisted == 0u)
                Debug.LogError($"[Overrun] {name}: hash {computed} computed but did NOT persist to disk.");
            else
                Debug.Log($"[Overrun] {name} GlobalObjectIdHash persisted = {persisted}");

            return persisted;
        }

        /// <summary>Reads the value actually written in the .prefab file.</summary>
        private static uint ReadHashFromFile(string prefabPath)
        {
            if (!System.IO.File.Exists(prefabPath)) return 0u;

            foreach (string line in System.IO.File.ReadLines(prefabPath))
            {
                int idx = line.IndexOf("GlobalObjectIdHash:", System.StringComparison.Ordinal);
                if (idx < 0) continue;

                string raw = line.Substring(idx + "GlobalObjectIdHash:".Length).Trim();
                return uint.TryParse(raw, out uint v) ? v : 0u;
            }
            return 0u;
        }

        /// <summary>Reads the live field, which OnValidate populates.</summary>
        private static uint ReadFieldHash(NetworkObject netObj)
        {
            FieldInfo field = typeof(NetworkObject).GetField(
                "GlobalObjectIdHash",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            if (field == null) return 0u;

            object value = field.GetValue(netObj);
            return value is uint u ? u : 0u;
        }
    }
}
