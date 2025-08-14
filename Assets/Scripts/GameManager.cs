using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    [SerializeField] public ItemManager itemManager;
    [SerializeField] public TileManager tileManager;
    [SerializeField] public UI_Manager uiManager;
    [SerializeField] public Player player;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(this.gameObject);

        if (itemManager == null) itemManager = GetComponent<ItemManager>();
        if (tileManager == null) tileManager = GetComponent<TileManager>();
        if (uiManager == null) uiManager = GetComponent<UI_Manager>();
        if (player == null)
        {
            player = FindFirstObjectByType<Player>(); // Thay FindObjectOfType bằng FindFirstObjectByType
            if (player == null) Debug.LogError("Player not found in scene!");
        }
    }
}