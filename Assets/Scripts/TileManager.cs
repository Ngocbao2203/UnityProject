using System;
using System.Collections.Generic;
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

    // ✅ Khóa chống bắn trùng request theo tileId
    private readonly HashSet<int> _pending = new();

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

    // ================== ACTION API (ENVELOPE MỚI + DEBUG + LOCK) ==================
    public void DoPlow(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoPlow] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoPlow] userId null/empty"); return; }

        int id = CellToId(cell);
        var s = GetStateById(id);

        // ⛔ Nếu BE đã báo ô này Plowed/Watered/Planted thì không gửi nữa
        if (s != null && (s.status == TileStatus.Plowed || s.status == TileStatus.Watered || s.status == TileStatus.Planted))
        {
            Debug.Log($"[DoPlow] tile {id} đã {s.status} → không gửi Plow");
            return;
        }

        if (!_pending.Add(id))
        {
            Debug.Log($"[DoPlow] tile {id} đang pending → bỏ qua duplicate.");
            return;
        }

        Debug.Log($"[DoPlow->REQ] userId={userId} tileId={id} statusBefore={(s != null ? s.status : TileStatus.Hidden)}");

        StartCoroutine(FarmlandApiClient.Plow(userId, id, env =>
        {
            try
            {
                Debug.Log($"[DoPlow->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0)
                {
                    SetPlowed(cell);
                    Debug.Log($"[DoPlow->APPLY] tileId={id} => Plowed");
                }
                else
                {
                    Debug.LogError("[DoPlow] " + (env?.message ?? "Response invalid"));
                }
            }
            finally { _pending.Remove(id); }
        }));
    }

    public void DoPlant(Vector3Int cell, string userId, string seedId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoPlant] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoPlant] userId null/empty"); return; }

        int id = CellToId(cell);
        var before = GetStateById(id)?.status ?? TileStatus.Hidden;

        if (!_pending.Add(id))
        {
            Debug.Log($"[DoPlant] tile {id} pending → skip duplicate.");
            return;
        }

        Debug.Log($"[DoPlant->REQ] userId={userId} tileId={id} seedId={seedId} statusBefore={before}");

        StartCoroutine(FarmlandApiClient.Plant(userId, id, seedId, env =>
        {
            try
            {
                Debug.Log($"[DoPlant->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0)
                {
                    SetPlanted(cell, seedId, 0);
                    Debug.Log($"[DoPlant->APPLY] tileId={id} => Planted (seed={seedId})");
                }
                else
                {
                    Debug.LogError("[DoPlant] " + (env?.message ?? "Response invalid"));
                }
            }
            finally
            {
                _pending.Remove(id);
            }
        }));
    }

    public void DoWater(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoWater] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoWater] userId null/empty"); return; }

        int id = CellToId(cell);
        var before = GetStateById(id)?.status ?? TileStatus.Hidden;

        if (!_pending.Add(id))
        {
            Debug.Log($"[DoWater] tile {id} pending → skip duplicate.");
            return;
        }

        Debug.Log($"[DoWater->REQ] userId={userId} tileId={id} statusBefore={before}");

        StartCoroutine(FarmlandApiClient.Water(userId, id, env =>
        {
            try
            {
                Debug.Log($"[DoWater->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0)
                {
                    SetWatered(cell);
                    Debug.Log($"[DoWater->APPLY] tileId={id} => Watered");
                }
                else
                {
                    Debug.LogError("[DoWater] " + (env?.message ?? "Response invalid"));
                }
            }
            finally
            {
                _pending.Remove(id);
            }
        }));
    }

    public void DoHarvest(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoHarvest] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoHarvest] userId null/empty"); return; }

        int id = CellToId(cell);
        var before = GetStateById(id)?.status ?? TileStatus.Hidden;

        if (!_pending.Add(id))
        {
            Debug.Log($"[DoHarvest] tile {id} pending → skip duplicate.");
            return;
        }

        Debug.Log($"[DoHarvest->REQ] userId={userId} tileId={id} statusBefore={before}");

        StartCoroutine(FarmlandApiClient.Harvest(userId, id, env =>
        {
            try
            {
                Debug.Log($"[DoHarvest->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0)
                {
                    SetHarvested(cell);
                    Debug.Log($"[DoHarvest->APPLY] tileId={id} => Plowed (after harvest)");
                }
                else
                {
                    Debug.LogError("[DoHarvest] " + (env?.message ?? "Response invalid"));
                }
            }
            finally
            {
                _pending.Remove(id);
            }
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
        if (!plowedTile) { Debug.LogError("[TileManager] plowedTile chưa gán!"); return; }
        SetTileSprite(cell, plowedTile);
    }

    public void SetWatered(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Watered;
        s.watered = true;
        if (!wateredTile) { Debug.LogError("[TileManager] wateredTile chưa gán!"); return; }
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
        if (interactableTile)
            SetTileSprite(cell, interactableTile);
        else
            interactableMap.SetTile(cell, null); // fallback: xoá tile
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
                var s = GetStateById(id);
                Debug.Log($"[Click] cell={cell} id={id} status={(s != null ? s.status : TileStatus.Hidden)} watered={(s != null && s.watered)} cropId={(s != null ? s.cropId : "")}");
            }
        }
    }

    public void LoadFarm(string userId)
    {
        StartCoroutine(FarmlandApiClient.GetFarmlands(userId, env =>
        {
            if (env != null && env.error == 0 && env.data != null)
            {
                Debug.Log($"[TileManager] LoadFarm OK: {env.data.Length} plots");
                foreach (var plot in env.data)
                    ApplyPlotFromServer(plot);

                interactableMap.RefreshAllTiles();
            }
            else
            {
                Debug.LogError("[TileManager] LoadFarm failed: " + (env?.message ?? "null response"));
            }
        }));
    }
    // Map dữ liệu plot từ server -> state & sprite trong game
    private void ApplyPlotFromServer(FarmlandPlotDto plot)
    {
        if (plot == null) return;

        int id = plot.tileId;
        var cell = IdToCell(id);

        if (!IsInsideFarm(cell))
        {
            Debug.LogWarning($"[ApplyPlotFromServer] tileId={id} nằm ngoài farm rect → bỏ qua.");
            return;
        }

        // Đảm bảo có state local
        var s = GetOrCreate(id);

        // Cập nhật cờ watered từ server
        s.watered = plot.watered;

        // Chuẩn hoá status để switch
        var st = (plot.status ?? "Empty").Trim().ToLowerInvariant();

        switch (st)
        {
            case "empty":
                ResetTile(cell);
                break;

            case "plowed":
                SetPlowed(cell);
                break;

            case "watered":
                SetWatered(cell);
                break;

            case "planted":
                // Chưa có cropId từ BE → giữ/đặt tạm rỗng.
                SetPlanted(cell, s.cropId ?? "", 0);
                break;

            case "harvestable":
                // Nếu chưa có sprite riêng, tạm hiển thị như plowed (tuỳ bạn thay bằng tile thu hoạch)
                s.status = TileStatus.Harvestable;
                SetTileSprite(cell, plowedTile);
                break;

            default:
                // Fallback an toàn
                ResetTile(cell);
                Debug.LogWarning($"[ApplyPlotFromServer] Status không nhận dạng: '{plot.status}' → reset tile.");
                break;
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
