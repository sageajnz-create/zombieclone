using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;

namespace Overrun.EditorTools
{
    /// <summary>Read-only fact gathering for the GlobalObjectIdHash problem.</summary>
    public static class NetworkPrefabDiagnostic
    {
        [MenuItem("Overrun/Diagnose Network Prefab Hashes")]
        public static void Run()
        {
            foreach (string path in new[]
            {
                "Assets/Content/Prefabs/PlayerPawn.prefab",
                "Assets/Content/Prefabs/Enemy_Basic.prefab",
            })
            {
                var sb = new StringBuilder();
                sb.AppendLine($"--- {path}");

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine($"    asset loaded: {go != null}");
                if (go == null) { Debug.Log(sb.ToString()); continue; }

                var no = go.GetComponent<NetworkObject>();
                sb.AppendLine($"    NetworkObject: {no != null}");
                if (no == null) { Debug.Log(sb.ToString()); continue; }

                FieldInfo field = typeof(NetworkObject).GetField("GlobalObjectIdHash",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                sb.AppendLine($"    field found: {field != null}  value: {(field != null ? field.GetValue(no) : "n/a")}");

                var so = new SerializedObject(no);
                SerializedProperty prop = so.FindProperty("GlobalObjectIdHash");
                sb.AppendLine($"    SerializedProperty found: {prop != null}");
                if (prop != null)
                    sb.AppendLine($"    propertyType={prop.propertyType} longValue={prop.longValue} uintValue={prop.uintValue}");

                MethodInfo onValidate = typeof(NetworkObject).GetMethod("OnValidate",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                sb.AppendLine($"    OnValidate method found: {onValidate != null}");

                sb.AppendLine($"    GlobalObjectId: {GlobalObjectId.GetGlobalObjectIdSlow(no)}");
                sb.AppendLine($"    IsPartOfPrefabAsset: {PrefabUtility.IsPartOfPrefabAsset(go)}");

                Debug.Log(sb.ToString());
            }
        }
    }
}
