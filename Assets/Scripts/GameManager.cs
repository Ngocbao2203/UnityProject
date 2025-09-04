using UnityEngine;
using UnityEngine.SceneManagement;

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

    // Tránh load farm lặp cho cùng user
    private string _lastLoadedUserId = null;

    // Hàng đợi gọi LoadFarm khi TileManager chưa sẵn sàng
    private string _pendingLoadUserId = null;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);

        // Bind refs nếu để trống
        if (itemManager == null) itemManager = GetComponent<ItemManager>();
        if (tileManager == null) tileManager = GetComponent<TileManager>();
        if (uiManager == null) uiManager = GetComponent<UI_Manager>();
        if (player == null) player = FindFirstObjectByType<Player>();

        if (player == null) Debug.LogWarning("[GameManager] Player not found in scene!");

        // Lấy userId dự phòng từ PlayerPrefs nếu Auth chưa có
        if (string.IsNullOrEmpty(userId) && PlayerPrefs.HasKey(PLAYERPREFS_USERID_KEY))
        {
            var cached = PlayerPrefs.GetString(PLAYERPREFS_USERID_KEY);
            if (!string.IsNullOrEmpty(cached))
            {
                userId = cached;
                Debug.Log("[GameManager] Fallback userId from PlayerPrefs: " + userId);
                EnsureLoadFarm(userId); // sẽ tự xếp hàng nếu TileManager chưa sẵn sàng
            }
        }

        // Theo dõi sự kiện load scene để rebind refs và xử lý pending
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        // Nếu Auth đã sẵn sàng -> dùng id từ Auth làm nguồn sự thật
        if (AuthManager.Instance && AuthManager.Instance.IsUserDataReady)
        {
            var uid = AuthManager.Instance.GetCurrentUserId();
            if (!string.IsNullOrEmpty(uid))
                SetUserId(uid); // SetUserId sẽ tự EnsureLoadFarm
        }

        // Đăng ký lắng nghe kết quả Auth (login / refresh token …)
        if (AuthManager.Instance != null)
        {
            _authCb = (ok, msg, data) =>
            {
                if (ok && data != null && !string.IsNullOrEmpty(data.id))
                {
                    SetUserId(data.id); // luôn đi qua đây để đảm bảo thống nhất
                    Debug.Log("[GameManager] userId set from AuthManager: " + userId);
                }
            };
            AuthManager.Instance.OnUserInfoReceived += _authCb;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (AuthManager.Instance != null && _authCb != null)
            AuthManager.Instance.OnUserInfoReceived -= _authCb;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Rebind refs khi đổi scene
        if (tileManager == null) tileManager = FindFirstObjectByType<TileManager>();
        if (uiManager == null) uiManager = FindFirstObjectByType<UI_Manager>();
        if (itemManager == null) itemManager = FindFirstObjectByType<ItemManager>();
        if (player == null) player = FindFirstObjectByType<Player>();

        // Nếu có pending load từ trước và giờ TileManager đã sẵn
        if (!string.IsNullOrEmpty(_pendingLoadUserId) && tileManager != null)
        {
            Debug.Log("[GameManager] SceneLoaded → run pending LoadFarm for userId=" + _pendingLoadUserId);
            tileManager.LoadFarm(_pendingLoadUserId);
            _lastLoadedUserId = _pendingLoadUserId;
            _pendingLoadUserId = null;
        }
    }

    /// <summary>Đặt userId (ưu tiên id từ Auth) và lưu PlayerPrefs. Tự gọi EnsureLoadFarm.</summary>
    public void SetUserId(string uid, bool persist = true)
    {
        if (string.IsNullOrEmpty(uid)) return;

        // Không cần check changed; EnsureLoadFarm đã có guard
        userId = uid;

        if (persist)
        {
            PlayerPrefs.SetString(PLAYERPREFS_USERID_KEY, uid);
            PlayerPrefs.Save();
        }

        Debug.Log("[GameManager] userId set: " + userId);
        EnsureLoadFarm(userId);
    }

    /// <summary>Lấy userId: ưu tiên AuthManager, sau đó đến biến cục bộ.</summary>
    public string GetUserId()
    {
        if (AuthManager.Instance && AuthManager.Instance.IsUserDataReady)
        {
            var uid = AuthManager.Instance.GetCurrentUserId();
            if (!string.IsNullOrEmpty(uid)) return uid;
        }
        return string.IsNullOrEmpty(userId) ? null : userId;
    }

    /// <summary>
    /// Đảm bảo gọi LoadFarm đúng 1 lần cho mỗi userId. Nếu TileManager chưa sẵn sàng
    /// (ví dụ vừa reload WebGL), sẽ xếp hàng và gọi ngay khi scene load xong.
    /// </summary>
    private void EnsureLoadFarm(string uid)
    {
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[GameManager] EnsureLoadFarm: uid null/empty");
            return;
        }

        // Tránh gọi lặp cho cùng user
        if (_lastLoadedUserId == uid && string.IsNullOrEmpty(_pendingLoadUserId))
        {
            Debug.Log("[GameManager] EnsureLoadFarm: already loaded for userId=" + uid);
            return;
        }

        var tm = (tileManager != null) ? tileManager : TileManager.Instance;
        if (tm == null)
        {
            // Chưa có TileManager -> chờ sceneLoaded
            _pendingLoadUserId = uid;
            Debug.Log("[GameManager] EnsureLoadFarm: TileManager not ready, queue load for userId=" + uid);
            return;
        }

        Debug.Log("[GameManager] EnsureLoadFarm: calling TileManager.LoadFarm for userId=" + uid);
        tm.LoadFarm(uid);
        _lastLoadedUserId = uid;
        _pendingLoadUserId = null; // xoá pending nếu có
    }
}
