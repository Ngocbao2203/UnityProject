using UnityEngine;

[CreateAssetMenu(fileName = "NewCrop", menuName = "Farming/Crop")]
public class CropData : ScriptableObject
{
    public string cropName;
    public Sprite cropIcon;
    public Sprite[] growthStages;     // Hình ảnh theo từng giai đoạn
    public int[] growthWaters;          // Ngày cần để chuyển stage
    public float[] growthStageTimes; // Thời gian mỗi stage (tính bằng giây), nếu dùng theo thời gian
    public GameObject harvestPrefab;  // Vật phẩm khi thu hoạch
}
