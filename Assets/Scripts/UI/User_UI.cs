using UnityEngine;
using TMPro;

public class User_UI : MonoBehaviour
{
    public TMP_Text nameText; // gán trong Inspector

    void Start()
    {
        // Lấy token
        string token = LocalStorageHelper.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            Debug.LogError("[Unity] Không tìm thấy token! Yêu cầu đăng nhập.");
            // Có thể load scene login
            return;
        }

        Debug.Log($"[Unity] Token lấy từ LocalStorage: {token}");

        // Nghe event khi AuthManager lấy xong dữ liệu
        AuthManager.Instance.OnUserInfoReceived += OnUserInfoReceived;

        // Nếu chưa gọi API thì gọi
        if (!AuthManager.Instance.IsUserDataReady)
        {
            AuthManager.Instance.GetCurrentUser();
        }
        else
        {
            // Nếu đã sẵn sàng, cập nhật luôn
            var data = AuthManager.Instance.GetCurrentUserData();
            nameText.text = data.userName;
        }
    }

    private void OnUserInfoReceived(bool success, string message, UserData data)
    {
        if (success && data != null)
        {
            nameText.text = data.userName;
        }
        else
        {
            nameText.text = "Unknown";
        }
    }
}
