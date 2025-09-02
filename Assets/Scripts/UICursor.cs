using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UICursor : MonoBehaviour
{
    public static UICursor Instance { get; private set; }

    [Header("Refs")]
    [SerializeField] private Canvas targetCanvas;   // Canvas chứa UICursor
    [SerializeField] private Image cursorImage;     // Image làm con trỏ

    [Header("Options")]
    [Tooltip("Ẩn con trỏ hệ thống")]
    [SerializeField] private bool hideSystemCursor = true;
    [Tooltip("Ẩn UICursor khi mở menu pause...")]
    [SerializeField] private bool hideWhenTimeScaleZero = false;

    [Header("Default Style")]
    [SerializeField] private Sprite defaultSprite;
    [SerializeField, Range(0.25f, 3f)] private float defaultScale = 1f;

    [System.Serializable]
    public class CursorStyle
    {
        public string key;           // ví dụ: "default", "hoe", "water", "harvest"
        public Sprite sprite;
        [Range(0.25f, 3f)] public float scale = 1f;
        [Tooltip("Pivot 0..1 tương ứng điểm click. Nếu để (-1,-1) sẽ dùng pivot hiện tại của RectTransform")]
        public Vector2 pivot01 = new Vector2(-1f, -1f);
    }

    [Header("Styles (optional)")]
    public List<CursorStyle> styles = new();
    private Dictionary<string, CursorStyle> styleMap;

    RectTransform _rt;
    Camera _cam;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _rt = cursorImage.rectTransform;

        if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>();
        _cam = targetCanvas != null ? targetCanvas.worldCamera : null;

        if (cursorImage != null) cursorImage.raycastTarget = false; // không chặn click
        if (hideSystemCursor) Cursor.visible = false;

        // map styles
        styleMap = new Dictionary<string, CursorStyle>();
        foreach (var s in styles)
            if (!string.IsNullOrEmpty(s.key) && !styleMap.ContainsKey(s.key))
                styleMap.Add(s.key, s);

        // set mặc định
        if (defaultSprite != null) SetCursor("default", defaultSprite, defaultScale);
    }

    void OnEnable()
    {
        if (hideSystemCursor) Cursor.visible = false;
    }

    void OnDisable()
    {
        // khi tắt object này, trả chuột hệ thống lại để tránh mất chuột
        Cursor.visible = true;
    }

    void Update()
    {
        if (hideWhenTimeScaleZero && Mathf.Approximately(Time.timeScale, 0f))
        {
            cursorImage.enabled = false;
            return;
        }
        else cursorImage.enabled = true;

        // đưa Image tới vị trí con trỏ theo loại Canvas
        Vector2 pos = Input.mousePosition;

        if (targetCanvas == null || targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            _rt.position = pos;
        }
        else // ScreenSpace-Camera hoặc WorldSpace
        {
            RectTransform canvasRect = targetCanvas.transform as RectTransform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, pos, _cam, out Vector2 local))
                _rt.localPosition = local;
        }
    }
    void LateUpdate()
    {
        if (Input.GetMouseButtonDown(0))
            cursorImage.transform.localScale = Vector3.one * 0.9f;
        if (Input.GetMouseButtonUp(0))
            cursorImage.transform.localScale = Vector3.one; // hoặc tween mượt hơn
    }


    /// <summary>
    /// Set con trỏ theo key đã khai báo trong Styles.
    /// </summary>
    public void SetCursor(string key)
    {
        if (string.IsNullOrEmpty(key) || !styleMap.TryGetValue(key, out var style))
        {
            // quay về default nếu không thấy key
            SetCursor("default", defaultSprite, defaultScale);
            return;
        }
        ApplyStyle(style);
    }

    /// <summary>
    /// Set con trỏ thủ công (sprite + scale).
    /// </summary>
    public void SetCursor(string key, Sprite sprite, float scale = 1f)
    {
        cursorImage.sprite = sprite;
        _rt.localScale = Vector3.one * scale;
        // Giữ nguyên pivot hiện tại của RectTransform để làm hotspot
    }

    private void ApplyStyle(CursorStyle style)
    {
        cursorImage.sprite = style.sprite;
        _rt.localScale = Vector3.one * (style.scale <= 0 ? 1f : style.scale);

        // nếu có pivot custom → set pivot làm hotspot
        if (style.pivot01.x >= 0f && style.pivot01.y >= 0f)
            _rt.pivot = style.pivot01;
    }

    public void Show(bool show)
    {
        cursorImage.enabled = show;
        if (hideSystemCursor) Cursor.visible = !show;
    }
}
