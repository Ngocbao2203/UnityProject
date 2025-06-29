using UnityEngine;

public class Player : MonoBehaviour
{
    public InventoryManager inventoryManager;
    public TileManager tileManager;

    private Vector2Int facingDirection = Vector2Int.down;

    private void Start()
    {
        tileManager = GameManager.instance.tileManager;
    }

    void Update()
    {
        // Cập nhật hướng nếu có di chuyển
        if (Input.GetKeyDown(KeyCode.W)) facingDirection = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.S)) facingDirection = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.A)) facingDirection = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.D)) facingDirection = Vector2Int.right;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (tileManager != null)
            {
                Vector3Int currentCell = Vector3Int.FloorToInt(transform.position);
                Vector3Int targetCell = currentCell + new Vector3Int(facingDirection.x, facingDirection.y, 0);

                string tileName = tileManager.GetTileName(targetCell);
                if (!string.IsNullOrWhiteSpace(tileName))
                {
                    if (tileName == "Interactable" && inventoryManager.toolbar.selectedSlot.itemName == "Hoe")
                    {
                        tileManager.SetInteracted(targetCell);
                    }
                }
            }
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
