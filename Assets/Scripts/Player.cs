using System.Xml.Serialization;
using UnityEngine;
public class Player : MonoBehaviour
{
    public Inventory inventory; // Reference to the player's inventory

    private void Awake()
    {
        inventory = new Inventory(12); // Initialize the inventory with 10 slots
    }

    private void Update()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            Vector3Int position = new Vector3Int((int)transform.position.x, (int)transform.position.y, 0);

            if(GameManager.instance.tileManager.IsInteractable(position))
            {
                Debug.Log("Interacting with tile at position");
                GameManager.instance.tileManager.SetInteracted(position); // Set the tile at the player's position to interacted
            }
        }
    }
    public void DropItem (Collectable item)
    {
        Vector2 spawnLocation = transform.position;

        Vector2 spawnOffset = Random.insideUnitCircle * 1.25f; // Random offset for item spawn within a circle of radius 0.5


        Collectable droppedItem = Instantiate(item, spawnLocation + spawnOffset,Quaternion.identity); // Spawn the item at the player's position with a random offset 

        droppedItem.rb2d.AddForce(spawnOffset * 2f, ForceMode2D.Impulse); // Add force to the dropped item for a realistic drop effect

    }
}
