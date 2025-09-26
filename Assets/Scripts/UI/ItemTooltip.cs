using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CGP.UI
{
    public class ItemTooltip : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private GameObject window;
        [SerializeField] private Image icon;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private Canvas rootCanvas; // nếu để trống sẽ tự tìm

        // cached
        private RectTransform rt;
        private RectTransform canvasRT;

        // layout / position
        const float MARGIN = 10f; // chừa mép
        const float OFFSET_X = 24f; // đẩy sang phải con trỏ
        const float OFFSET_Y = 12f; // đẩy xuống nhẹ

        void Awake()
        {
            // --- Validate refs ---
            if (window == null)
            {
                Debug.LogError("[ItemTooltip] 'window' chưa gán!", this);
                enabled = false;
                return;
            }

            rt = window.GetComponent<RectTransform>();
            if (rt == null)
            {
                Debug.LogError("[ItemTooltip] 'window' cần RectTransform.", window);
                enabled = false;
                return;
            }

            // Tìm canvas gốc nếu chưa gán
            if (!rootCanvas) rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas == null)
            {
                Debug.LogError("[ItemTooltip] Không tìm thấy Canvas gốc.", this);
                enabled = false;
                return;
            }

            canvasRT = rootCanvas.transform as RectTransform;
            if (canvasRT == null)
            {
                Debug.LogError("[ItemTooltip] Canvas không có RectTransform.", rootCanvas);
                enabled = false;
                return;
            }

            // --- CanvasGroup: đảm bảo KHÔNG chặn chuột ---
            var cg = window.GetComponent<CanvasGroup>();
            if (cg == null) cg = window.AddComponent<CanvasGroup>();
            // Các flag để tooltip không chặn tương tác UI
            cg.interactable = false;
            cg.blocksRaycasts = false;
            cg.ignoreParentGroups = true;

            Hide();
        }

        void OnEnable()
        {
            // nếu bật khi đã có item, đảm bảo không crash nếu ref thiếu
            if (window != null && window.activeSelf) SafePositionNearMouse();
        }

        void Update()
        {
            if (window != null && window.activeSelf) SafePositionNearMouse();
        }

        // Bao bọc PositionNearMouse với kiểm tra null để tránh exception
        private void SafePositionNearMouse()
        {
            if (rt == null || canvasRT == null || rootCanvas == null) return;
            PositionNearMouse();
        }

        private void PositionNearMouse()
        {
            // Kích thước canvas theo local rect
            Vector2 canvasSize = canvasRT.rect.size;
            Vector2 mouseScreen = Input.mousePosition;

            // Kích thước tooltip (sau layout)
            Vector2 tipSize = rt.rect.size;

            // Mặc định: bên phải – dưới con trỏ (mặc dù y của screen tăng lên trên)
            Vector2 screenTarget = mouseScreen + new Vector2(OFFSET_X, -OFFSET_Y);
            Vector2 pivot = new Vector2(0f, 1f); // trái–trên

            // Nếu tràn mép phải → đặt sang trái con trỏ
            if (screenTarget.x + tipSize.x + MARGIN > canvasSize.x)
            {
                screenTarget.x = mouseScreen.x - OFFSET_X;
                pivot.x = 1f; // phải
            }
            // Nếu tràn mép dưới (theo local) → đặt pivot xuống dưới
            if (screenTarget.y - tipSize.y - MARGIN < 0f)
            {
                pivot.y = 0f; // dưới
            }

            rt.pivot = pivot;

            // Đổi ScreenPoint -> local trong canvas
            Camera cam = (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : rootCanvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRT, screenTarget, cam, out var local);

            // Kẹp trong canvas (phòng hờ)
            Vector2 half = Vector2.Scale(tipSize, rt.pivot); // tipSize * pivot
            Vector2 min = -canvasSize * 0.5f + new Vector2(MARGIN, MARGIN) + half;
            Vector2 max = canvasSize * 0.5f - new Vector2(MARGIN, MARGIN) - (tipSize - half);
            local.x = Mathf.Clamp(local.x, min.x, max.x);
            local.y = Mathf.Clamp(local.y, min.y, max.y);

            rt.anchoredPosition = local;

            // Đảm bảo tooltip trên cùng
            window.transform.SetAsLastSibling();
        }

        public void Show(CGP.Gameplay.Items.ItemData item)
        {
            if (item == null || window == null) return;

            if (icon) icon.sprite = item.icon;
            if (nameText) nameText.text = item.itemName;
            if (descriptionText) descriptionText.text = item.description;

            if (!window.activeSelf) window.SetActive(true);
            SafePositionNearMouse();
        }

        public void Hide()
        {
            if (window != null) window.SetActive(false);
        }
    }
}