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

    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            Debug.LogError("[Crop] Thiếu SpriteRenderer!");
            return;
        }

        if (cropData == null || cropData.growthStages.Length == 0)
        {
            Debug.LogWarning("[Crop] Thiếu cropData hoặc không có sprite stages!");
            return;
        }

        spriteRenderer.sprite = cropData.growthStages[0];
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
