using UnityEngine;

public class Collectable : MonoBehaviour
{
    public CollectableType type;
    public Sprite icon;

    public Rigidbody2D rb2d; // Reference to the Rigidbody2D component for physics interactions

    private void Awake()
    {
        rb2d = GetComponent<Rigidbody2D>(); // Get the Rigidbody2D component attached to this GameObject
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        Player player = collision.GetComponent<Player>();
        if (player)
        {
            player.inventory.Add(this);
            Destroy(this.gameObject);
        }

    }
}

public enum CollectableType
{
    NONE, CARROOT_SEED
}

