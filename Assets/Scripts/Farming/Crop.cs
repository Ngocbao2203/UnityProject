using UnityEngine;

public class Crop : MonoBehaviour
{
    public CropData cropData;

    private SpriteRenderer spriteRenderer;

    // === Các biến bạn cần truy cập từ ngoài ===
    public int CurrentStage => currentStage;                      // Giai đoạn hiện tại (dạng chỉ đọc)
    public bool HasBeenWatered => Time.time - lastWaterTime < requiredInterval; // Có được tưới gần đây không
    public float TimeLeftToNextStage => cropData.growthStageTimes.Length > currentStage
        ? cropData.growthStageTimes[currentStage] - growthTimer  // Thời gian còn lại
        : 0f;

    private int currentStage = 0;
    private int timesWatered = 0;
    private float lastWaterTime = -999f;
    private bool tileSetBack = false;

    [Tooltip("Thời gian tối thiểu giữa 2 lần tưới (giây)")]
    public float requiredInterval = 3f;

    private float growthTimer = 0f;
    private bool isWaitingForNextStage = false;

    private void Awake()
    {
        // Cache sớm để các hàm được gọi trước Start() vẫn dùng được
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (spriteRenderer == null)
        {
            Debug.LogError("[Crop] Thiếu SpriteRenderer!");
            return;
        }

        if (cropData == null || cropData.growthStages == null || cropData.growthStages.Length == 0)
        {
            Debug.LogWarning("[Crop] Thiếu cropData hoặc không có sprite stages!");
            return;
        }

        // ✅ ĐỪNG ép về [0] nữa, dùng currentStage hiện tại
        int idx = Mathf.Clamp(currentStage, 0, cropData.growthStages.Length - 1);
        spriteRenderer.sprite = cropData.growthStages[idx];
        spriteRenderer.color = Color.white;
    }

    private void Update()
    {
        if (spriteRenderer == null) return;

        if (isWaitingForNextStage)
        {
            growthTimer += Time.deltaTime;

            if (currentStage < cropData.growthStageTimes.Length &&
                growthTimer >= cropData.growthStageTimes[currentStage])
            {
                currentStage++;
                growthTimer = 0f;
                isWaitingForNextStage = false;

                if (currentStage < cropData.growthStages.Length)
                {
                    spriteRenderer.sprite = cropData.growthStages[currentStage];
                    Debug.Log($"🌱 [Crop] {cropData.cropName} phát triển sang stage {currentStage}!");
                }

                spriteRenderer.color = Color.white;

                if (!tileSetBack && TileManager.Instance != null)
                {
                    Vector3Int tilePos = TileManager.Instance.interactableMap.WorldToCell(transform.position);
                    TileManager.Instance.SetDry(tilePos);
                    tileSetBack = true;
                }
            }
            else
            {
                spriteRenderer.color = Color.gray;
            }
        }
        else
        {
            if (Time.time - lastWaterTime >= requiredInterval)
            {
                spriteRenderer.color = Color.white;

                if (!tileSetBack && TileManager.Instance != null)
                {
                    Vector3Int tilePos = TileManager.Instance.interactableMap.WorldToCell(transform.position);
                    TileManager.Instance.SetDry(tilePos);
                    tileSetBack = true;
                }
            }
            else
            {
                spriteRenderer.color = Color.gray;
                tileSetBack = false;
            }
        }
    }

