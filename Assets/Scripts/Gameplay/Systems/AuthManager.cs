using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using CGP.Framework;                      // ApiRoutes, LocalStorageHelper
using CGP.Gameplay.Auth;                  // UserData (giữ cùng namespace)
using CGP.Gameplay.Items;                 // (nếu không dùng có thể bỏ)
using CGP.Gameplay.InventorySystem;       // (nếu không dùng có thể bỏ)
using CGP.Gameplay.Inventory.Presenter;   // (nếu không dùng có thể bỏ)

namespace CGP.Gameplay.Auth
{
    [DefaultExecutionOrder(-100)]
    public class AuthManager : MonoBehaviour
    {
        private const string GET_CURRENT_USER_URL = ApiRoutes.Auth.GET_CURRENT_USER;
        private const string PREFS_TOKEN_KEY = "token";

        // ==== Event ====
        public delegate void UserInfoResult(bool success, string message, UserData userData);
        public event UserInfoResult OnUserInfoReceived;

        public static AuthManager Instance { get; private set; }

#if UNITY_EDITOR
        [Header("Editor Testing")]
        [Tooltip("Bật để dùng JWT + userId ngay trong Editor (không cần UI login).")]
        public bool useEditorMockAuth = true;

        [Tooltip("JWT hợp lệ (copy từ Swagger/web).")]
        [TextArea(3, 8)] public string editorJwtToken = "";

        [Tooltip("userId khớp với JWT ở trên.")] 
        public string editorUserId = "dev-user-id";

        [Space] 
        [Tooltip("Không gọi server, tạo user giả để test UI/logic.")]
        public bool editorOfflineMode = false;
#endif

        // ==== State ====
        private UserData currentUserData;
        public bool IsUserDataReady { get; private set; }
        private bool isLoading = false;
        private string cachedJwtToken = null;
        private Coroutine runningRoutine;

        // ==== Public getters ====
        public string GetCurrentUserId()
        {
            if (!IsUserDataReady || currentUserData == null) return null;
            return currentUserData.id;
        }

        public UserData GetCurrentUserData() => IsUserDataReady ? currentUserData : null;

        public string GetJwtToken() => cachedJwtToken;

#if UNITY_EDITOR
        public bool IsOfflineMode => editorOfflineMode;
#else
        public bool IsOfflineMode => false;
#endif

        // ====================== Lifecycle ======================
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            TryIngestTokenFromUrl();

#if UNITY_EDITOR
            if (IsOfflineMode)
            {
                currentUserData = new UserData
                {
                    id = string.IsNullOrEmpty(editorUserId) ? "dev-offline-user" : editorUserId,
                    userName = "EditorOffline",
                    email = "offline@example.com",
                    status = "Active",
                    roleId = 0
                };
                cachedJwtToken = null;
                IsUserDataReady = true;
                Debug.Log("[Auth] Editor OFFLINE mode → skip server, set dummy user.");
                SafeFireEvent(true, "EditorOffline", currentUserData);
                return;
            }

            if (useEditorMockAuth && !string.IsNullOrWhiteSpace(editorJwtToken) && !string.IsNullOrWhiteSpace(editorUserId))
            {
                cachedJwtToken = editorJwtToken.Trim();
                PlayerPrefs.SetString(PREFS_TOKEN_KEY, cachedJwtToken);
                PlayerPrefs.Save();
                Debug.Log("[Auth] Editor MOCK enabled → have JWT & userId. Will try fetch user info.");
            }
#endif
            runningRoutine = StartCoroutine(GetCurrentUser());
        }

        private void OnDestroy()
        {
            if (runningRoutine != null) StopCoroutine(runningRoutine);
            runningRoutine = null;
        }

