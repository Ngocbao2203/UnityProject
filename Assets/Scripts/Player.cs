﻿using System.Collections;
using UnityEngine;

public class Player : MonoBehaviour
{
    public InventoryManager inventoryManager;
    public TileManager tileManager;
    public Animator animator;

    [HideInInspector] public bool canMove = true;

    [SerializeField] private GameObject cropParent;

    [HideInInspector] public Vector2 facingDirection = Vector2.down;

    private void Start()
    {
        tileManager = GameManager.instance.tileManager;
    }

    void Update()
    {
        // 1️⃣ Hành động chính bằng phím Space
        if (Input.GetKeyDown(KeyCode.Space))
        {
            HandlePrimaryAction();
        }

        // 2️⃣ Thu hoạch cây bằng phím F
        if (Input.GetKeyDown(KeyCode.F))
        {
            TryHarvestCrop();
        }
    }

    public void HandlePrimaryAction()
    {
        Toolbar_UI toolbarUI = FindFirstObjectByType<Toolbar_UI>();
        if (toolbarUI == null || toolbarUI.selectedSlot == null)
        {
            Debug.LogWarning("[Plant] Không có slot được chọn từ Toolbar_UI!");
            return;
        }

        Inventory.Slot selectedSlot = toolbarUI.selectedSlot.GetSlot();
        if (selectedSlot == null || selectedSlot.IsEmpty)
        {
            Debug.LogWarning("[Plant] Slot trống hoặc không hợp lệ!");
            return;
        }

        ItemData itemData = selectedSlot.itemData;
        if (itemData == null)
        {
            Debug.LogWarning("⚠️ [Plant] ItemData null!");
            return;
        }

        Vector3Int currentCell = Vector3Int.FloorToInt(transform.position);
        Vector3Int targetCell = currentCell + new Vector3Int((int)facingDirection.x, (int)facingDirection.y, 0);
        string tileName = tileManager.GetTileName(targetCell);

        // 🪓 Cuốc đất
        if (itemData.itemName == "Hoe" && tileName == "Interactable")
        {
            Debug.Log($"🪓 Cuốc đất tại {targetCell}");
            StartCoroutine(PerformHoeAction(targetCell));
            return;
        }

        // 💧 Tưới cây
        if (itemData.itemName == "WateringCan" && (tileName == "Summer_Plowed" || tileName == "Summer_Watered"))
        {
            Debug.Log($"💧 Tưới cây tại {targetCell}");
            StartCoroutine(PerformWateringAction(targetCell));
            return;
        }

        // 🌱 Trồng cây
        if (itemData.itemType == ItemData.ItemType.Seed && itemData.cropPrefab != null &&
            (tileName == "Summer_Plowed" || tileName == "Summer_Watered"))
        {
            Debug.Log($"🌱 Kiểm tra trồng cây {itemData.itemName} tại {targetCell}");
            TryPlantCrop(targetCell, itemData, tileName, toolbarUI, selectedSlot); // Truyền toolbarUI và selectedSlot
            return;
        }

        // ⚠️ Cảnh báo không thể trồng
        if (itemData.cropPrefab == null)
        {
            Debug.LogWarning($"⚠️ [Plant] Item '{itemData.itemName}' không phải hạt giống (cropPrefab == null)");
        }
        if (tileName != "Summer_Plowed" && tileName != "Summer_Watered")
        {
            Debug.LogWarning($"⚠️ [Plant] Không thể trồng tại: {tileName}.");
        }
    }

    private IEnumerator PerformHoeAction(Vector3Int targetCell)
    {
        canMove = false;

        animator.SetFloat("horizontal", facingDirection.x);
        animator.SetFloat("vertical", facingDirection.y);
        animator.SetTrigger("UseHoe");

        yield return new WaitForSeconds(0.5f);
        tileManager.SetInteracted(targetCell);

        canMove = true;
    }

