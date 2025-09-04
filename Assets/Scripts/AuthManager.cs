using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

#region DTOs
[Serializable]
public class GetCurrentUserResponse
{
    public int error;
    public string message;
    public UserData data;
}

[Serializable]
public class UserData
{
    public string id;
    public string userName;
    public string email;
    public string phoneNumber;
    public string status;
    public int roleId;
}
#endregion

/// <summary>
/// Quản lý thông tin đăng nhập/người dùng.
/// - Hỗ trợ thử nghiệm trong Editor: Mock token + Offline mode.
/// - Chống gọi trùng, retry kèm backoff & jitter.
/// - Cung cấp sự kiện OnUserInfoReceived cho các hệ thống khác.
/// </summary>
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
    [TextArea(3, 8)]
    public string editorJwtToken = "";

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

    public UserData GetCurrentUserData()
    {
        if (!IsUserDataReady || currentUserData == null) return null;
        return currentUserData;
    }

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

        // Ưu tiên các chế độ Editor
#if UNITY_EDITOR
        if (IsOfflineMode)
        {
            // Tạo user giả, không gọi server
            currentUserData = new UserData
            {
                id = string.IsNullOrEmpty(editorUserId) ? "dev-offline-user" : editorUserId,
                userName = "EditorOffline",
                email = "offline@example.com",
                status = "Active",
                roleId = 0
            };
            cachedJwtToken = null; // không dùng
            IsUserDataReady = true;
            Debug.Log("[Auth] Editor OFFLINE mode → skip server, set dummy user.");
            SafeFireEvent(true, "EditorOffline", currentUserData);
            return;
        }

        if (useEditorMockAuth && !string.IsNullOrWhiteSpace(editorJwtToken) && !string.IsNullOrWhiteSpace(editorUserId))
        {
            cachedJwtToken = editorJwtToken.Trim();
            PlayerPrefs.SetString(PREFS_TOKEN_KEY, cachedJwtToken); // để các nơi khác vẫn lấy được nếu có đọc từ PlayerPrefs
            PlayerPrefs.Save();

            // Vẫn sẽ gọi GET_CURRENT_USER để lấy dữ liệu user chuẩn từ server
            // nhưng nếu backend không cần thì có thể set luôn dữ liệu ở đây.
            Debug.Log("[Auth] Editor MOCK enabled → have JWT & userId. Will try fetch user info.");
        }
#endif

        // Nếu không ở Offline, tiến hành fetch.
        runningRoutine = StartCoroutine(GetCurrentUser());
    }

    private void OnDestroy()
    {
        if (runningRoutine != null) StopCoroutine(runningRoutine);
        runningRoutine = null;
    }

    // ====================== Core ======================
    /// <summary>
    /// Lấy thông tin user từ server. Có retry/backoff. Không gọi nếu đang offline.
    /// </summary>
    public IEnumerator GetCurrentUser(int maxRetries = 3)
    {
        if (IsOfflineMode) yield break;

        if (isLoading)
        {
            Debug.LogWarning("[Auth] GetCurrentUser already running.");
            yield break;
        }
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

    /// <summary>
    /// Thử lấy token theo thứ tự:
    /// - EditorMock (nếu bật)
    /// - LocalStorageHelper.GetToken()
    /// - PlayerPrefs("token")
    /// - cache hiện có
    /// </summary>
    public string ResolveToken()
    {
#if UNITY_EDITOR
        if (useEditorMockAuth && !string.IsNullOrWhiteSpace(editorJwtToken))
            return editorJwtToken.Trim();
#endif

        // Ưu tiên helper (WebGL/Editor có thể khác nhau)
        string t = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(t)) return t;

        // Fallback PlayerPrefs (cho các môi trường Editor)
        if (PlayerPrefs.HasKey(PREFS_TOKEN_KEY))
        {
            t = PlayerPrefs.GetString(PREFS_TOKEN_KEY);
            if (!string.IsNullOrEmpty(t)) return t;
        }

        // Cuối cùng dùng cache nếu có
        return cachedJwtToken;
    }

    private void SafeFireEvent(bool ok, string msg, UserData data)
    {
        try { OnUserInfoReceived?.Invoke(ok, msg, data); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ====================== Utilities ======================
    /// <summary>Cho phép các nơi khác chủ động refresh thông tin người dùng.</summary>
    public void RefreshUserInfo()
    {
        if (IsOfflineMode) return;
        if (runningRoutine != null) StopCoroutine(runningRoutine);
        runningRoutine = StartCoroutine(GetCurrentUser());
    }

    /// <summary>Xoá dữ liệu user hiện tại (đăng xuất cục bộ).</summary>
    public void ClearUserData()
    {
        currentUserData = null;
        IsUserDataReady = false;
        cachedJwtToken = null;
        Debug.Log("[Auth] User data cleared.");
    }

    /// <summary>Chờ cho đến khi user sẵn sàng (hoặc hết timeout).</summary>
    public IEnumerator WaitUntilUserReady(float timeoutSeconds = 5f)
    {
        float t = 0f;
        while (!IsUserDataReady && t < timeoutSeconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