        // ====================== Core ======================
        public IEnumerator GetCurrentUser(int maxRetries = 3)
        {
            if (IsOfflineMode) yield break;
            if (isLoading) yield break;
            isLoading = true;

            int retries = 0;

            while (true)
            {
                string token = ResolveToken();
                if (string.IsNullOrEmpty(token))
                {
                    isLoading = false;
                    SafeFireEvent(false, "Token not found", null);
                    yield break;
                }

                using (var request = UnityWebRequest.Get(GET_CURRENT_USER_URL))
                {
                    request.timeout = 10;
                    request.SetRequestHeader("Authorization", "Bearer " + token);
                    yield return request.SendWebRequest();

                    bool shouldRetry = false;
                    float retryDelay = 0f;

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (request.responseCode == 401)
                        {
                            isLoading = false;
                            SafeFireEvent(false, "Token expired, please log in again", null);
                            yield break;
                        }

                        retries++;
                        if (retries <= maxRetries)
                        {
                            shouldRetry = true;
                            retryDelay = Mathf.Pow(2f, retries) * 0.35f + UnityEngine.Random.Range(0f, 0.25f);
                            Debug.LogWarning($"[Auth] Request failed: {request.error}. Retry {retries}/{maxRetries} in {retryDelay:0.00}s");
                        }
                        else
                        {
                            isLoading = false;
                            SafeFireEvent(false, $"Request failed after {maxRetries} retries: {request.error}", null);
                            yield break;
                        }
                    }
                    else
                    {
                        try
                        {
                            var jsonResponse = request.downloadHandler.text;
                            var response = JsonUtility.FromJson<GetCurrentUserResponse>(jsonResponse);

                            if (response != null && response.error == 0 && response.data != null)
                            {
                                cachedJwtToken = token;
                                currentUserData = response.data;
                                IsUserDataReady = true;

                                isLoading = false;
                                SafeFireEvent(true, response.message, response.data);
                                yield break;
                            }
                            else
                            {
                                isLoading = false;
                                SafeFireEvent(false, response?.message ?? "Invalid response", null);
                                yield break;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[Auth] JSON parse error: {e.Message}");

                            retries++;
                            if (retries <= maxRetries)
                            {
                                shouldRetry = true;
                                retryDelay = Mathf.Pow(2f, retries) * 0.35f + UnityEngine.Random.Range(0f, 0.25f);
                            }
                            else
                            {
                                isLoading = false;
                                SafeFireEvent(false, "Invalid server response", null);
                                yield break;
                            }
                        }
                    }

                    if (shouldRetry)
                    {
                        yield return new WaitForSecondsRealtime(retryDelay);
                        continue;
                    }
                }
            }
        }

        public string ResolveToken()
        {
#if UNITY_EDITOR
            if (useEditorMockAuth && !string.IsNullOrWhiteSpace(editorJwtToken))
                return editorJwtToken.Trim();
#endif
            string t = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(t)) return t;

            if (PlayerPrefs.HasKey(PREFS_TOKEN_KEY))
            {
                t = PlayerPrefs.GetString(PREFS_TOKEN_KEY);
                if (!string.IsNullOrEmpty(t)) return t;
            }

            return cachedJwtToken;
        }

        private void SafeFireEvent(bool ok, string msg, UserData data)
        {
            try { OnUserInfoReceived?.Invoke(ok, msg, data); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ====================== Utilities ======================
        public void RefreshUserInfo()
        {
            if (IsOfflineMode) return;
            if (runningRoutine != null) StopCoroutine(runningRoutine);
            runningRoutine = StartCoroutine(GetCurrentUser());
        }

        public void ClearUserData()
        {
            currentUserData = null;
            IsUserDataReady = false;
            cachedJwtToken = null;
            Debug.Log("[Auth] User data cleared.");
        }

        public IEnumerator WaitUntilUserReady(float timeoutSeconds = 5f)
        {
            float t = 0f;
            while (!IsUserDataReady && t < timeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        private void TryIngestTokenFromUrl()
        {
            // WebGL + Editor (khi play scene từ URL) đều có thể có absoluteURL
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return;

            int q = url.IndexOf('?');
            if (q < 0) return;

            string query = url.Substring(q + 1);
            foreach (var pair in query.Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    string jwt = UnityEngine.Networking.UnityWebRequest.UnEscapeURL(kv[1]);
                    if (!string.IsNullOrWhiteSpace(jwt))
                    {
                        LocalStorageHelper.SaveToken(jwt);   // lưu cache + storage
                        PlayerPrefs.SetString(PREFS_TOKEN_KEY, jwt); // optional đồng bộ
                        PlayerPrefs.Save();
                        cachedJwtToken = jwt;                // dùng ngay cho request đầu
                        Debug.Log("[Auth] Ingested token from URL param.");
                    }
                    break;
                }
            }
        }

#if UNITY_EDITOR
// tiện test: bấm F9 để dán token từ clipboard
private void Update()
{
    if (Input.GetKeyDown(KeyCode.F9))
    {
        var clip = GUIUtility.systemCopyBuffer;
        if (!string.IsNullOrEmpty(clip))
        {
            LocalStorageHelper.SaveToken(clip.Trim());
            PlayerPrefs.SetString(PREFS_TOKEN_KEY, clip.Trim());
            PlayerPrefs.Save();
            cachedJwtToken = clip.Trim();
            Debug.Log("[Auth] Token pasted from clipboard (F9). Refreshing...");
            RefreshUserInfo();
        }
    }
}
#endif
    }

    // giữ lại DTO response
    [Serializable]
    public class GetCurrentUserResponse
    {
        public int error;
        public string message;
        public UserData data;
    }
}
