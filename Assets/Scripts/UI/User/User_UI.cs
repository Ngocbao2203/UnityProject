using UnityEngine;
using TMPro;
using CGP.Framework;
using CGP.Gameplay.Auth;

namespace CGP.UI
{
    public class User_UI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText; // kéo trong Inspector

        private void Awake()
        {
            if (!nameText)
                Debug.LogError("[User_UI] Chưa gán nameText trong Inspector!");
        }

        private void Start()
        {
            // ===== Check token =====
            string token = LocalStorageHelper.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[Unity] Không tìm thấy token! Yêu cầu đăng nhập.");
                nameText.text = "Guest";
                return;
            }

            Debug.Log($"[Unity] Token lấy từ LocalStorage: {token}");

            // ===== Check AuthManager =====
            if (AuthManager.Instance == null)
            {
                Debug.LogError("[User_UI] AuthManager.Instance = null. Đảm bảo AuthManager có trong scene trước khi User_UI chạy!");
                nameText.text = "Unknown";
                return;
            }

            // Đăng ký event
            AuthManager.Instance.OnUserInfoReceived -= OnUserInfoReceived; // tránh double-sub
            AuthManager.Instance.OnUserInfoReceived += OnUserInfoReceived;

            // Nếu chưa sẵn sàng thì gọi API
            if (!AuthManager.Instance.IsUserDataReady)
            {
                AuthManager.Instance.GetCurrentUser();
            }
            else
            {
                // Cập nhật ngay nếu đã có dữ liệu
                var data = AuthManager.Instance.GetCurrentUserData();
                if (data != null && !string.IsNullOrEmpty(data.userName))
                    nameText.text = data.userName;
                else
                    nameText.text = "Unknown";
            }
        }

        private void OnDestroy()
        {
            if (AuthManager.Instance != null)
                AuthManager.Instance.OnUserInfoReceived -= OnUserInfoReceived;
        }

        private void OnUserInfoReceived(bool success, string message, UserData data)
        {
            if (!nameText) return; // tránh lỗi null

            if (success && data != null && !string.IsNullOrEmpty(data.userName))
            {
                nameText.text = data.userName;
            }
            else
            {
                nameText.text = "Unknown";
            }
        }
    }
}