    public void Water()
    {
        if (isWaitingForNextStage)
        {
            Debug.LogWarning($"⚠️ [Crop] {cropData.cropName} đã được tưới và đang chờ phát triển. Không thể tưới thêm.");
            return;
        }

        float now = Time.time;

        if (IsMature())
        {
            Debug.LogWarning($"⚠️ [Crop] {cropData.cropName} đã trưởng thành. Không cần tưới nữa.");
            return;
        }

        if (now - lastWaterTime < requiredInterval)
        {
            float remaining = requiredInterval - (now - lastWaterTime);
            Debug.LogWarning($"⚠️ [Crop] Bạn tưới quá nhanh! Chờ thêm {remaining:F1}s.");
            return;
        }

        lastWaterTime = now;
        timesWatered++;
        spriteRenderer.color = Color.gray;
        tileSetBack = false;

        if (currentStage >= cropData.growthWaters.Length)
        {
            Debug.LogError($"❌ [Crop] growthWaters không có dữ liệu cho stage {currentStage}");
            return;
        }

        int requiredWaters = cropData.growthWaters[currentStage];
        Debug.Log($"💧 [Crop] {cropData.cropName}: {timesWatered}/{requiredWaters} lần tưới ở stage {currentStage}");

        if (timesWatered >= requiredWaters)
        {
            timesWatered = 0;

            if (currentStage < cropData.growthStageTimes.Length)
            {
                isWaitingForNextStage = true;
                growthTimer = 0f;
                Debug.Log($"⏳ [Crop] {cropData.cropName} đang đợi {cropData.growthStageTimes[currentStage]} giây để phát triển.");
            }
            else
            {
                Debug.LogWarning($"⚠️ [Crop] Không có thời gian phát triển cho stage {currentStage}");
            }
        }
    }

    public void SetStageInstant(int stage)
    {
        if (cropData == null || cropData.growthStages == null || cropData.growthStages.Length == 0) return;

        currentStage = Mathf.Clamp(stage, 0, cropData.growthStages.Length - 1);
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = cropData.growthStages[currentStage];
            spriteRenderer.color = Color.white;
        }

        // reset các biến chờ
        isWaitingForNextStage = false;
        growthTimer = 0f;
        tileSetBack = true;            // để Update có thể set đất khô nếu cần
    }

    /// <summary>
    /// Server báo ô đang được tưới và đang đếm sang stage tiếp theo.
    /// Nếu server có thời điểm kết thúc, truyền remainingSec (giây còn lại).
    /// </summary>
    public void StartWaitingFromServer(float? remainingSec = null)
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        isWaitingForNextStage = true;

        // Nếu có thời gian còn lại từ server, nội suy vào growthTimer để còn đúng nhịp
        if (cropData != null && cropData.growthStageTimes != null && currentStage < cropData.growthStageTimes.Length)
        {
            float duration = Mathf.Max(0f, cropData.growthStageTimes[currentStage]);
            if (remainingSec.HasValue)
            {
                float remain = Mathf.Clamp(remainingSec.Value, 0f, duration);
                growthTimer = Mathf.Clamp(duration - remain, 0f, duration);
            }
            else
            {
                growthTimer = 0f;
            }
        }
        else
        {
            growthTimer = 0f;
        }

        // cho cảm giác "ướt"
        if (spriteRenderer) spriteRenderer.color = Color.gray;
        tileSetBack = false;
        lastWaterTime = Time.time;   // để HasBeenWatered = true một lúc
    }

    public bool IsWaitingForNextStage()
    {
        return isWaitingForNextStage;
    }

    public bool IsMature()
    {
        return currentStage == cropData.growthStages.Length - 1;
    }

    public void Harvest()
    {
        if (spriteRenderer == null || cropData == null) return;

        if (IsMature())
        {
            if (cropData.harvestPrefab != null)
            {
                Instantiate(cropData.harvestPrefab, transform.position, Quaternion.identity);
            }

            Vector3Int tilePos = TileManager.Instance.interactableMap.WorldToCell(transform.position);
            TileManager.Instance.ResetTile(tilePos);

            Destroy(gameObject);
        }
    }
    private void OnMouseDown()
    {
        if (cropData == null)
        {
            Debug.LogWarning("[Crop] Chưa có cropData!");
            return;
        }

        Debug.Log($"🖱 [Crop] Clicked on {cropData.cropName} at stage {currentStage}.");
        CropInfo_UI.Instance?.ShowCropInfo(this);
    }
}
