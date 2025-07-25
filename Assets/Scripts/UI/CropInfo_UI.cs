using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CropInfo_UI : MonoBehaviour
{
    public static CropInfo_UI Instance;

    [Header("UI Elements")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI stageText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI statusText;
    public Image cropIcon;
    private Crop currentCrop;
    private bool isShowing = false;

    private void Awake()
    {
        Instance = this;
        HidePanel();
    }

    public void ShowCropInfo(Crop crop)
    {
        if (crop == null || crop.cropData == null)
            return;

        currentCrop = crop;
        isShowing = true;

        UpdateUI(crop); // Gọi hàm cập nhật UI một lần
        gameObject.SetActive(true);
    }

    public void HidePanel()
    {
        gameObject.SetActive(false);
    }
    private void UpdateUI(Crop crop)
    {
        nameText.text = crop.cropData.cropName;
        stageText.text = $"Giai đoạn: {crop.CurrentStage + 1}/{crop.cropData.growthStages.Length}";

        if (crop.IsMature())
        {
            statusText.text = "✅ Có thể thu hoạch";
            timeText.text = "";
        }
        else if (crop.IsWaitingForNextStage())
        {
            statusText.text = "⏳ Đang phát triển...";
            timeText.text = $"Còn {crop.TimeLeftToNextStage:F1}s để phát triển";
        }
        else if (crop.HasBeenWatered)
        {
            statusText.text = "💧 Đã được tưới (chưa đủ)";
            timeText.text = "";
        }
        else
        {
            statusText.text = "💧 Chưa được tưới";
            timeText.text = "";
        }

        cropIcon.sprite = crop.cropData.growthStages[crop.CurrentStage];
    }
    private void Update()
    {
        if (isShowing && currentCrop != null)
        {
            UpdateUI(currentCrop);
        }

        // ✳️ ESC để đóng
        if (isShowing && Input.GetKeyDown(KeyCode.Escape))
        {
            HidePanel();
        }

        if (Input.GetMouseButtonDown(1)) // 1 là chuột phải
        {
            HidePanel();
        }
    }
}
