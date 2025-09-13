using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

using CGP.Framework;                       // LocalStorageHelper
using ApiRoutes = CGP.Framework.ApiRoutes;

namespace CGP.Gameplay.Inventory.Presenter
{
    public partial class InventoryManager
    {
        // ==== header helpers ====
        private static void AttachJson(UnityWebRequest req)
        {
            req.SetRequestHeader("Content-Type", "application/json");
            var token = CGP.Framework.LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
        }
        private static void AttachAuth(UnityWebRequest req)
        {
            var token = CGP.Framework.LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        private static async Task<HttpResult> Send(UnityWebRequest req)
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            return new HttpResult
            {
                ok = req.result == UnityWebRequest.Result.Success,
                code = req.responseCode,
                body = req.downloadHandler != null ? (req.downloadHandler.text ?? "") : "",
                error = req.error ?? ""
            };
        }

        // ==== Queries ====
        private async Task<List<InventoryItem>> FetchInventoryData(string userId)
        {
            var list = new List<InventoryItem>();
            try
            {
                string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);
                using var req = UnityWebRequest.Get(url);
                AttachJson(req);
                var res = await Send(req);
                if (!res.ok) return list;
                var parsed = JsonUtility.FromJson<InventoryResponse>(res.body);
                if (parsed?.data != null) list = new List<InventoryItem>(parsed.data);
            }
            catch { }
            return list;
        }

        private async Task<string> PostCreate(string userId, string itemId, int qty, string inventoryName, int slotIndex, int? quality = null)
        {
            if (slotIndex < 0) return null;

            string url = ApiRoutes.Inventory.ADD_ITEM_TO_INVENTORY;
            var form = new WWWForm();
            form.AddField("UserId", userId);
            form.AddField("ItemId", itemId);
            form.AddField("Quantity", qty);
            form.AddField("InventoryType", inventoryName);
            form.AddField("SlotIndex", slotIndex);
            if (quality.HasValue) form.AddField("Quality", quality.Value);

            using var req = UnityWebRequest.Post(url, form);
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachAuth(req);

            var res = await Send(req);
            if (!res.ok) return null;

            string newId = null;
            try
            {
                var single = JsonUtility.FromJson<InventorySingleResponse>(res.body);
                newId = single?.data?.id;
                if (string.IsNullOrEmpty(newId))
                {
                    var m = Regex.Match(res.body ?? "", "\"id\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) newId = m.Groups[1].Value;
                }
            }
            catch { }

            return newId;
        }

        private async Task<HttpResult> PutUpdate(UpdateDto dto, int? quality = null)
        {
            string url = ApiRoutes.Inventory.UPDATE_ITEM;

            var form = new WWWForm();
            form.AddField("Id", dto.Id ?? "");
            form.AddField("UserId", dto.UserId ?? "");
            form.AddField("ItemId", dto.ItemId ?? "");
            form.AddField("Quantity", dto.Quantity);
            form.AddField("InventoryType", dto.InventoryType ?? "");
            form.AddField("SlotIndex", dto.SlotIndex);
            if (quality.HasValue) form.AddField("Quality", quality.Value);

            var req = UnityWebRequest.Post(url, form);
            req.method = UnityWebRequest.kHttpVerbPUT;
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachAuth(req);
            return await Send(req);
        }

        private async Task<bool> DeleteRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return false;
            string url = ApiRoutes.Inventory.DELETE_ITEM.Replace("{inventoryId}", recordId);
            using var req = UnityWebRequest.Delete(url);
            AttachAuth(req);
            var res = await Send(req);
            return res.ok;
        }

        private async Task<Dictionary<string, string>> FetchRecordMapBySlot(string userId)
        {
            var map = new Dictionary<string, string>();
            string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);

            using var req = UnityWebRequest.Get(url);
            AttachJson(req);

            var res = await Send(req);
            if (!res.ok) return map;

            try
            {
                var resp = JsonUtility.FromJson<InventoryResponse>(res.body);
                if (resp?.data != null)
                {
                    foreach (var it in resp.data)
                    {
                        var invName = string.Equals(it.inventoryType, TOOLBAR, System.StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK;
                        map[$"{invName}:{it.slotIndex}"] = it.id;
                    }
                }
            }
            catch { }
            return map;
        }
    }
}
