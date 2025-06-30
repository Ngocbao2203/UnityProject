using System.Collections;
using UnityEngine;

public class Player : MonoBehaviour
{
    public InventoryManager inventoryManager;
    public TileManager tileManager;
    public Animator animator;

    [HideInInspector] public Vector2 facingDirection = Vector2.down; // cập nhật từ Movement.cs

    private void Start()
    {
        tileManager = GameManager.instance.tileManager;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (tileManager != null)
            {
                Vector3Int currentCell = Vector3Int.FloorToInt(transform.position);
                Vector3Int targetCell = currentCell + new Vector3Int((int)facingDirection.x, (int)facingDirection.y, 0);

                string tileName = tileManager.GetTileName(targetCell);
                if (!string.IsNullOrWhiteSpace(tileName))
                {
                    if (tileName == "Interactable" && inventoryManager.toolbar.selectedSlot.itemName == "Hoe")
                    {
                        // Gọi Coroutine để delay cuốc đất
                        StartCoroutine(PerformHoeAction(targetCell));
                    }
                }
            }
        }
    }

    private IEnumerator PerformHoeAction(Vector3Int targetCell)
    {
        // 1. Gửi hướng vào Animator để Blend Tree chọn đúng hướng
        animator.SetFloat("horizontal", facingDirection.x);
        animator.SetFloat("vertical", facingDirection.y);

        // 2. Trigger animation Hoe
        animator.SetTrigger("UseHoe");

        // 3. Đợi animation hoàn tất (0.5s hoặc đúng thời gian thật)
        yield return new WaitForSeconds(0.5f);

        // 4. Thực hiện cuốc đất
        tileManager.SetInteracted(targetCell);
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
