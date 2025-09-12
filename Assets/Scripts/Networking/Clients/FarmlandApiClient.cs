using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using CGP.Framework;            // ApiRoutes, LocalStorageHelper
using CGP.Networking.DTOs;     // FarmlandPlotDto, FarmlandCropDto

namespace CGP.Networking.Clients
{
    #region ===== Envelopes khớp BE =====
    [Serializable]
    public class ApiEnvelope
    {
        public int error;       // 0 = OK
        public string message;
        public int count;
        // public object data;  // nếu BE sau này trả gì đó thì thêm
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
        // ---------- Core helpers ----------
        private static void AttachAuth(UnityWebRequest req)
        {
            var token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", "Bearer " + token);
        }

        private static IEnumerator SendForm(
            string url,
            WWWForm form,
            Action<ApiEnvelope> onOk,
            Action<string> onErr,
            string method = UnityWebRequest.kHttpVerbPOST)
        {
            var req = UnityWebRequest.Post(url, form);
            req.method = method; // PUT/POST
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachAuth(req);

            yield return req.SendWebRequest();

            var resp = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (req.result == UnityWebRequest.Result.Success)
            {
                ApiEnvelope env = null;
                try { env = JsonUtility.FromJson<ApiEnvelope>(resp); }
                catch (Exception ex)
                {
                    onErr?.Invoke($"[FarmlandApi] Parse error: {ex.Message}\nResp: {resp}");
                    yield break;
                }
                onOk?.Invoke(env);
            }
            else
            {
                onErr?.Invoke($"[FarmlandApi] HTTP {(long)req.responseCode} {req.error}\nResp: {resp}");
            }

#if UNITY_EDITOR
            req.Dispose();
#endif
        }

        private static IEnumerator SendGet(
            string url,
            Action<FarmlandsEnvelope> onOk,
            Action<string> onErr)
        {
            var req = UnityWebRequest.Get(url);
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachAuth(req);

            yield return req.SendWebRequest();

            var resp = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (req.result == UnityWebRequest.Result.Success)
            {
                FarmlandsEnvelope env = null;
                try { env = JsonUtility.FromJson<FarmlandsEnvelope>(resp); }
                catch (Exception ex)
                {
                    onErr?.Invoke($"[FarmlandApi] Parse error: {ex.Message}\nResp: {resp}");
                    yield break;
                }
                onOk?.Invoke(env);
            }
            else
            {
                onErr?.Invoke($"[FarmlandApi] HTTP {(long)req.responseCode} {req.error}\nResp: {resp}");
            }

#if UNITY_EDITOR
            req.Dispose();
#endif
        }

        // ---------- Endpoints ----------

        /// GET /api/Farmland/GetFarmlands/{userId}
        public static IEnumerator GetFarmlands(string userId, Action<FarmlandsEnvelope> onDone, Action<string> onErr = null)
        {
            var url = ApiRoutes.Farmland.GET_FARMLANDS.Replace("{userId}", userId);
            yield return SendGet(url, onDone, onErr ?? (e => Debug.LogError("[GetFarmlands] " + e)));
        }

        /// POST /api/Farmland/Plow  (form: UserId, TileId)
        public static IEnumerator Plow(string userId, int tileId, Action<ApiEnvelope> onDone, Action<string> onErr = null)
        {
            var f = new WWWForm();
            f.AddField("UserId", userId);
            f.AddField("TileId", tileId);
            yield return SendForm(ApiRoutes.Farmland.PLOW, f, onDone, onErr ?? (e => Debug.LogError("[Plow] " + e)));
        }

        /// PUT /api/Farmland/Plant (form: UserId, TileId, ItemId[, NextWaterDueAtUtc])
        /// Nếu BE là POST, đổi method về UnityWebRequest.kHttpVerbPOST.
        public static IEnumerator Plant(string userId, int tileId, string itemId, Action<ApiEnvelope> onDone, Action<string> onErr = null, DateTime? nextWaterDueUtc = null)
        {
            var f = new WWWForm();
            f.AddField("UserId", userId);
            f.AddField("TileId", tileId);
            f.AddField("ItemId", itemId);
            if (nextWaterDueUtc.HasValue)
                f.AddField("NextWaterDueAtUtc", nextWaterDueUtc.Value.ToUniversalTime().ToString("o"));

            yield return SendForm(ApiRoutes.Farmland.PLANT, f, onDone, onErr ?? (e => Debug.LogError("[Plant] " + e)), UnityWebRequest.kHttpVerbPUT);
        }

        /// PUT /api/Farmland/Water (form: UserId, TileId)
        public static IEnumerator Water(string userId, int tileId, Action<ApiEnvelope> onDone, Action<string> onErr = null)
        {
            var f = new WWWForm();
            f.AddField("UserId", userId);
            f.AddField("TileId", tileId);
            yield return SendForm(ApiRoutes.Farmland.WATER, f, onDone, onErr ?? (e => Debug.LogError("[Water] " + e)), UnityWebRequest.kHttpVerbPUT);
        }

        /// POST /api/Farmland/Harvest (form: UserId, TileId)
        public static IEnumerator Harvest(string userId, int tileId, Action<ApiEnvelope> onDone, Action<string> onErr = null)
        {
            var f = new WWWForm();
            f.AddField("UserId", userId);
            f.AddField("TileId", tileId);
            yield return SendForm(ApiRoutes.Farmland.HARVEST, f, onDone, onErr ?? (e => Debug.LogError("[Harvest] " + e)));
        }
    }
}
