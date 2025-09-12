using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CGP.Gameplay.Farming;

namespace CGP.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public class CropInfo_UI : MonoBehaviour
    {
        public static CropInfo_UI Instance;

        [SerializeField] private RectTransform panelRoot; // có thể để trống
        [SerializeField] private TextMeshProUGUI nameText, stageText, timeText, statusText;
        [SerializeField] private Image cropIcon;

        private Crop currentCrop;
        private bool isShowing;
        private float lastToggleTime = -999f;
        private const float ToggleCooldown = 0.08f;

        CanvasGroup _cg;

        void Awake()
        {
            if (Instance != this && Instance != null) { Destroy(gameObject); return; }
            Instance = this;

            if (!panelRoot) panelRoot = transform as RectTransform;

            _cg = GetComponent<CanvasGroup>();
            if (!_cg) _cg = gameObject.AddComponent<CanvasGroup>();

            HideVisual(); // ẩn bằng CanvasGroup, KHÔNG SetActive(false)

            var canvas = GetComponentInParent<Canvas>();
            if (canvas) { canvas.overrideSorting = true; canvas.sortingOrder = 500; }
        }

        void OnEnable() => Crop.OnCropClicked += ShowCropInfo;
        void OnDisable() => Crop.OnCropClicked -= ShowCropInfo;

        public void ShowCropInfo(Crop crop)
        {
            if (Time.unscaledTime - lastToggleTime < ToggleCooldown) return;
            if (crop == null || crop.cropData == null) { HidePanel(); return; }

            currentCrop = crop;
            isShowing = true;
            lastToggleTime = Time.unscaledTime;

            UpdateUI(currentCrop);
            ShowVisual(); // thay cho SetActive(true)
        }

        public void HidePanel()
        {
            if (Time.unscaledTime - lastToggleTime < ToggleCooldown) return;
            isShowing = false;
            currentCrop = null;
            lastToggleTime = Time.unscaledTime;
            HideVisual(); // thay cho SetActive(false)
        }

        void ShowVisual() { _cg.alpha = 1f; _cg.interactable = true; _cg.blocksRaycasts = true; }
        void HideVisual() { _cg.alpha = 0f; _cg.interactable = false; _cg.blocksRaycasts = false; }

        void Update()
        {
            if (isShowing && currentCrop) UpdateUI(currentCrop);
            if (!isShowing) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { HidePanel(); return; }
            if (Input.GetMouseButtonDown(0) && !IsPointerOverSelf()) HidePanel();
        }

        void UpdateUI(Crop crop)
        {
            if (!crop || !crop.cropData) { HidePanel(); return; }
            if (nameText) nameText.text = crop.cropData.cropName;
            if (stageText) stageText.text = $"Giai đoạn: {crop.CurrentStage + 1}/{crop.cropData.growthStages.Length}";
            if (crop.IsMature()) { if (statusText) statusText.text = "✅ Có thể thu hoạch"; if (timeText) timeText.text = ""; }
            else if (crop.IsWaitingForNextStage()) { if (statusText) statusText.text = "⏳ Đang phát triển..."; if (timeText) timeText.text = $"Còn {crop.TimeLeftToNextStage:F1}s"; }
            else if (crop.HasBeenWatered) { if (statusText) statusText.text = "💧 Đã được tưới (chưa đủ)"; if (timeText) timeText.text = ""; }
            else { if (statusText) statusText.text = "💧 Chưa được tưới"; if (timeText) timeText.text = ""; }

            if (cropIcon)
            {
                var stages = crop.cropData.growthStages;
                var idx = Mathf.Clamp(crop.CurrentStage, 0, stages.Length - 1);
                cropIcon.sprite = (stages != null && stages.Length > 0) ? stages[idx] : null;
                cropIcon.enabled = cropIcon.sprite != null;
            }
        }

        bool IsPointerOverSelf()
        {
            var cam = Camera.main;
            return panelRoot && RectTransformUtility.RectangleContainsScreenPoint(panelRoot, Input.mousePosition, cam);
        }

        public void OnCloseButtonClicked() => HidePanel();
    }
}
