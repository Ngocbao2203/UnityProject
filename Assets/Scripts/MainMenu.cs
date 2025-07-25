using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void PlayGame()
    {
        SceneManager.LoadScene("LoadingScene"); // đổi tên này thành scene chơi game của bạn
    }
}
