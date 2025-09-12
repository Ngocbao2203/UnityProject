#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public static class RemoveMissingScripts
{
    [MenuItem("Tools/Cleanup/Remove Missing Scripts (Selection)")]
    private static void RemoveOnSelection()
    {
        var objects = Selection.objects;
        int totalRemoved = 0;

        foreach (var obj in objects)
        {
            // Nếu là prefab asset
            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                totalRemoved += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    totalRemoved += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            else if (obj is GameObject go) // object trong scene
            {
                totalRemoved += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    totalRemoved += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
        }

        Debug.Log($"[Cleanup] Removed {totalRemoved} Missing MonoBehaviours.");
        AssetDatabase.SaveAssets();
    }
}
#endif
