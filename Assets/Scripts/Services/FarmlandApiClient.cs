using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

#region ===== Envelope từ BE =====
[Serializable]
public class ApiEnvelope
{
    public int error;       // 0 = OK
    public string message;
    public int count;       // có thể = 0
    // public string data;  // tuỳ endpoint; Plow thường trả null → không cần map
}
#endregion

public static class FarmlandApiClient
{
    // Gửi multipart/form-data + Authorization
    private static IEnumerator SendForm(string url,
                                        WWWForm form,
                                        Action<ApiEnvelope> onOk,
                                        Action<string> onErr = null,
                                        string method = UnityWebRequest.kHttpVerbPOST)
    {
        using var req = UnityWebRequest.Post(url, form);
        req.method = method; // phòng khi endpoint là PUT
        req.downloadHandler = new DownloadHandlerBuffer();

        // Authorization header
        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", "Bearer " + token);

        // UnityWebRequest.Post tự set Content-Type: multipart/form-data
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
            }
        }
        else
        {
            onErr?.Invoke($"HTTP {(long)req.responseCode} - {req.error}\nResp: {respText}");
            Debug.LogError($"[FarmlandApi] {method} {url} FAILED {(long)req.responseCode}\n{respText}");
        }
    }

    // ====== Endpoints ======

    // POST /api/Farmland/Plow  (form-data: UserId, TileId)
    public static IEnumerator Plow(string userId, int tileId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);   // PascalCase như Swagger
        form.AddField("TileId", tileId);
        return SendForm(ApiRoutes.Farmland.PLOW, form, onDone, err => Debug.LogError("[Plow] " + err));
    }

    // PUT /api/Farmland/Plant (nếu Swagger là POST thì đổi method về POST)
    public static IEnumerator Plant(string userId, int tileId, string seedId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("TileId", tileId);
        form.AddField("SeedId", seedId);
        // nếu Swagger hiển thị là POST: đổi UnityWebRequest.kHttpVerbPUT -> kHttpVerbPOST
        return SendForm(ApiRoutes.Farmland.PLANT, form, onDone, err => Debug.LogError("[Plant] " + err), UnityWebRequest.kHttpVerbPUT);
    }

    // PUT /api/Farmland/Water (form-data)
    public static IEnumerator Water(string userId, int tileId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("TileId", tileId);
        return SendForm(ApiRoutes.Farmland.WATER, form, onDone, err => Debug.LogError("[Water] " + err), UnityWebRequest.kHttpVerbPUT);
    }

    // POST /api/Farmland/Harvest (form-data)
    public static IEnumerator Harvest(string userId, int tileId, Action<ApiEnvelope> onDone)
    {
        var form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("TileId", tileId);
        return SendForm(ApiRoutes.Farmland.HARVEST, form, onDone, err => Debug.LogError("[Harvest] " + err));
    }
}
