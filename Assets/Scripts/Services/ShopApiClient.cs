using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class ApiEnvelope<T>
{
    public int error;
    public string message;
    public int count;
    public T data;
}

[Serializable]
public class InventoryItemDto
{
    public string id;
    public string userId;
    public string itemId;
    public int quantity;
    public string inventoryType;
    public int slotIndex;
    public string acquiredAt;
}

[Serializable]
public class ShopPriceDto
{
    public string itemId;
    public string itemName;   // (có thể null, tuỳ BE)
    public int sellPrice;     // <-- ĐÚNG tên trường từ API
    public string iconUrl;
}

public static class ShopApiClient
{
    // ================= Helpers =================

    private static string ResolveToken()
    {
        var am = AuthManager.Instance;
        if (am == null) return null;

        // 1) Thử property thông dụng
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string[] propNames = { "AccessToken", "Token", "JwtToken", "JWT", "BearerToken" };
        foreach (var pn in propNames)
        {
            var p = am.GetType().GetProperty(pn, flags);
            if (p != null && p.PropertyType == typeof(string))
            {
                var val = p.GetValue(am) as string;
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }

        // 2) Thử method thông dụng
        string[] methodNames = { "GetToken", "GetAccessToken", "GetJwt", "GetJwtToken" };
        foreach (var mn in methodNames)
        {
            var m = am.GetType().GetMethod(mn, flags, null, Type.EmptyTypes, null);
            if (m != null && m.ReturnType == typeof(string))
            {
                var val = m.Invoke(am, null) as string;
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }

        // 3) Fallback: LocalStorageHelper / PlayerPrefs nếu AuthManager của bạn hỗ trợ
        try
        {
            var local = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(local)) return local;
        }
        catch { /* ignore */ }

        return null;
    }

    private static void TrySetAuth(UnityWebRequest req)
    {
        var token = ResolveToken();
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", "Bearer " + token);
    }

    private static bool TryParse<T>(string json, out ApiEnvelope<T> env, out string parseError)
    {
        try
        {
            env = JsonUtility.FromJson<ApiEnvelope<T>>(json);
            parseError = null;
            return true;
        }
        catch (Exception e)
        {
            env = null;
            parseError = e.Message;
            return false;
        }
    }

    // ================= READ APIs =================

    /// <summary>Lấy danh sách sản phẩm đang treo bán (bảng shop price).</summary>
    public static IEnumerator GetItemsSell(Action<ApiEnvelope<ShopPriceDto[]>> onOk, Action<string> onErr = null)
    {
        var url = ApiRoutes.ShopPrice.GET_ITEMS_SELL;
        using (var req = UnityWebRequest.Get(url))
        {
            TrySetAuth(req);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<ShopPriceDto[]> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }
    }

    /// <summary>Danh sách item trong Backpack (để kiểm tra có gì để bán).</summary>
    public static IEnumerator GetItemsInBackpack(string userId, Action<ApiEnvelope<InventoryItemDto[]>> onOk, Action<string> onErr = null)
    {
        var url = ApiRoutes.ShopPrice.GET_ITEMS_IN_BACKPACK.Replace("{userId}", userId);
        using (var req = UnityWebRequest.Get(url))
        {
            TrySetAuth(req);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<InventoryItemDto[]> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }
    }

    /// <summary>Lấy chi tiết một item trong shop theo id (nếu BE có).</summary>
    public static IEnumerator GetItemInShop(string id, Action<ApiEnvelope<ShopPriceDto>> onOk, Action<string> onErr = null)
    {
        var url = ApiRoutes.ShopPrice.GET_ITEM_IN_SHOP.Replace("{id}", id);
        using (var req = UnityWebRequest.Get(url))
        {
            TrySetAuth(req);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onErr?.Invoke($"{req.responseCode} {req.error}"); yield break; }

            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<ShopPriceDto> env, out var perr))
            { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

            onOk?.Invoke(env);
        }
    }

    // ================= WRITE APIs (JSON body) =================

    /// <summary>Đăng sản phẩm lên shop: { ItemId, Price }.</summary>
    public static IEnumerator AddItemToShop(string itemId, int price,
                                            Action<ApiEnvelope<object>> onOk, Action<string> onErr = null)
    {
        var url = ApiRoutes.ShopPrice.ADD_ITEM_TO_SHOP;

        var payload = new { ItemId = itemId, Price = price };
        var json = JsonUtility.ToJson(payload);

        var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        TrySetAuth(req);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        { onErr?.Invoke($"{req.responseCode} {req.error} | {req.downloadHandler.text}"); yield break; }

        if (!TryParse(req.downloadHandler.text, out ApiEnvelope<object> env, out var perr))
        { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

        onOk?.Invoke(env);
    }

    /// <summary>Cập nhật giá trong shop: { Id, Price } (tuỳ BE, có thể là ItemId + Price).</summary>
    public static IEnumerator UpdateItemInShop(string idOrItemId, int price,
                                               Action<ApiEnvelope<object>> onOk, Action<string> onErr = null)
    {
        var url = ApiRoutes.ShopPrice.UPDATE_ITEM_IN_SHOP;

        var payload = new { Id = idOrItemId, Price = price };
        var json = JsonUtility.ToJson(payload);

        var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        TrySetAuth(req);

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
        var req = UnityWebRequest.Delete(url);
        TrySetAuth(req);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        { onErr?.Invoke($"{req.responseCode} {req.error} | {req.downloadHandler.text}"); yield break; }

        if (!TryParse(req.downloadHandler.text, out ApiEnvelope<object> env, out var perr))
        { onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}"); yield break; }

        onOk?.Invoke(env);
    }

    /// <summary>Bán item từ Backpack: { UserId, ItemId, Quantity } (JSON).</summary>
    public static IEnumerator SellItem(string userId, string itemId, int quantity,
                                   Action<ApiEnvelope<object>> onOk, Action<string> onErr = null)
    {
        var url = ApiRoutes.ShopPrice.SELL_ITEM;

        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("ItemId", itemId);
        form.AddField("Quantity", quantity);

        using var req = UnityWebRequest.Post(url, form);
        TrySetAuth(req); // thêm Bearer token

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            if (!TryParse(req.downloadHandler.text, out ApiEnvelope<object> env, out var perr))
                onErr?.Invoke($"Parse error: {perr} | raw: {req.downloadHandler.text}");
            else onOk?.Invoke(env);
        }
        else
        {
            onErr?.Invoke($"HTTP {req.responseCode} {req.error} | {req.downloadHandler.text}");
        }
    }
}
