using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

#region ===== Envelopes / DTOs khớp BE =====

// Envelope chung cho các POST/PUT (Plow/Plant/Water/Harvest)
[Serializable]
public class ApiEnvelope
{
    public int error;          // 0 = OK
    public string message;
    public int count;
    // public object data;     // BE hiện không trả data cho các action => bỏ
}

// Plot (ô ruộng) trong GetFarmlands
[Serializable]
public class FarmlandPlotDto
{
    public string id;
    public string userId;
    public int tileId;
    public bool watered;
    public string status;          // "Empty" | "Plowed" | "Planted" | ...
    public string plantedAt;       // <- string thay vì DateTime?
    public string createdAtUtc;    // <- string
    public List<FarmlandCropDto> farmlandCrops;
}

// Crop (cây đang trồng) đi kèm trong plot
[Serializable]
public class FarmlandCropDto
{
    public string id;
    public int tileId;
    public string seedId;
    public string userId;

    public int stage;
    public bool needsWater;

    public string nextWaterDueAtUtc; // <- string
    public string stageEndsAtUtc;    // <- string
    public string harvestableAtUtc;  // <- string
    public string plantedAtUtc;      // <- string
    public string harvestedAtUtc;    // <- string
    public bool isActive;

    public ItemLiteDto item;
}

[Serializable]
public class ItemLiteDto
{
    public string id;
    public string nameItem;
    public string description;
    public string itemType;    // "Seed"
    public bool isStackable;
}

// Envelope cho GET Farmlands
[Serializable]
public class FarmlandsEnvelope
{
    public int error;
    public string message;
    public int count;
    public FarmlandPlotDto[] data;
}

#endregion

public static class FarmlandApiClient
{
    // ---------- Helpers ----------
    private static void LogForm(string tag, string url, WWWForm form, string method)
    {
        try
        {
            Debug.Log($"[{tag}] {method} {url} (multipart/form-data)");
        }
        catch { /* ignore */ }
    }

    // multipart/form-data + Authorization
    private static IEnumerator SendForm(
        string url,
        WWWForm form,
        Action<ApiEnvelope> onOk,
        Action<string> onErr = null,
        string method = UnityWebRequest.kHttpVerbPOST)
    {
        using var req = UnityWebRequest.Post(url, form);
        req.method = method;                        // PUT/POST
        req.downloadHandler = new DownloadHandlerBuffer();

        // Authorization
        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", "Bearer " + token);

        LogForm("FarmlandApi", url, form, method);

        yield return req.SendWebRequest();

        var respText = req.downloadHandler != null ? req.downloadHandler.text : "";
        if (req.result == UnityWebRequest.Result.Success)
        {
            try
            {
                var env = JsonUtility.FromJson<ApiEnvelope>(respText);
                onOk?.Invoke(env);
            }
            catch (Exception ex)
            {
                onErr?.Invoke($"Parse JSON failed: {ex.Message}\nResp: {respText}");
                Debug.LogError($"[FarmlandApi] Parse failed\n{respText}");
            }
        }
        else
        {
            onErr?.Invoke($"HTTP {(long)req.responseCode} - {req.error}\nResp: {respText}");
            Debug.LogError($"[FarmlandApi] {method} {url} FAILED {(long)req.responseCode}\n{respText}");
        }
    }

    // GET + Authorization (cho GetFarmlands)
    private static IEnumerator SendGet(
        string url,
        Action<FarmlandsEnvelope> onOk,
        Action<string> onErr = null)
    {
        using var req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerBuffer();

        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", "Bearer " + token);

        Debug.Log($"[FarmlandApi] GET {url}");

        yield return req.SendWebRequest();

        var respText = req.downloadHandler != null ? req.downloadHandler.text : "";
        if (req.result == UnityWebRequest.Result.Success)
        {
            try
            {
                var env = JsonUtility.FromJson<FarmlandsEnvelope>(respText);
                onOk?.Invoke(env);
            }
            catch (Exception ex)
            {
                onErr?.Invoke($"Parse JSON failed: {ex.Message}\nResp: {respText}");
                Debug.LogError($"[FarmlandApi] Parse failed\n{respText}");
            }
        }
        else
        {
            onErr?.Invoke($"HTTP {(long)req.responseCode} - {req.error}\nResp: {respText}");
            Debug.LogError($"[FarmlandApi] GET {url} FAILED {(long)req.responseCode}\n{respText}");
        }
    }

    // ---------- Endpoints ----------

    // GET /api/Farmland/GetFarmlands/{userId}
    public static IEnumerator GetFarmlands(string userId, Action<FarmlandsEnvelope> onDone, Action<string> onErr = null)
    {
        var url = ApiRoutes.Farmland.GET_FARMLANDS.Replace("{userId}", userId);
        return SendGet(url, onDone, onErr ?? (e => Debug.LogError("[GetFarmlands] " + e)));
    }

    // POST /api/Farmland/Plow  (form: UserId, TileId)
    public static IEnumerator Plow(string userId, int tileId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("TileId", tileId);
        return SendForm(ApiRoutes.Farmland.PLOW, form, onDone, err => Debug.LogError("[Plow] " + err));
    }

    // PUT /api/Farmland/Plant (form: UserId, TileId, ItemId[, NextWaterDueAtUtc])
    public static IEnumerator Plant(
        string userId,
        int tileId,
        string itemId,
        Action<ApiEnvelope> onDone,
        DateTime? nextWaterDueUtc = null)
    {
        var url = ApiRoutes.Farmland.PLANT;
        var form = new WWWForm();

        form.AddField("UserId", userId);
        form.AddField("TileId", tileId.ToString());
        form.AddField("ItemId", itemId);

        if (nextWaterDueUtc.HasValue)
            form.AddField("NextWaterDueAtUtc",
                nextWaterDueUtc.Value.ToUniversalTime().ToString("o"));

        yield return SendForm(url, form, onDone, err => Debug.LogError("[Plant] " + err), UnityWebRequest.kHttpVerbPUT);
    }

    // PUT /api/Farmland/Water
    public static IEnumerator Water(string userId, int tileId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("TileId", tileId);
        return SendForm(ApiRoutes.Farmland.WATER, form, onDone, err => Debug.LogError("[Water] " + err), UnityWebRequest.kHttpVerbPUT);
    }

    // POST /api/Farmland/Harvest
    public static IEnumerator Harvest(string userId, int tileId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("TileId", tileId);
        return SendForm(ApiRoutes.Farmland.HARVEST, form, onDone, err => Debug.LogError("[Harvest] " + err));
    }
}
