using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

#region ===== Envelopes / DTOs khớp BE =====
[Serializable]
public class ApiEnvelope                    // Cho Plow/Plant/Water/Harvest (data thường = null)
{
    public int error;                       // 0 = OK
    public string message;
    public int count;
    // public object data; // không cần map khi BE không trả data
}

// GET Farmlands trả mảng plot
[Serializable]
public class FarmlandPlotDto
{
    public string id;        // optional
    public string userId;    // optional
    public int tileId;
    public bool watered;
    public string status;    // "Empty" | "Plowed" | "Planted" | "Watered" | "Harvestable"
    // có thể bổ sung: public string plantedAt; public string waterExpiresAt; ...
}

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
    // ===== Helpers =====
    private static void LogForm(string tag, string url, WWWForm form, string method)
    {
        try
        {
            var fields = form == null ? "null" : string.Join(", ",
                form.headers != null ? Array.Empty<string>() : Array.Empty<string>()); // chỉ để tránh null-ref, không in headers được
            Debug.Log($"[{tag}] {method} {url} (multipart/form-data)");
        }
        catch { /* ignore */ }
    }

    // Gửi multipart/form-data + Authorization
    // Gửi multipart/form-data + Authorization
    private static IEnumerator SendForm(
        string url,
        WWWForm form,
        Action<ApiEnvelope> onOk,
        Action<string> onErr = null,
        string method = UnityWebRequest.kHttpVerbPOST)
    {
        // UnityWebRequest.Post(...) tự set content type multipart/form-data
        using var req = UnityWebRequest.Post(url, form);
        req.method = method;                        // cho PUT/POST tuỳ endpoint
        req.downloadHandler = new DownloadHandlerBuffer();
        // ❌ bỏ dòng req.chunkedTransfer = false; vì mặc định đã là false

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

    // Gửi GET + Authorization (cho GetFarmlands)
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

    // ===== Endpoints =====

    // GET /api/Farmland/GetFarmlands/{userId}
    public static IEnumerator GetFarmlands(string userId, Action<FarmlandsEnvelope> onDone, Action<string> onErr = null)
    {
        var url = ApiRoutes.Farmland.GET_FARMLANDS.Replace("{userId}", userId);
        return SendGet(url, onDone, onErr ?? (e => Debug.LogError("[GetFarmlands] " + e)));
    }

    // POST /api/Farmland/Plow (form: UserId, TileId)
    public static IEnumerator Plow(string userId, int tileId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);   // PascalCase đúng theo Swagger
        form.AddField("TileId", tileId);
        return SendForm(ApiRoutes.Farmland.PLOW, form, onDone, err => Debug.LogError("[Plow] " + err));
    }

    // PUT /api/Farmland/Plant  (nếu Swagger là POST thì đổi method ở dưới)
    public static IEnumerator Plant(string userId, int tileId, string seedId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("TileId", tileId);
        form.AddField("SeedId", seedId);
        // nếu BE để POST: đổi UnityWebRequest.kHttpVerbPUT -> UnityWebRequest.kHttpVerbPOST
        return SendForm(ApiRoutes.Farmland.PLANT, form, onDone, err => Debug.LogError("[Plant] " + err), UnityWebRequest.kHttpVerbPUT);
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
