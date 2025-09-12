using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using CGP.Framework;               // ApiRoutes, LocalStorageHelper
using CGP.Networking.DTOs;        // ApiEnvelope / ApiListEnvelope / ItemDto

namespace CGP.Networking.Clients
{
    public static class ItemApiClient
    {
        /// <summary>
        /// GET danh sách tất cả item.
        /// Trả về ApiListEnvelope&lt;ItemDto&gt; (tức data là ItemDto[]).
        /// </summary>
        public static IEnumerator GetAllItems(
            Action<ApiListEnvelope<ItemDto>> onOk,
            Action<string> onErr = null)
        {
            var url = ApiRoutes.Item.GET_ALL;

            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 12;

                // Headers
                req.SetRequestHeader("Content-Type", "application/json");
                var token = LocalStorageHelper.GetToken();
                if (!string.IsNullOrEmpty(token))
                    req.SetRequestHeader("Authorization", "Bearer " + token);

                // Bảo đảm có DownloadHandlerBuffer (thường GET tự set, nhưng set tay cho chắc)
                if (req.downloadHandler == null)
                    req.downloadHandler = new DownloadHandlerBuffer();

                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                var isOk = req.result == UnityWebRequest.Result.Success;
#else
                var isOk = !req.isNetworkError && !req.isHttpError;
#endif

                if (!isOk)
                {
                    onErr?.Invoke($"[ItemApiClient] HTTP {(long)req.responseCode} - {req.error}");
                    yield break;
                }

                var json = req.downloadHandler != null ? (req.downloadHandler.text ?? string.Empty) : string.Empty;
                if (string.IsNullOrEmpty(json))
                {
                    onErr?.Invoke("[ItemApiClient] Empty response body.");
                    yield break;
                }

                try
                {
                    var env = JsonUtility.FromJson<ApiListEnvelope<ItemDto>>(json);
                    if (env == null)
                    {
                        onErr?.Invoke("[ItemApiClient] Parse result is null.");
                        yield break;
                    }
                    onOk?.Invoke(env);
                }
                catch (Exception ex)
                {
                    onErr?.Invoke($"[ItemApiClient] Parse error: {ex.Message}\nRaw: {json}");
                }
            }
        }
    }
}
