using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using CGP.Framework;
using CGP.Networking.DTOs;

namespace CGP.Networking.Clients
{
    public class QuestClient
    {
        static string Clip(string s, int max = 300)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...(trim)";
        }

        static void AttachJson(UnityWebRequest req)
        {
            req.SetRequestHeader("Content-Type", "application/json");
            var token = CGP.Framework.LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        static void AttachAuth(UnityWebRequest req)
        {
            var token = CGP.Framework.LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        static async Task<UnityWebRequest> Send(UnityWebRequest req)
        {
            Debug.Log($"[QuestClient] {req.method} {req.url}");
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            var code = req.responseCode;
            var ok = req.result == UnityWebRequest.Result.Success;
            var body = req.downloadHandler != null ? req.downloadHandler.text : "";
            Debug.Log($"[QuestClient] <= code={code}, ok={ok}, error='{req.error}', body='{Clip(body)}'");
            return req;
        }

        public async Task<List<QuestMeta>> GetAllMetas()
        {
            using var req = UnityWebRequest.Get(ApiRoutes.Quest.GET_ALL_QUESTS);
            AttachJson(req);
            var r = await Send(req);
            try
            {
                var parsed = JsonUtility.FromJson<QuestMetaListResponse>(r.downloadHandler.text);
                var count = parsed?.data?.Count ?? 0;
                Debug.Log($"[QuestClient] GetAllMetas -> {count} metas");
                return parsed?.data ?? new List<QuestMeta>();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[QuestClient] GetAllMetas parse error: {e}");
                return new List<QuestMeta>();
            }
        }

        public async Task<List<UserQuestState>> GetUserStates(string userId)
        {
            var url = ApiRoutes.Quest.GET_USER_QUESTS.Replace("{userId}", userId);
            using var req = UnityWebRequest.Get(url);
            AttachJson(req);
            var r = await Send(req);
            try
            {
                var parsed = JsonUtility.FromJson<UserQuestListResponse>(r.downloadHandler.text);
                var count = parsed?.data?.Count ?? 0;
                Debug.Log($"[QuestClient] GetUserStates -> {count} states (userId={userId})");
                return parsed?.data ?? new List<UserQuestState>();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[QuestClient] GetUserStates parse error: {e}");
                return new List<UserQuestState>();
            }
        }

        public async Task<bool> CompleteQuest(string userId, string userQuestId)
        {
            var url = $"{ApiRoutes.Quest.COMPLETE_QUEST}"
                    + $"?UserId={UnityWebRequest.EscapeURL(userId)}"
                    + $"&UserQuestId={UnityWebRequest.EscapeURL(userQuestId)}";

            using var req = UnityWebRequest.PostWwwForm(url, "");
            AttachAuth(req);
            req.downloadHandler = new DownloadHandlerBuffer();

            var r = await Send(req);
            if (r.result != UnityWebRequest.Result.Success) return false;

            var body = r.downloadHandler.text;
            var looksLikeJson = body.StartsWith("{") || body.StartsWith("[");
            if (!looksLikeJson)
            {                   // khi BE còn trả stacktrace
                Debug.LogError("[QuestClient] CompleteQuest: server returned non-JSON body");
                return false;
            }

            var res = JsonUtility.FromJson<BasicResponse>(body);
            return res != null && res.error == 0;
        }
    }
}
