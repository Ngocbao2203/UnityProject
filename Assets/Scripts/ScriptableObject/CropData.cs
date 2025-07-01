using UnityEngine;

[CreateAssetMenu(fileName = "NewCrop", menuName = "Farming/Crop")]
public class CropData : ScriptableObject
{
    public string cropName;
    public Sprite[] growthStages;     // Hình ảnh theo từng giai đoạn
    public int[] growthWaters;          // Ngày cần để chuyển stage
    public GameObject harvestPrefab;  // Vật phẩm khi thu hoạch
}
