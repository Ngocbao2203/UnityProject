#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ItemData))]
public class ItemDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Vẽ inspector mặc định trước (id, itemName, icon, ...)
        DrawDefaultInspector();

        // Lấy object đang chọn
        ItemData itemData = (ItemData)target;

        EditorGUILayout.Space();

        // Nút regenerate ID
        if (GUILayout.Button("🔄 Regenerate ID"))
        {
            Undo.RecordObject(itemData, "Regenerate Item ID"); // cho phép undo
            itemData.id = System.Guid.NewGuid().ToString();
            EditorUtility.SetDirty(itemData); // mark dirty để Unity lưu
            Debug.Log($"[ItemData] Regenerated ID for {itemData.itemName} → {itemData.id}");
        }
    }
}
#endif
