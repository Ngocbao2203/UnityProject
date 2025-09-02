using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

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

[DefaultExecutionOrder(-100)]
public class AuthManager : MonoBehaviour
{
    private const string GET_CURRENT_USER_URL = ApiRoutes.Auth.GET_CURRENT_USER;

    public delegate void UserInfoResult(bool success, string message, UserData userData);
    public event UserInfoResult OnUserInfoReceived;

    public static AuthManager Instance { get; private set; }

    private UserData currentUserData;
    public bool IsUserDataReady { get; private set; }
    private bool isLoading = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            StartCoroutine(GetCurrentUser()); // bắt đầu tải ngay khi singleton sẵn sàng
        }
        else
        {
            Destroy(gameObject);
        }
    }

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
            string token = LocalStorageHelper.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                OnUserInfoReceived?.Invoke(false, "Token not found", null);
                isLoading = false;
                yield break;
            }

            using (var request = UnityWebRequest.Get(GET_CURRENT_USER_URL))
            {
                request.timeout = 10;
                request.SetRequestHeader("Authorization", "Bearer " + token);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (request.responseCode == 401)
                    {
                        OnUserInfoReceived?.Invoke(false, "Token expired, please log in again", null);
                        isLoading = false;
                        yield break;
                    }

                    retries++;
                    if (retries < maxRetries)
                    {
                        yield return new WaitForSeconds(Mathf.Pow(2, retries) * 0.5f);
                        continue;
                    }

                    OnUserInfoReceived?.Invoke(false, $"Request failed after {maxRetries} retries: {request.error}", null);
                    isLoading = false;
                    yield break;
                }

                try
                {
                    var jsonResponse = request.downloadHandler.text;
                    // Debug.Log($"Server response: {jsonResponse}");
                    var response = JsonUtility.FromJson<GetCurrentUserResponse>(jsonResponse);

                    if (response != null && response.error == 0 && response.data != null)
                    {
                        currentUserData = response.data;
                        IsUserDataReady = true;
                        OnUserInfoReceived?.Invoke(true, response.message, response.data);
                    }
                    else
                    {
                        OnUserInfoReceived?.Invoke(false, response?.message ?? "Invalid response", null);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"JSON parse error: {e.Message}");
                    OnUserInfoReceived?.Invoke(false, "Invalid server response", null);
                }

                break; // thành công -> thoát vòng while
            }
        }

        isLoading = false;
    }

    public string GetCurrentUserId()
    {
        if (!IsUserDataReady || currentUserData == null)
        {
            Debug.LogWarning("User data not ready or unavailable");
            return null;
        }
        return currentUserData.id;
    }

    public UserData GetCurrentUserData()
    {
        if (!IsUserDataReady || currentUserData == null)
        {
            Debug.LogWarning("User data not ready or unavailable");
            return null;
        }
        return currentUserData;
    }

    public void RetryGetCurrentUser()
    {
        if (!isLoading && !IsUserDataReady)
        {
            Debug.Log("Retrying to get current user...");
            StartCoroutine(GetCurrentUser());
        }
    }

    public void ClearUserData()
    {
        currentUserData = null;
        IsUserDataReady = false;
        Debug.Log("User data cleared");
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
}
