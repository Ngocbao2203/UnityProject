using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

// Gameplay deps (không phụ thuộc UI)
using CGP.Gameplay.Inventory.Presenter;     // InventoryManager
using CGP.Gameplay.Farming;                 // TileManager
using CGP.Gameplay.Items;                   // Item, ItemData
using CGP.Gameplay.InventorySystem;         // Inventory core (Inventory, Slot)
using CGP.Gameplay.Auth;

// Alias tránh nhầm tên namespace
using Inv = CGP.Gameplay.InventorySystem.Inventory;
using Slot = CGP.Gameplay.InventorySystem.Inventory.Slot;
using GM = global::GameManager;

namespace CGP.Gameplay.Systems
{
    public class Player : MonoBehaviour
    {
        [Header("Refs")]
        public InventoryManager inventoryManager;
        public TileManager tileManager;
        public Animator animator;
        [SerializeField] private GameObject cropParent;

        [Header("State")]
        [HideInInspector] public bool canMove = true;
        [HideInInspector] public Vector2 facingDirection = Vector2.down;

        private void Start()
        {
            // Lấy TileManager từ GameManager nếu chưa gán
            if (tileManager == null && GameManager.instance != null)
                tileManager = GameManager.instance.tileManager;

            if (tileManager == null || tileManager.interactableMap == null)
                Debug.LogError("[Player] TileManager / InteractableMap is null!");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space)) HandlePrimaryAction();
            if (Input.GetKeyDown(KeyCode.F)) TryHarvestCrop();
        }

        // ===== Debug tiện soi nguồn userId (gọi khi cần) =====
        private void DebugUserContext(string source)
        {
            string uidAuth = (AuthManager.Instance && AuthManager.Instance.IsUserDataReady)
                ? AuthManager.Instance.GetCurrentUserId()
                : "null";

            string uidGM = (GameManager.instance != null && !string.IsNullOrEmpty(GameManager.instance.userId))
                ? GameManager.instance.userId
                : "null";

            Debug.Log($"[UserCtx:{source}] Auth={uidAuth} | GM={uidGM}");
        }

        // === Helper: 1 nguồn sự thật → AuthManager; fallback GameManager nếu Auth chưa sẵn
        private string ResolveUserId()
        {
            if (AuthManager.Instance && AuthManager.Instance.IsUserDataReady)
            {
                var uid = AuthManager.Instance.GetCurrentUserId();
                return string.IsNullOrEmpty(uid) ? null : uid.Trim().Trim('{', '}');
            }

            // fallback (nếu thật sự cần)
            if (GameManager.instance && !string.IsNullOrEmpty(GameManager.instance.userId))
                return GameManager.instance.userId.Trim().Trim('{', '}');

            return null;
        }

        // ================= MAIN ACTION =================
        public void HandlePrimaryAction()
        {
            if (tileManager == null || tileManager.interactableMap == null) return;
            if (!TryGetSelectedItem(out var itemData, out var selectedSlot)) return;

            var targetCell = GetTargetCell();
            if (!tileManager.interactableMap.HasTile(targetCell)) return;

            var state = tileManager.GetStateByCell(targetCell);
            var status = state != null ? state.status : TileManager.TileStatus.Hidden;

            // Hoe → cuốc đất
            if (itemData.itemName == "Hoe" && status == TileManager.TileStatus.Hidden)
            { StartCoroutine(PerformHoeAction(targetCell)); return; }

            // Watering Can → tưới
            if (itemData.itemName == "WateringCan" &&
                (status == TileManager.TileStatus.Plowed || status == TileManager.TileStatus.Planted || status == TileManager.TileStatus.Watered))
            { StartCoroutine(PerformWateringAction(targetCell)); return; }

            // Seed → trồng
            if (itemData.itemType == ItemData.ItemType.Seed && itemData.cropPrefab != null &&
                (status == TileManager.TileStatus.Plowed || status == TileManager.TileStatus.Watered))
            { TryPlantCrop(targetCell, itemData, selectedSlot, status); return; }
        }

        // ================= HOE =================
        private IEnumerator PerformHoeAction(Vector3Int cell)
        {
            if (tileManager == null) yield break;

            LockMoveAndAim();
            if (animator) animator.SetTrigger("UseHoe");
            yield return new WaitForSeconds(0.5f);

            var uid = ResolveUserId();
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogError("[Player] userId is null/empty");
                DebugUserContext("Plow");
                yield break;
            }

            tileManager.DoPlow(cell, uid);
            canMove = true;
        }

        // ================= WATER =================
        private IEnumerator PerformWateringAction(Vector3Int cell)
        {
            if (tileManager == null) yield break;

            LockMoveAndAim();
            if (animator) animator.SetTrigger("UseWateringCan");
            yield return new WaitForSeconds(0.5f);

            var uid = ResolveUserId();
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogError("[Player] userId is null/empty");
                DebugUserContext("Water");
                yield break;
            }

            tileManager.DoWater(cell, uid);
            canMove = true;
        }

        // ================= PLANT =================
        private void TryPlantCrop(
            Vector3Int cell,
            ItemData itemData,
            Slot selectedSlot,
            TileManager.TileStatus statusBeforePlant)
        {
            if (tileManager == null) return;

            var uid = ResolveUserId();
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogError("[Player] userId is null/empty");
                DebugUserContext("Plant");
                return;
            }

            // Spawn crop prefab
            //var worldPos = tileManager.interactableMap.GetCellCenterWorld(cell);
            //var cropGO = Instantiate(itemData.cropPrefab, worldPos, Quaternion.identity);
            //if (cropParent) cropGO.transform.SetParent(cropParent.transform);

            // Gọi API Plant (TileManager sẽ apply state theo response)
            tileManager.DoPlant(cell, uid, itemData.id);

            // Trừ hạt & sync: tự tìm index của selectedSlot trong Toolbar (không cần Toolbar_UI)
            if (inventoryManager != null && inventoryManager.toolbar != null && selectedSlot != null)
            {
                var idx = inventoryManager.toolbar.slots.IndexOf(selectedSlot);
                if (idx >= 0)
                {
                    InventoryManager.Instance.UseItem(InventoryManager.TOOLBAR, idx);
                    _ = InventoryManager.Instance.SyncInventory(InventoryManager.TOOLBAR); // fire-and-forget
                    // UI (nếu có) sẽ nghe OnInventoryLoaded để tự refresh
                }
            }
        }

        // ================= HARVEST =================
        private void TryHarvestCrop()
        {
            if (tileManager == null) return;

            var uid = ResolveUserId();
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogError("[Player] userId is null/empty");
                DebugUserContext("Harvest");
                return;
            }

            var cell = GetTargetCell();
            tileManager.DoHarvest(cell, uid);
        }

        // ================= HELPERS =================
        // Lấy selected item trực tiếp từ Inventory Core (không sờ tới Toolbar_UI)
        private bool TryGetSelectedItem(out ItemData itemData, out Slot selectedSlot)
        {
            itemData = null;
            selectedSlot = null;

            if (inventoryManager == null || inventoryManager.toolbar == null) return false;

            selectedSlot = inventoryManager.toolbar.selectedSlot;
            if (selectedSlot == null || selectedSlot.IsEmpty) return false;

            itemData = selectedSlot.itemData;
            return itemData != null;
        }

        private Vector3Int GetTargetCell()
        {
            var currentCell = Vector3Int.FloorToInt(transform.position);
            return currentCell + new Vector3Int((int)facingDirection.x, (int)facingDirection.y, 0);
        }

        private void LockMoveAndAim()
        {
            canMove = false;
            if (animator != null)
            {
                animator.SetFloat("horizontal", facingDirection.x);
                animator.SetFloat("vertical", facingDirection.y);
            }
        }

        // ================= DROP ITEMS =================
        public void DropItem(Item item)
        {
            Vector2 spawnLocation = transform.position;
            Vector2 spawnOffset = Random.insideUnitCircle * 1.25f;
            Item droppedItem = Instantiate(item, spawnLocation + spawnOffset, Quaternion.identity);
            droppedItem.rb2d.AddForce(spawnOffset * 2f, ForceMode2D.Impulse);
        }

        public void DropItem(Item item, int numToDrop)
        {
            for (int i = 0; i < numToDrop; i++) DropItem(item);
        }
    }
}
