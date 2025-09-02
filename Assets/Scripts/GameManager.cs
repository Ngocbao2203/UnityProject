using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    [Header("Scene refs")]
    [SerializeField] public ItemManager itemManager;
    [SerializeField] public TileManager tileManager;
    [SerializeField] public UI_Manager uiManager;
    [SerializeField] public Player player;

    [HideInInspector] public string userId;
    private const string PLAYERPREFS_USERID_KEY = "userId";

    // giữ delegate để có thể hủy đăng ký event khi OnDestroy
    private AuthManager.UserInfoResult _authCb;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);

        if (itemManager == null) itemManager = GetComponent<ItemManager>();
        if (tileManager == null) tileManager = GetComponent<TileManager>();
        if (uiManager == null) uiManager = GetComponent<UI_Manager>();
        if (player == null)
        {
            player = FindFirstObjectByType<Player>();
            if (player == null) Debug.LogError("[GameManager] Player not found in scene!");
        }

        // ⚠️ Fallback tạm thời: chỉ dùng nếu Auth chưa sẵn
        if (string.IsNullOrEmpty(userId) && PlayerPrefs.HasKey(PLAYERPREFS_USERID_KEY))
        {
            var cached = PlayerPrefs.GetString(PLAYERPREFS_USERID_KEY);
            if (!string.IsNullOrEmpty(cached))
            {
                userId = cached;
                Debug.Log("[GameManager] Fallback userId from PlayerPrefs: " + userId);
            }
        }
    }

    private void Start()
    {
        // Nếu Auth đã sẵn → ưu tiên set ngay từ Auth
        if (AuthManager.Instance && AuthManager.Instance.IsUserDataReady)
        {
            var uid = AuthManager.Instance.GetCurrentUserId();
            if (!string.IsNullOrEmpty(uid))
            {
                SetUserId(uid); // SetUserId sẽ lưu lại PlayerPrefs
            }
        }

        // Đăng ký lắng nghe kết quả Auth (login / refresh)
        if (AuthManager.Instance != null)
        {
            _authCb = (ok, msg, data) =>
            {
                if (ok && data != null && !string.IsNullOrEmpty(data.id))
                {
                    // Luôn ưu tiên id từ Auth (nguồn sự thật)
                    SetUserId(data.id);
                    Debug.Log("[GameManager] userId set from AuthManager: " + userId);
                }
            };
            AuthManager.Instance.OnUserInfoReceived += _authCb;
        }
    }

    private void OnDestroy()
    {
        if (AuthManager.Instance != null && _authCb != null)
            AuthManager.Instance.OnUserInfoReceived -= _authCb;
    }

    /// <summary>
    /// Set userId và (mặc định) lưu vào PlayerPrefs.
    /// CHỈ gọi với id từ AuthManager hoặc khi bạn chắc chắn đúng user.
    /// </summary>
    public void SetUserId(string uid, bool persist = true)
    {
        if (string.IsNullOrEmpty(uid)) return;

        // tránh log/ghi đè không cần thiết nếu không đổi
        if (userId == uid) return;

        userId = uid;

        if (persist)
        {
            PlayerPrefs.SetString(PLAYERPREFS_USERID_KEY, uid);
            PlayerPrefs.Save();
        }

        Debug.Log("[GameManager] userId set: " + userId);
    }

    /// <summary>
    /// Lấy userId ưu tiên theo thứ tự: AuthManager -> GameManager (current) -> null
    /// </summary>
    public string GetUserId()
    {
        if (AuthManager.Instance && AuthManager.Instance.IsUserDataReady)
        {
            var uid = AuthManager.Instance.GetCurrentUserId();
            if (!string.IsNullOrEmpty(uid)) return uid;
        }
        return string.IsNullOrEmpty(userId) ? null : userId;
    }
}
