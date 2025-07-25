using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LoadingManager : MonoBehaviour
{
    public Slider progressBar;
    public string sceneToLoad;

    void Start()
    {
        StartCoroutine(LoadSceneAsync());
    }

    IEnumerator LoadSceneAsync()
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneToLoad);
        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            float progress = Mathf.Clamp01(op.progress / 0.9f);
            progressBar.value = progress;

            // Khi load xong 100%, tự động chuyển
            if (op.progress >= 0.9f)
            {
                yield return new WaitForSeconds(0.5f); // chờ tí
                op.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}