    private IEnumerator PerformWateringAction(Vector3Int targetCell)
    {
        canMove = false;

        Vector3 world = tileManager.interactableMap.GetCellCenterWorld(targetCell);
        Collider2D col = Physics2D.OverlapCircle(world, 0.25f);

        // Nếu có cây
        if (col != null && col.TryGetComponent(out Crop crop))
        {
            // ⛔ Nếu cây đang chờ phát triển → không cho tưới
            if (crop.IsWaitingForNextStage())
            {
                Debug.LogWarning($"⚠️ [Crop] {crop.cropData.cropName} đã được tưới và đang chờ phát triển. Đừng tưới nữa!");
                canMove = true;
                yield break; // ⛔ Không thực hiện gì nữa
            }
        }

        // ✅ Nếu không bị chặn, thực hiện animation tưới
        animator.SetFloat("horizontal", facingDirection.x);
        animator.SetFloat("vertical", facingDirection.y);
        animator.SetTrigger("UseWateringCan");

        yield return new WaitForSeconds(0.5f);

        // Sau khi tưới xong animation
        if (col != null && col.TryGetComponent(out crop))
        {
            tileManager.SetWatered(targetCell);
            crop.Water();
            Debug.Log($"💦 Đã tưới cây: {crop.cropData.cropName}");
        }
        else if (col != null)
        {
            Debug.Log($"⚠️ Tìm thấy vật thể không phải Crop: {col.name}");
        }
        else
        {
            Debug.Log($"⚠️ Không tìm thấy cây nào tại {targetCell} để tưới.");
        }

        canMove = true;
    }

    private void TryPlantCrop(Vector3Int targetCell, ItemData itemData, string tileName, Toolbar_UI toolbarUI, Inventory.Slot selectedSlot)
    {
        Vector3 worldPos = tileManager.interactableMap.CellToWorld(targetCell) + new Vector3(0.5f, 0.5f, 0f);

        // 🔍 Kiểm tra xem đã có cây ở ô này chưa, tăng bán kính phát hiện
        Collider2D existingCrop = Physics2D.OverlapCircle(worldPos, 0.5f); // Tăng từ 0.25f lên 0.5f
        if (existingCrop != null && existingCrop.GetComponent<Crop>() != null)
        {
            Debug.LogWarning($"⚠️ [Plant] Đã có cây ở ô {targetCell}, không thể trồng đè!");
            return; // Không giảm số lượng nếu có cây
        }

        // 🌱 Tiến hành trồng cây
        GameObject cropGO = Instantiate(itemData.cropPrefab, worldPos, Quaternion.identity);
        if (cropParent != null)
        {
            cropGO.transform.SetParent(cropParent.transform);
        }

        // 💧 Nếu trồng trên đất đã tưới → tính 1 lần tưới
        if (tileName == "Summer_Watered" && cropGO.TryGetComponent(out Crop crop))
        {
            crop.Water();
            Debug.Log("💧 [Plant] Trồng trên đất đã tưới → tính 1 lần tưới.");
        }

        // Giảm số lượng chỉ khi trồng thành công
        int selectedIndex = toolbarUI.toolbarSlots.IndexOf(toolbarUI.selectedSlot);
        if (selectedIndex >= 0)
        {
            InventoryManager.Instance.UseItem(InventoryManager.TOOLBAR, selectedIndex);
            toolbarUI.selectedSlot.UpdateSlotUI();
            Debug.Log($"🌱 Trồng thành công {itemData.itemName}, số lượng còn lại: {selectedSlot.count}");
        }
    }

    private void TryHarvestCrop()
    {
        Vector3Int currentCell = Vector3Int.FloorToInt(transform.position);
        Vector3Int targetCell = currentCell + new Vector3Int((int)facingDirection.x, (int)facingDirection.y, 0);
        Vector3 world = tileManager.interactableMap.GetCellCenterWorld(targetCell);

        Collider2D col = Physics2D.OverlapCircle(world, 0.25f);

        if (col != null && col.TryGetComponent(out Crop crop))
        {
            if (crop.IsMature())
            {
                Debug.Log($"🧺 Thu hoạch cây {crop.cropData.cropName}!");
                crop.Harvest();

                // ✅ Reset lại ô đất thành "Interactable"
                tileManager.ResetTile(targetCell);
            }
            else
            {
                Debug.Log($"🌱 Cây {crop.cropData.cropName} chưa thể thu hoạch.");
            }
        }
        else
        {
            Debug.Log("❌ Không có cây nào để thu hoạch trước mặt.");
        }
    }

    public void DropItem(Item item)
    {
        Vector2 spawnLocation = transform.position;
        Vector2 spawnOffset = Random.insideUnitCircle * 1.25f;

        Item droppedItem = Instantiate(item, spawnLocation + spawnOffset, Quaternion.identity);
        droppedItem.rb2d.AddForce(spawnOffset * 2f, ForceMode2D.Impulse);
    }

    public void DropItem(Item item, int numToDrop)
    {
        for (int i = 0; i < numToDrop; i++)
        {
            DropItem(item);
        }
    }
}