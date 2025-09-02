using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("UI References")]
    public Image imageArea;         // Nơi hiển thị ảnh tutorial
    public TextMeshProUGUI textArea; // Nơi hiển thị text
    public TextMeshProUGUI pageText; // Hiển thị "1/3"
    public Button prevButton;
    public Button nextButton;
    public Button closeButton;

    [Header("Tutorial Data")]
    public Sprite[] tutorialImages; // Ảnh các bước
    [TextArea(3, 5)]
    public string[] tutorialTexts;  // Text mô tả mỗi bước

    private int currentIndex = 0;

    private void Start()
    {
        // Gắn sự kiện cho nút
        prevButton.onClick.AddListener(PrevPage);
        nextButton.onClick.AddListener(NextPage);
        closeButton.onClick.AddListener(CloseTutorial);
        ShowPage(0); // Bắt đầu từ trang 0
    }

    private void ShowPage(int index)
    {
        // Giới hạn index
        currentIndex = Mathf.Clamp(index, 0, tutorialImages.Length - 1);

        // Hiển thị ảnh + text
        if (tutorialImages.Length > 0 && currentIndex < tutorialImages.Length)
            imageArea.sprite = tutorialImages[currentIndex];

        if (tutorialTexts.Length > 0 && currentIndex < tutorialTexts.Length)
            textArea.text = tutorialTexts[currentIndex];

        // Hiển thị số trang (vd: 1/3)
        pageText.text = $"{currentIndex + 1}/{tutorialImages.Length}";

        // Bật/tắt nút nếu ở đầu/cuối
        prevButton.interactable = currentIndex > 0;
        nextButton.interactable = currentIndex < tutorialImages.Length - 1;
    }

    public void NextPage()
    {
        ShowPage(currentIndex + 1);
    }

    public void PrevPage()
    {
        ShowPage(currentIndex - 1);
    }
    private void CloseTutorial()
    {
        gameObject.SetActive(false); // 🔹 Tắt TutorialPanel
    }
    public void ResetTutorial()
    {
        ShowPage(0);
        gameObject.SetActive(true); // Mở lại từ đầu
    }
}
