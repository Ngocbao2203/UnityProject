using UnityEngine;

public class Crop : MonoBehaviour
{
    public CropData cropData;

    private SpriteRenderer spriteRenderer;
    private int currentStage = 0;
    private int timesWatered = 0;
    private float lastWaterTime = -999f;
    private bool tileSetBack = false;

    [Tooltip("Thời gian tối thiểu giữa 2 lần tưới (giây)")]
    public float requiredInterval = 3f;

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

        // Đổi màu cây theo trạng thái có thể tưới
        if (Time.time - lastWaterTime >= requiredInterval)
        {
            spriteRenderer.color = Color.white;

            // Nếu trước đó đã tưới nhưng giờ hết hạn → reset đất khô
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

    public void Water()
    {
        if (spriteRenderer == null || cropData == null) return;

        float now = Time.time;

        if (IsMature())
        {
            Debug.LogWarning($"⚠️ [Crop] {cropData.cropName} đã trưởng thành. Không cần tưới nữa.");
            return;
        }

        if (now - lastWaterTime >= requiredInterval)
        {
            lastWaterTime = now;
            timesWatered++;

            // ✅ Kiểm tra index an toàn
            if (currentStage >= cropData.growthWaters.Length)
            {
                Debug.LogError($"❌ [Crop] growthWaters không có dữ liệu cho stage {currentStage}");
                return;
            }

            int requiredWaters = cropData.growthWaters[currentStage];
            Debug.Log($"💧 [Crop] {cropData.cropName}: {timesWatered}/{requiredWaters} lần tưới ở giai đoạn {currentStage}");

            if (timesWatered >= requiredWaters && currentStage < cropData.growthStages.Length - 1)
            {
                currentStage++;
                timesWatered = 0;

                if (currentStage < cropData.growthStages.Length)
                {
                    spriteRenderer.sprite = cropData.growthStages[currentStage];
                    Debug.Log($"🌾 [Crop] {cropData.cropName} đã lớn lên giai đoạn {currentStage}!");
                }
            }
        }
        else
        {
            float remaining = requiredInterval - (now - lastWaterTime);
            Debug.LogWarning($"⚠️ [Crop] Bạn tưới quá nhanh! Chờ thêm {remaining:F1}s để tưới tiếp.");
        }
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

            // ✅ Reset tile về tương tác sau khi thu hoạch
            Vector3Int tilePos = TileManager.Instance.interactableMap.WorldToCell(transform.position);
            TileManager.Instance.ResetTile(tilePos);

            Destroy(gameObject);
        }
    }

}