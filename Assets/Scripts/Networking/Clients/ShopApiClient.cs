using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using CGP.Framework;            // LocalStorageHelper
using CGP.Networking.DTOs;     // ApiEnvelope<T>, ShopPriceDto, InventoryItemDto

namespace CGP.Networking.Clients
{
    public static class ShopApiClient
    {
        // ========= Helpers =========

        private static void AttachAuth(UnityWebRequest req)
        {
            var token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", "Bearer " + token);
        }

        private static void AttachJson(UnityWebRequest req)
        {
            req.SetRequestHeader("Content-Type", "application/json");
            AttachAuth(req);
        }

        private static bool TryParse<T>(string json, out ApiEnvelope<T> env, out string err)
        {
            try
            {
                env = JsonUtility.FromJson<ApiEnvelope<T>>(json);
                err = null;
                return true;
            }
            catch (Exception e)
            {
                env = null;
                err = e.Message;
                return false;
            }
        }

        // ========= READ =========

        /// <summary>Lấy danh sách sản phẩm đang treo bán.</summary>
        public static IEnumerator GetItemsSell(Action<ApiEnvelope<ShopPriceDto[]>> onOk, Action<string> onErr = null)
        {
            var url = ApiRoutes.ShopPrice.GET_ITEMS_SELL;
            using var req = UnityWebRequest.Get(url);
            AttachAuth(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<ShopPriceDto[]> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }

        /// <summary>Danh sách item trong Backpack.</summary>
        public static IEnumerator GetItemsInBackpack(string userId, Action<ApiEnvelope<InventoryItemDto[]>> onOk, Action<string> onErr = null)
        {
            var url = ApiRoutes.ShopPrice.GET_ITEMS_IN_BACKPACK.Replace("{userId}", userId);
            using var req = UnityWebRequest.Get(url);
            AttachAuth(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<InventoryItemDto[]> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }

        /// <summary>Chi tiết một item trong shop theo id.</summary>
        public static IEnumerator GetItemInShop(string id, Action<ApiEnvelope<ShopPriceDto>> onOk, Action<string> onErr = null)
        {
            var url = ApiRoutes.ShopPrice.GET_ITEM_IN_SHOP.Replace("{id}", id);
            using var req = UnityWebRequest.Get(url);
            AttachAuth(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<ShopPriceDto> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }

        // ========= WRITE =========

        /// <summary>Đăng sản phẩm lên shop: { ItemId, Price }.</summary>
        public static IEnumerator AddItemToShop(string itemId, int price,
                                                Action<ApiEnvelope<object>> onOk, Action<string> onErr = null)
        {
            var url = ApiRoutes.ShopPrice.ADD_ITEM_TO_SHOP;

            var payload = new { ItemId = itemId, Price = price };
            var json = JsonUtility.ToJson(payload);

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachJson(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error} | {req.downloadHandler.text}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<object> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }

        /// <summary>Cập nhật giá: { Id, Price } hoặc { ItemId, Price } tuỳ BE.</summary>
        public static IEnumerator UpdateItemInShop(string idOrItemId, int price,
                                                   Action<ApiEnvelope<object>> onOk, Action<string> onErr = null)
        {
            var url = ApiRoutes.ShopPrice.UPDATE_ITEM_IN_SHOP;

            var payload = new { Id = idOrItemId, Price = price };
            var json = JsonUtility.ToJson(payload);

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachJson(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error} | {req.downloadHandler.text}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<object> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }

        /// <summary>Xoá khỏi shop theo id.</summary>
        public static IEnumerator RemoveItemInShop(string id,
                                                   Action<ApiEnvelope<object>> onOk, Action<string> onErr = null)
        {
            var url = ApiRoutes.ShopPrice.REMOVE_ITEM_IN_SHOP.Replace("{id}", id);
            using var req = UnityWebRequest.Delete(url);
            AttachAuth(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error} | {req.downloadHandler.text}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<object> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }

        /// <summary>Bán item từ Backpack (form): { UserId, ItemId, Quantity }.</summary>
        public static IEnumerator SellItem(string userId, string itemId, int quantity,
                                           Action<ApiEnvelope<object>> onOk, Action<string> onErr = null)
        {
            var url = ApiRoutes.ShopPrice.SELL_ITEM;

            var form = new WWWForm();
            form.AddField("UserId", userId);
            form.AddField("ItemId", itemId);
            form.AddField("Quantity", quantity);

            using var req = UnityWebRequest.Post(url, form);
            AttachAuth(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"HTTP {req.responseCode} {req.error} | {req.downloadHandler.text}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<object> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }
    }
}
