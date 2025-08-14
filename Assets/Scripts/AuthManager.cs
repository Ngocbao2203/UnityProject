using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class GetCurrentUserResponse
{
    public int error;
    public string message;
    public UserData data;
}

[System.Serializable]
public class UserData
{
    public string id; // GUID dạng string
    public string userName;
    public string email;
    public string phoneNumber;
    public string status;
    public int roleId;
}

[DefaultExecutionOrder(-100)]
public class AuthManager : MonoBehaviour
{
    private const string GET_CURRENT_USER_URL = ApiRoutes.GET_CURRENT_USER;
    public delegate void UserInfoResult(bool success, string message, UserData userData);
    public event UserInfoResult OnUserInfoReceived;

    public static AuthManager Instance { get; private set; }
    private UserData currentUserData;
    public bool IsUserDataReady { get; private set; }
    private bool isLoading = false;

    private void Start()
    {
        StartCoroutine(GetCurrentUser()); // Bắt đầu tải dữ liệu user khi game khởi động
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Giữ lại qua các scene
        }
        else
        {
            Destroy(gameObject); // Không tạo bản mới
        }
    }


    /// <summary>
    /// Gửi request để lấy thông tin người dùng hiện tại từ server.
    /// </summary>
    /// <param name="maxRetries">Số lần thử lại nếu request thất bại.</param>
    /// <returns>IEnumerator để sử dụng trong coroutine.</returns>
    public IEnumerator GetCurrentUser(int maxRetries = 3)
    {
        if (isLoading)
        {
            Debug.LogWarning("GetCurrentUser already in progress");
            yield break;
        }

        isLoading = true;
        int retries = 0;

        while (retries < maxRetries)
        {
            //Debug.Log($"GetCurrentUser started (Attempt {retries + 1}/{maxRetries})...");
            string token = LocalStorageHelper.GetToken();

            if (string.IsNullOrEmpty(token))
            {
                //Debug.LogError("Token not found in localStorage!");
                OnUserInfoReceived?.Invoke(false, "Token not found", null);
                isLoading = false;
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(GET_CURRENT_USER_URL))
            {
                request.timeout = 10; // Timeout sau 10 giây
                request.SetRequestHeader("Authorization", "Bearer " + token);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (request.responseCode == 401)
                    {
                        //Debug.LogError("Unauthorized: Token expired or invalid");
                        OnUserInfoReceived?.Invoke(false, "Token expired, please log in again", null);
                        isLoading = false;
                        yield break;
                    }

                    //Debug.LogError($"Request error: {request.error} (HTTP {request.responseCode})");
                    retries++;
                    if (retries < maxRetries)
                    {
                        yield return new WaitForSeconds(1f);
                        continue;
                    }

                    OnUserInfoReceived?.Invoke(false, $"Request failed after {maxRetries} retries: {request.error}", null);
                    isLoading = false;
                    yield break;
                }

                try
                {
                    string jsonResponse = request.downloadHandler.text;
                    Debug.Log($"Server response: {jsonResponse}");
                    GetCurrentUserResponse response = JsonUtility.FromJson<GetCurrentUserResponse>(jsonResponse);

                    if (response.error == 0 && response.data != null)
                    {
                        currentUserData = response.data;
                        IsUserDataReady = true;
                        //Debug.Log($"Successfully fetched user info! UserId: {currentUserData.id}");
                        OnUserInfoReceived?.Invoke(true, response.message, response.data);
                    }
                    else
                    {
                        //Debug.LogError($"Failed to fetch user info: {response?.message ?? "No message"}");
                        OnUserInfoReceived?.Invoke(false, response?.message ?? "Invalid response", null);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"JSON parse error: {e.Message}");
                    OnUserInfoReceived?.Invoke(false, "Invalid server response", null);
                }

                break; // Thoát nếu thành công hoặc hết lần retry
            }
        }

        isLoading = false;
    }

    /// <summary>
    /// Lấy ID của người dùng hiện tại.
    /// </summary>
    /// <returns>ID của người dùng hoặc null nếu chưa sẵn sàng.</returns>
    public string GetCurrentUserId()
    {
        if (!IsUserDataReady || currentUserData == null)
        {
            Debug.LogWarning("User data not ready or unavailable");
            return null;
        }
        Debug.Log($"GetCurrentUserId called, userId: {currentUserData.id}");
        return currentUserData.id;
    }

    /// <summary>
    /// Lấy dữ liệu người dùng hiện tại.
    /// </summary>
    /// <returns>Dữ liệu người dùng hoặc null nếu chưa sẵn sàng.</returns>
    public UserData GetCurrentUserData()
    {
        if (!IsUserDataReady || currentUserData == null)
        {
            Debug.LogWarning("User data not ready or unavailable");
            return null;
        }
        return currentUserData;
    }

    /// <summary>
    /// Thử lại việc lấy thông tin người dùng nếu chưa thành công.
    /// </summary>
    public void RetryGetCurrentUser()
    {
        if (!isLoading && !IsUserDataReady)
        {
            Debug.Log("Retrying to get current user...");
            StartCoroutine(GetCurrentUser());
        }
    }

    /// <summary>
    /// Xóa dữ liệu người dùng khi đăng xuất.
    /// </summary>
    public void ClearUserData()
    {
        currentUserData = null;
        IsUserDataReady = false;
        Debug.Log("User data cleared");
    }
}