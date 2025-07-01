using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TileManager : MonoBehaviour
{
    public static TileManager Instance; // 🔁 Singleton để gọi từ Crop.cs

    [Header("Tilemap & Tile Assets")]
    public Tilemap interactableMap;
    public Tile hiddenInteractableTile;
    public Tile plowedTile;    // Summer_Plowed
    public Tile wateredTile;   // Summer_Watered
    public Tile interactableTile; // ✅ Thêm nếu muốn đặt lại sau khi thu hoạch

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);

        if (interactableMap == null)
        {
            Debug.LogError("[TileManager] interactableMap chưa được gán!");
        }
    }

    private void Start()
    {
        if (interactableMap == null) return;

        // Ẩn các tile có tên Interactable_Visible → chuyển sang hidden
        foreach (var position in interactableMap.cellBounds.allPositionsWithin)
        {
            TileBase tile = interactableMap.GetTile(position);

            if (tile != null && tile.name == "Interactable_Visible")
            {
                interactableMap.SetTile(position, hiddenInteractableTile);
            }
        }
    }

    /// <summary>
    /// Đặt tile thành đất đã cuốc (Summer_Plowed)
    /// </summary>
    public void SetInteracted(Vector3Int position)
    {
        if (interactableMap != null)
        {
            interactableMap.SetTile(position, plowedTile);
        }
    }

    /// <summary>
    /// Đặt tile thành đất đã tưới (Summer_Watered)
    /// </summary>
    public void SetWatered(Vector3Int position)
    {
        if (interactableMap != null)
        {
            TileBase tile = interactableMap.GetTile(position);

            if (tile != null && tile.name == plowedTile.name)
            {
                interactableMap.SetTile(position, wateredTile);
                Debug.Log($"💧 Tile tại {position} → chuyển thành Summer_Watered");
            }
        }
    }

    /// <summary>
    /// Đặt tile thành đất khô trở lại (Summer_Plowed)
    /// </summary>
    public void SetDry(Vector3Int position)
    {
        if (interactableMap != null)
        {
            TileBase tile = interactableMap.GetTile(position);

            if (tile != null && tile.name == wateredTile.name)
            {
                interactableMap.SetTile(position, plowedTile);
                Debug.Log($"🌤️ Tile tại {position} khô lại → chuyển về Summer_Plowed");
            }
            else
            {
                Debug.LogWarning($"[TileManager] Tile tại {position} không phải Summer_Watered, không thể setDry!");
            }
        }
    }

    /// <summary>
    /// Reset ô đất về Interactable sau khi thu hoạch
    /// </summary>
    public void ResetTile(Vector3Int position)
    {
        if (interactableMap != null)
        {
            interactableMap.SetTile(position, interactableTile);
            Debug.Log($"🔁 Reset ô {position} về Interactable");
        }
    }

    /// <summary>
    /// Trả về tên của tile tại vị trí chỉ định
    /// </summary>
    public string GetTileName(Vector3Int position)
    {
        if (interactableMap != null)
        {
            TileBase tile = interactableMap.GetTile(position);
            if (tile != null)
            {
                return tile.name;
            }
        }
        return "";
    }
}