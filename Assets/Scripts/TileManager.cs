using UnityEngine;
using UnityEngine.Tilemaps;
public class TileManager : MonoBehaviour
{
    [SerializeField] private Tilemap interactableMap; // Reference to the Tilemap component

    [SerializeField] private Tile hiddenInteractableTile; // Array of TileBase objects to be used in the Tilemap

    [SerializeField] private Tile interactableTile; // Tile that represents interactable tiles
    void Start()
    {
        foreach (var position in interactableMap.cellBounds.allPositionsWithin)
        {
            //TileBase tile = interactableMap.GetTile(position); 

            //if(tile != null && tile.name == "Interactable_Visible")
            //{
            //    interactableMap.SetTile(position, hiddenInteractableTile);
            //}   
            interactableMap.SetTile(position, hiddenInteractableTile); // Set all tiles in the Tilemap to the hidden interactable tile
        }
    }
    public bool IsInteractable(Vector3Int position)
    {
        TileBase tile = interactableMap.GetTile(position);

        if (tile != null)
        {
            if (tile.name == "Interactable")
            {
                return true; // Return true if the tile is interactable
            }
        }
        return false; // Return false if the tile is not interactable
    }
    public void SetInteracted(Vector3Int position)
    {
        interactableMap.SetTile(position, interactableTile); // Set the tile at the specified position to the interactable tile
    }
}
