using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using CGP.Gameplay.Auth;

public class LoadingManager : MonoBehaviour
{
    public Slider progressBar;
    public string sceneToLoad;

    void Start()
    {
        StartCoroutine(LoadEverything());
    }

    IEnumerator LoadEverything()
    {
        // 1. Chờ AuthManager có dữ liệu user
        if (AuthManager.Instance != null && !AuthManager.Instance.IsUserDataReady)
        {
            yield return StartCoroutine(AuthManager.Instance.GetCurrentUser());
        }

        // 2. Bắt đầu load scene
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneToLoad);
        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            float progress = Mathf.Clamp01(op.progress / 0.9f);
            progressBar.value = progress;

            if (op.progress >= 0.9f)
            {
                yield return new WaitForSeconds(0.5f);
                op.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}
