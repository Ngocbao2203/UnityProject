using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TileManager : MonoBehaviour
{
    public static TileManager Instance;

    [Header("Grid & Tilemap")]
    public Grid grid;
    public Tilemap interactableMap;

    [Header("Farm Rect (CELL coords)")]
    public Vector3Int originCell;
    public Vector2Int size;

    [Header("Tile Assets")]
    public Tile visibleInteractableTile;
    public Tile hiddenInteractableTile;
    public Tile plowedTile;
    public Tile wateredTile;
    public Tile interactableTile;

    // ====== TRẠNG THÁI ======
    public enum TileStatus { Hidden = 0, Plowed = 1, Watered = 2, Planted = 3, Harvestable = 4 }

    [Serializable]
    public class TileSave
    {
        public int id;
        public TileStatus status;
        public string cropId;
        public long plantedAtUnixUtc;
        public int growthStage;
        public bool watered;
    }

    [Serializable]
    public class FieldSave
    {
        public string userId;
        public List<TileSave> tiles = new();
        public long serverTimeUnixUtc;
    }

    private readonly Dictionary<int, TileSave> _state = new();

    // ================= LIFECYCLE =================
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);

        if (!grid) grid = FindFirstObjectByType<Grid>();
        if (!interactableMap) Debug.LogError("[TileManager] interactableMap chưa được gán!");
    }

    private void Start()
    {
        if (!interactableMap) return;

        interactableMap.CompressBounds();
        var bounds = interactableMap.cellBounds;

        if (size.x <= 0 || size.y <= 0)
        {
            originCell = bounds.min;
            size = new Vector2Int(bounds.size.x, bounds.size.y);
            Debug.Log($"[TileManager] Auto farm rect: origin={originCell}, size={size}");
        }

        foreach (var cell in bounds.allPositionsWithin)
        {
            var tile = interactableMap.GetTile(cell);
            if (!tile) continue;

            bool isVisibleMarker =
                (visibleInteractableTile && tile == visibleInteractableTile) ||
                (!visibleInteractableTile && tile.name == "Interactable_Visible");

            if (isVisibleMarker && hiddenInteractableTile)
            {
                interactableMap.SetTile(cell, hiddenInteractableTile);
                tile = hiddenInteractableTile;
            }

            if (!IsInsideFarm(cell)) continue;

            if (tile == hiddenInteractableTile || tile == plowedTile || tile == wateredTile || tile == interactableTile)
            {
                int id = CellToId(cell);
                if (!_state.ContainsKey(id))
                {
                    var status = TileStatus.Hidden;
                    if (tile == plowedTile) status = TileStatus.Plowed;
                    else if (tile == wateredTile) status = TileStatus.Watered;

                    _state[id] = new TileSave
                    {
                        id = id,
                        status = status,
                        cropId = "",
                        plantedAtUnixUtc = 0,
                        growthStage = 0,
                        watered = (tile == wateredTile)
                    };
                }
            }
        }

        interactableMap.RefreshAllTiles();
    }

    // ================== ID / CELL HELPER ==================
    public bool TryWorldToCell(Vector3 world, out Vector3Int cell)
    {
        cell = grid ? grid.WorldToCell(world) : Vector3Int.zero;
        return IsInsideFarm(cell);
    }

    public bool IsInsideFarm(Vector3Int cell)
    {
        return cell.x >= originCell.x && cell.x < originCell.x + size.x
            && cell.y >= originCell.y && cell.y < originCell.y + size.y;
    }

    public int CellToId(Vector3Int cell)
    {
        int lx = cell.x - originCell.x;
        int ly = cell.y - originCell.y;
        return lx + ly * size.x;
    }

    public Vector3Int IdToCell(int id)
    {
        int lx = id % size.x;
        int ly = id / size.x;
        return new Vector3Int(originCell.x + lx, originCell.y + ly, 0);
    }

    // ================== STATE (PUBLIC GETTERS) ==================
    public TileSave GetStateByCell(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return null;
        int id = CellToId(cell);
        _state.TryGetValue(id, out var s);
        return s;
    }

    public TileSave GetStateById(int id)
    {
        _state.TryGetValue(id, out var s);
        return s;
    }

    // ================== STATE (INTERNAL) ==================
    private TileSave GetOrCreate(int id)
    {
        if (!_state.TryGetValue(id, out var s))
        {
            s = new TileSave { id = id, status = TileStatus.Hidden, cropId = "", plantedAtUnixUtc = 0, growthStage = 0, watered = false };
            _state[id] = s;
        }
        return s;
    }

    private void SetTileSprite(Vector3Int cell, Tile tile)
    {
        if (!interactableMap || !tile) return;
        interactableMap.SetTile(cell, tile);
    }

    // ================== ACTION API (THEO ENVELOPE MỚI) ==================
    public void DoPlow(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) return;
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoPlow] userId null/empty"); return; }

        int id = CellToId(cell);
        StartCoroutine(FarmlandApiClient.Plow(userId, id, env =>
        {
            if (env != null && env.error == 0) SetPlowed(cell);
            else Debug.LogError("[DoPlow] " + (env?.message ?? "Response invalid"));
        }));
    }

    public void DoPlant(Vector3Int cell, string userId, string seedId)
    {
        if (!IsInsideFarm(cell)) return;
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoPlant] userId null/empty"); return; }

        int id = CellToId(cell);
        StartCoroutine(FarmlandApiClient.Plant(userId, id, seedId, env =>
        {
            if (env != null && env.error == 0) SetPlanted(cell, seedId, 0);
            else Debug.LogError("[DoPlant] " + (env?.message ?? "Response invalid"));
        }));
    }

    public void DoWater(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) return;
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoWater] userId null/empty"); return; }

        int id = CellToId(cell);
        StartCoroutine(FarmlandApiClient.Water(userId, id, env =>
        {
            if (env != null && env.error == 0) SetWatered(cell);
            else Debug.LogError("[DoWater] " + (env?.message ?? "Response invalid"));
        }));
    }

    public void DoHarvest(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) return;
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoHarvest] userId null/empty"); return; }

        int id = CellToId(cell);
        StartCoroutine(FarmlandApiClient.Harvest(userId, id, env =>
        {
            if (env != null && env.error == 0) SetHarvested(cell);
            else Debug.LogError("[DoHarvest] " + (env?.message ?? "Response invalid"));
        }));
    }

    // ================== COMPAT WRAPPERS (giữ để code cũ không lỗi) ==================
    public void SetDry(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        if (s.status == TileStatus.Watered)
        {
            s.status = TileStatus.Plowed;
            s.watered = false;
            SetTileSprite(cell, plowedTile);
        }
    }

    public void SetPlowed(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Plowed;
        s.watered = false;
        s.cropId = "";
        SetTileSprite(cell, plowedTile);
    }

    public void SetWatered(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Watered;
        s.watered = true;
        SetTileSprite(cell, wateredTile);
    }

    public void SetPlanted(Vector3Int cell, string cropId, long serverNowUnixUtc = 0)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Planted;
        s.cropId = cropId;
        s.plantedAtUnixUtc = serverNowUnixUtc;
        SetTileSprite(cell, plowedTile); // hoặc sprite riêng cho planted
    }

    public void SetHarvested(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Plowed;
        s.cropId = "";
        s.watered = false;
        SetTileSprite(cell, plowedTile);
    }

    public void ResetTile(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Hidden;
        s.cropId = "";
        s.watered = false;
        SetTileSprite(cell, interactableTile);
    }

    // ================== DEBUG CLICK ==================
    private void Update()
    {
        if (Input.GetMouseButtonDown(0) && Camera.main)
        {
            var world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            if (TryWorldToCell(world, out var cell) && interactableMap.HasTile(cell))
            {
                int id = CellToId(cell);
                Debug.Log($"[Click] cell={cell} id={id}");
            }
        }
    }

    // ================== GIZMOS ==================
    private void OnDrawGizmosSelected()
    {
        if (!grid) grid = FindFirstObjectByType<Grid>();
        if (!grid) return;

        Gizmos.color = Color.yellow;
        for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                var cell = new Vector3Int(originCell.x + x, originCell.y + y, 0);
                var center = grid.GetCellCenterWorld(cell);
                var next = grid.CellToWorld(cell + new Vector3Int(1, 1, 0)) - grid.CellToWorld(cell);
                Gizmos.DrawWireCube(center, new Vector3(next.x, next.y, 0));
            }
    }
}
