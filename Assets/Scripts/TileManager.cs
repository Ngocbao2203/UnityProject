using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TileManager : MonoBehaviour
{
    public static TileManager Instance;
    [Header("Tilemap & Tile Assets")]
    public Tilemap interactableMap;
    public Tile hiddenInteractableTile;
    public Tile plowedTile; // Summer_Plowed
    public Tile wateredTile; // Summer_Watered
    public Tile interactableTile; // Sử dụng để reset

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
        foreach (var position in interactableMap.cellBounds.allPositionsWithin)
        {
            TileBase tile = interactableMap.GetTile(position);
            if (tile != null && tile.name == "Interactable_Visible")
            {
                interactableMap.SetTile(position, hiddenInteractableTile);
            }
        }
    }

    public void SetInteracted(Vector3Int position)
    {
        if (interactableMap != null && interactableMap.GetTile(position) == hiddenInteractableTile)
        {
            interactableMap.SetTile(position, plowedTile);
            Debug.Log($"🪓 Đặt tile tại {position} thành Summer_Plowed");
        }
    }

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

    public void ResetTile(Vector3Int position)
    {
        if (interactableMap != null)
        {
            interactableMap.SetTile(position, interactableTile);
            Debug.Log($"🔁 Reset ô {position} về Interactable");
        }
    }

    public string GetTileName(Vector3Int position)
    {
        if (interactableMap != null)
        {
            TileBase tile = interactableMap.GetTile(position);
            return tile != null ? tile.name : "";
        }
        return "";
    }
}