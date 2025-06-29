using UnityEngine;

public class CropInstance : MonoBehaviour
{
    public CropData cropData;
    public int growthStage = 0;
    public int daysSincePlanted = 0;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateSprite();
    }

    public void Grow()
    {
        if (growthStage >= cropData.growthStages.Length - 1) return;

        daysSincePlanted++;

        if (daysSincePlanted >= cropData.growthDays[growthStage])
        {
            growthStage++;
            daysSincePlanted = 0;
            UpdateSprite();
        }
    }

    private void UpdateSprite()
    {
        spriteRenderer.sprite = cropData.growthStages[growthStage];
    }

    public bool CanHarvest()
    {
        return growthStage == cropData.growthStages.Length - 1;
    }

    public void Harvest()
    {
        if (!CanHarvest()) return;

        Instantiate(cropData.harvestPrefab, transform.position, Quaternion.identity);
        Destroy(gameObject); // hoặc reset nếu bạn muốn trồng lại
    }
}

