#if UNITY_EDITOR
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using CGP.Framework;              // ApiRoutes, LocalStorageHelper
using CGP.Gameplay.Items;        // ✅ ItemData (định nghĩa ScriptableObject của bạn)

public static class ItemDataUploader
{
    // --- Điều chỉnh nếu BE map enum khác ---
    private static int ToBackendEnum(ItemData.ItemType t) => t switch
    {
        ItemData.ItemType.Tool   => 0,
        ItemData.ItemType.Seed   => 1,
        ItemData.ItemType.Crop   => 2,
        _                        => 3 // Other
    };

    private static string NormalizeGuid(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return System.Guid.NewGuid().ToString();
        return s.Trim().Trim('{', '}').ToLowerInvariant();
    }

    // Bỏ qua cert tự ký khi chạy trong Editor (local dev)
    private class BypassCertificate : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }

    [MenuItem("Tools/Items/Sync Items To DB (Form)")]
    public static async void SyncAll()
    {
        var guids = AssetDatabase.FindAssets("t:ItemData");
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("No ItemData assets found.");
            return;
        }

        int created = 0, updated = 0, failed = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (so == null) continue;

            // Chuẩn hóa + map enum
            var id          = NormalizeGuid(so.id);
            var nameItem    = string.IsNullOrWhiteSpace(so.itemName) ? so.name : so.itemName;
            var description = so.description ?? "";
            var itemTypeInt = ToBackendEnum(so.itemType); // <-- map chuẩn BE
            var isStackable = so.isStackable;

            // build form [FromForm] (PascalCase field name)
            var form = BuildItemForm(id, nameItem, description, itemTypeInt, isStackable);

            // 1) CREATE
            var res = await PostForm(ApiRoutes.Item.CREATE, form);
            if (res.ok)
            {
                created++;
                if (so.id != id) { so.id = id; EditorUtility.SetDirty(so); }
                continue;
            }

            // 2) UPDATE nếu đã tồn tại
            if (res.statusCode == 400 || res.statusCode == 409 || res.statusCode == 422)
            {
                var updForm = BuildItemForm(id, nameItem, description, itemTypeInt, isStackable);
                var upd = await PutForm(ApiRoutes.Item.UPDATE, updForm);
                if (upd.ok)
                {
                    updated++;
                    if (so.id != id) { so.id = id; EditorUtility.SetDirty(so); }
                }
                else
                {
                    failed++;
                    Debug.LogError($"[Item Sync][UPDATE FAIL] {so.name} -> {upd.statusCode} {upd.error}\n{upd.body}");
                }
            }
            else
            {
                failed++;
                Debug.LogError($"[Item Sync][CREATE FAIL] {so.name} -> {res.statusCode} {res.error}\n{res.body}");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Item Sync] Created: {created}, Updated: {updated}, Failed: {failed}");
    }

    // ===== Helpers =====

    private static WWWForm BuildItemForm(string id, string nameItem, string description, int itemType, bool isStackable)
    {
        var f = new WWWForm();
        f.AddField("Id", id);
        f.AddField("NameItem", nameItem);
        f.AddField("Description", description);
        f.AddField("ItemType", itemType.ToString());     // gửi SỐ đã map
        f.AddField("IsStackable", isStackable ? "true" : "false");
        return f;
    }

    private static async Task<(bool ok, long statusCode, string body, string error)> PostForm(string url, WWWForm form)
    {
        using var req = UnityWebRequest.Post(url, form);
        req.downloadHandler   = new DownloadHandlerBuffer();
        req.certificateHandler = new BypassCertificate();
        req.SetRequestHeader("Accept", "application/json");

        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        bool success = req.result == UnityWebRequest.Result.Success &&
                       req.responseCode >= 200 && req.responseCode < 300;

        return (success, req.responseCode, req.downloadHandler.text, req.error);
    }

    // Unity không có helper PUT form-data → tự set method + header từ WWWForm
    private static async Task<(bool ok, long statusCode, string body, string error)> PutForm(string url, WWWForm form)
    {
        byte[] body = form.data;
        using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
        req.uploadHandler    = new UploadHandlerRaw(body);
        req.downloadHandler  = new DownloadHandlerBuffer();
        req.certificateHandler = new BypassCertificate();

        foreach (var h in form.headers) req.SetRequestHeader(h.Key, h.Value); // Content-Type (boundary)
        req.SetRequestHeader("Accept", "application/json");

        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        bool success = req.result == UnityWebRequest.Result.Success &&
                       req.responseCode >= 200 && req.responseCode < 300;

        return (success, req.responseCode, req.downloadHandler.text, req.error);
    }
}
#endif
