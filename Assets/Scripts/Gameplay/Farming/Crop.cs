using UnityEngine;
using CGP.Gameplay.Items;

namespace CGP.Gameplay.Farming
{
    public class Crop : MonoBehaviour
    {
        public CropData cropData;

        [HideInInspector] public int tileId;
        [HideInInspector] public string seedId;

        // UI sẽ đăng ký lắng nghe sự kiện này ở runtime
        public static System.Action<Crop> OnCropClicked;

        private SpriteRenderer spriteRenderer;

        // === Các biến expose ra ngoài ===
        public int CurrentStage => currentStage;
        public bool HasBeenWatered => Time.time - lastWaterTime < requiredInterval;
        public float TimeLeftToNextStage => cropData != null && cropData.growthStageTimes.Length > currentStage
            ? cropData.growthStageTimes[currentStage] - growthTimer
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
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            if (!spriteRenderer)
            {
                Debug.LogError("[Crop] Missing SpriteRenderer");
                return;
            }
            if (cropData == null || cropData.growthStages == null || cropData.growthStages.Length == 0)
                return;

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

                if (cropData != null &&
                    currentStage < cropData.growthStageTimes.Length &&
                    growthTimer >= cropData.growthStageTimes[currentStage])
                {
                    currentStage++;
                    growthTimer = 0f;
                    isWaitingForNextStage = false;

                    if (cropData.growthStages != null &&
                        currentStage < cropData.growthStages.Length)
                    {
                        spriteRenderer.sprite = cropData.growthStages[currentStage];
                        Debug.Log($"🌱 [Crop] {cropData.cropName} sang stage {currentStage}");
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
                Debug.LogWarning($"[Crop] {cropData?.cropName} đã tưới và đang đợi lên stage.");
                return;
            }

            float now = Time.time;

            if (IsMature())
            {
                Debug.LogWarning($"[Crop] {cropData?.cropName} đã trưởng thành, không cần tưới.");
                return;
            }

            if (now - lastWaterTime < requiredInterval)
            {
                float remaining = requiredInterval - (now - lastWaterTime);
                Debug.LogWarning($"[Crop] Tưới quá nhanh! Chờ thêm {remaining:F1}s.");
                return;
            }

            lastWaterTime = now;
            timesWatered++;
            spriteRenderer.color = Color.gray;
            tileSetBack = false;

            if (cropData == null || currentStage >= cropData.growthWaters.Length)
            {
                Debug.LogError($"[Crop] Thiếu growthWaters cho stage {currentStage}");
                return;
            }

            int requiredWaters = cropData.growthWaters[currentStage];
            Debug.Log($"[Crop] {cropData.cropName}: {timesWatered}/{requiredWaters} lần tưới ở stage {currentStage}");

            if (timesWatered >= requiredWaters)
            {
                timesWatered = 0;

                if (cropData != null && currentStage < cropData.growthStageTimes.Length)
                {
                    isWaitingForNextStage = true;
                    growthTimer = 0f;
                    Debug.Log($"[Crop] Đợi {cropData.growthStageTimes[currentStage]}s để lên stage.");
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

            isWaitingForNextStage = false;
            growthTimer = 0f;
            tileSetBack = true;
        }

        public void StartWaitingFromServer(float? remainingSec = null)
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            isWaitingForNextStage = true;

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

            if (spriteRenderer) spriteRenderer.color = Color.gray;
            tileSetBack = false;
            lastWaterTime = Time.time;
        }

        public bool IsWaitingForNextStage() => isWaitingForNextStage;
        public bool IsMature() => cropData != null && currentStage == cropData.growthStages.Length - 1;

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
            Debug.Log($"[Crop] Click: {name}");
            OnCropClicked?.Invoke(this);   // chỉ bắn event, không gọi UI trực tiếp
        }
    }
}
