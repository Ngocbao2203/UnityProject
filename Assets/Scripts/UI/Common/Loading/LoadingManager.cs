using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using CGP.Gameplay.Auth;

public class LoadingManager : MonoBehaviour
{
    [Header("Bindings")]
    [SerializeField] private Slider progressBar;          // kéo vào (có thể để null)
    [SerializeField] private TMP_Text tipText;            // optional
    [SerializeField] private CanvasGroup fadeGroup;       // optional (panel phủ loading)

    [Header("Config")]
    [SerializeField] private string sceneToLoad = "Main";
    [SerializeField] private float minDisplayTime = 2.0f; // tối thiểu ở màn loading
    [SerializeField] private float authTimeout = 5.0f;    // chờ Auth tối đa
    [SerializeField] private float tipInterval = 3.0f;    // đổi tip mỗi n giây
    [SerializeField] private string[] tips;               // gán vài tip trong Inspector

    [Header("Fade")]
    [SerializeField] private float fadeOutTime = 0.25f;   // fade panel khi chuyển cảnh

    float _tipTimer;

    void Start()
    {
        if (fadeGroup) fadeGroup.alpha = 1f; // loading panel hiện sẵn
        StartCoroutine(LoadFlow());
    }

    void Update()
    {
        // đổi tip định kỳ
        if (tipText && tips != null && tips.Length > 0)
        {
            _tipTimer -= Time.unscaledDeltaTime;
            if (_tipTimer <= 0f)
            {
                tipText.text = tips[Random.Range(0, tips.Length)];
                _tipTimer = tipInterval;
            }
        }
    }

    IEnumerator LoadFlow()
    {
        float startTime = Time.unscaledTime;

        // 1) Chờ AuthManager nếu có
        yield return StartCoroutine(WaitForAuth());

        // 2) Bắt đầu load scene đích
        if (string.IsNullOrEmpty(sceneToLoad))
        {
            Debug.LogError("[Loading] sceneToLoad rỗng!");
            yield break;
        }

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneToLoad);
        op.allowSceneActivation = false;

        // 3) Cập nhật progress mượt
        while (!op.isDone)
        {
            // op.progress: [0..0.9] trong khi load; 0.9 -> ready
            float raw = Mathf.Clamp01(op.progress / 0.9f);
            UpdateProgress(raw);

            // Đợi đủ điều kiện: progress sẵn sàng + qua tối thiểu minDisplayTime
            bool ready = op.progress >= 0.9f && (Time.unscaledTime - startTime) >= minDisplayTime;
            if (ready)
            {
                // Fade out nhẹ nếu có
                if (fadeGroup) yield return StartCoroutine(FadeCanvas(fadeGroup, 0f, fadeOutTime));
                op.allowSceneActivation = true;
            }

            yield return null;
        }
    }

    IEnumerator WaitForAuth()
    {
        // Không có AuthManager → bỏ qua
        if (AuthManager.Instance == null) yield break;

        // Nếu chưa sẵn sàng thì gọi API (nếu hàm GetCurrentUser là coroutine)
        if (!AuthManager.Instance.IsUserDataReady)
        {
            bool finished = false;
            float t = 0f;

            // Nếu AuthManager có coroutine trả về IEnumerator:
            IEnumerator co = null;
            try { co = AuthManager.Instance.GetCurrentUser(); }
            catch { /* nếu không phải coroutine */ }

            if (co != null)
            {
                // Chạy song song với timeout
                StartCoroutine(RunAndFlag(co, () => finished = true));
                while (!finished && t < authTimeout)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            else
            {
                // Không phải coroutine → đợi tới khi cờ sẵn sàng hoặc hết timeout
                AuthManager.Instance.GetCurrentUser();
                while (!AuthManager.Instance.IsUserDataReady && t < authTimeout)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }
    }

    IEnumerator RunAndFlag(IEnumerator routine, System.Action onDone)
    {
        yield return routine;
        onDone?.Invoke();
    }

    void UpdateProgress(float p)
    {
        if (progressBar)
        {
            // Lerp mượt thanh progress
            progressBar.value = Mathf.MoveTowards(progressBar.value, p, Time.unscaledDeltaTime * 0.8f);
        }
    }

    IEnumerator FadeCanvas(CanvasGroup cg, float target, float duration)
    {
        float start = cg.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        cg.alpha = target;
    }
}
