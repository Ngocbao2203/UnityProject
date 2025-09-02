using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Collections;

public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance;

    [Header("UI")]
    public TextMeshProUGUI coinText;

    [Header("State")]
    public int coins = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void OnEnable()
    {
        // Khi Auth sẵn sàng mới load xu
        if (AuthManager.Instance != null)
            AuthManager.Instance.OnUserInfoReceived += OnUserInfoReceived;
    }

    private void OnDisable()
    {
        if (AuthManager.Instance != null)
            AuthManager.Instance.OnUserInfoReceived -= OnUserInfoReceived;
    }

    private void Start()
    {
        UpdateCoinUI();

        // Nếu Auth đã sẵn sàng thì load luôn, chưa thì chờ sự kiện/coroutine
        if (AuthManager.Instance != null && AuthManager.Instance.IsUserDataReady)
        {
            _ = LoadCoinsFromServer(AuthManager.Instance.GetCurrentUserId());
        }
        else
        {
            StartCoroutine(WaitAuthThenLoadCoins());
        }
    }

    private IEnumerator WaitAuthThenLoadCoins()
    {
        float timeout = 10f, t = 0f;
        while ((AuthManager.Instance == null || !AuthManager.Instance.IsUserDataReady) && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (AuthManager.Instance != null && AuthManager.Instance.IsUserDataReady)
            _ = LoadCoinsFromServer(AuthManager.Instance.GetCurrentUserId());
        else
            Debug.LogWarning("[Currency] Auth not ready after timeout, skip loading coins.");
    }

    private void OnUserInfoReceived(bool ok, string msg, UserData user)
    {
        if (ok && user != null)
            _ = LoadCoinsFromServer(user.id);
    }

    // ==================== API ====================
    private async Task LoadCoinsFromServer(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogError("[Currency] userId null → không thể load coins");
            return;
        }

        string url = ApiRoutes.Point.GET_BY_USERID.Replace("{userId}", userId);
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 15;
            req.SetRequestHeader("Content-Type", "application/json");
            string token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string json = req.downloadHandler.text;
                Debug.Log($"[Currency] GetPoints response: {json}");

                // JSON mẫu bạn gửi:
                // { "error":0,"message":"...","count":0,"data":{"userId":"...","amount":100, ... } }
                try
                {
                    var env = JsonUtility.FromJson<PointEnvelope>(json);
                    if (env != null && env.data != null)
                    {
                        coins = env.data.amount;
                        Debug.Log($"[Currency] Parsed amount = {coins}");
                        UpdateCoinUI();
                    }
                    else
                    {
                        Debug.LogWarning("[Currency] Parse JSON ok nhưng data null.");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Currency] Parse JSON FAIL: {ex.Message}");
                }
            }
            else
            {
                Debug.LogError($"[Currency] GetPoints FAIL: {req.error} (code {req.responseCode})");
            }
        }
    }

    // ==================== Public API ====================
    public void AddCoins(int amount)
    {
        coins += amount;
        UpdateCoinUI();
        // TODO: gọi API update nếu có
    }

    public bool SpendCoins(int amount)
    {
        if (coins >= amount)
        {
            coins -= amount;
            UpdateCoinUI();
            // TODO: gọi API update nếu có
            return true;
        }
        return false;
    }

    public bool TrySpendCoins(int amount)
    {
        if (coins >= amount)
        {
            coins -= amount;
            UpdateCoinUI();
            // TODO: gọi API update nếu có
            return true;
        }
        return false;
    }

    public bool HasEnoughCoins(int amount) => coins >= amount;

    private void UpdateCoinUI()
    {
        if (coinText != null)
            coinText.text = coins.ToString();
        else
            Debug.LogWarning("[Currency] coinText chưa được gán trong Inspector.");
    }

    // ==================== DTOs phù hợp JSON ====================
    [System.Serializable]
    private class PointEnvelope
    {
        public int error;
        public string message;
        public int count;
        public PointData data;
    }

    [System.Serializable]
    private class PointData
    {
        public string userId;
        public int amount;       // 👈 cần đúng tên "amount"
        public string userName;
        public string createdAt;
        public string updatedAt;
        // pointTransactions bỏ qua vì không cần để đọc coins
    }
}
