using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public class TileManager : MonoBehaviour
{
    public static TileManager Instance;

    // ====== Maps ======
    [Header("Grid & Tilemaps")]
    public Grid grid;
    public Tilemap interactableMap;        // nền ruộng (plowed / watered / empty)

    [Tooltip("Không còn dùng vẽ cây lên Tilemap; để trống hoặc bỏ qua.")]
    public Tilemap cropMap;                // không dùng, giữ lại cho tương thích

    [Header("Crops (Prefab)")]
    [Tooltip("Parent chứa các GameObject cây. Nếu để trống sẽ tự tạo.")]
    public Transform cropsParent;

    // ====== Binding ======
    [Header("Binding")]
    [SerializeField] private string interactableMapName = "InteractableMap";

    // ====== Farm rect ======
    [Header("Farm Rect (CELL coords)")]
    public Vector3Int originCell;          // origin = GÓC DƯỚI-TRÁI
    public Vector2Int size;                // size.x = số cột, size.y = số hàng

    // ====== Tile Assets (ground) ======
    [Header("Tile Assets (ground)")]
    public Tile visibleInteractableTile;
    public Tile hiddenInteractableTile;
    public Tile plowedTile;
    public Tile wateredTile;
    public Tile interactableTile;

    [Header("Crop visuals (fallback)")]
    [Tooltip("Chỉ dùng nếu cần vẽ tạm; với prefab cây thì hiếm khi cần.")]
    public Tile defaultSproutTile;

    // ====== STATE ======
    public enum TileStatus { Hidden = 0, Plowed = 1, Watered = 2, Planted = 3, Harvestable = 4 }

    [Serializable]
    public class TileSave
    {
        public int id;
        public TileStatus status;
        public string cropId;           // seedId từ server
        public long plantedAtUnixUtc;
        public int growthStage;         // stage hiện tại
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
    private readonly HashSet<int> _pending = new();

    // Quản lý cây đã spawn trên từng tileId
    private readonly Dictionary<int, Crop> _cropsById = new();

    private bool _ready = false;
    private string _pendingUserToLoad = null;

    // ===== DEBUG =====
    [SerializeField] private bool debugLogMapping = true;
    [SerializeField] private bool debugForceShowOutside = false;

    // ================= LIFECYCLE =================
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);

        if (!grid) grid = FindFirstObjectByType<Grid>();
        if (!interactableMap) interactableMap = FindFirstObjectByType<Tilemap>();
        if (!interactableMap) Debug.LogWarning("[TileManager] interactableMap chưa được gán! Sẽ rebind khi scene load.");

        if (!grid && interactableMap) grid = interactableMap.layoutGrid;

        if (!cropsParent)
        {
            var go = new GameObject("Crops");
            cropsParent = go.transform;
            if (grid) cropsParent.SetParent(grid.transform);
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (RebindTilemapIfNeeded())
        {
            if (!grid && interactableMap) grid = interactableMap.layoutGrid;
            interactableMap.CompressBounds();
            interactableMap.RefreshAllTiles();
            Debug.Log($"[TileManager] SceneLoaded '{scene.name}' → rebind OK. map={interactableMap.name}");
        }
    }

    private void Start()
    {
        if (!RebindTilemapIfNeeded()) return;
        if (!grid && interactableMap) grid = interactableMap.layoutGrid;

        interactableMap.CompressBounds();
        var bounds = interactableMap.cellBounds;

        if (size.x <= 0 || size.y <= 0)
        {
            originCell = bounds.min;
            size = new Vector2Int(bounds.size.x, bounds.size.y);
            Debug.Log($"[TileManager] Auto farm rect: origin={originCell}, size={size}");
        }
        if (size.x <= 0 || size.y <= 0)
            Debug.LogError("[TileManager] SIZE <=0. Điền Origin & Size ở Inspector.");

        // Khởi tạo _state theo tile nền hiện có
        foreach (var cell in bounds.allPositionsWithin)
        {
            var tile = interactableMap.GetTile(cell);
            if (!tile) continue;

            bool isVisibleMarker =
                (visibleInteractableTile && tile == visibleInteractableTile) ||
                (!visibleInteractableTile && tile.name == "Interactable_Visible");

            if (isVisibleMarker && hiddenInteractableTile)
            { interactableMap.SetTile(cell, hiddenInteractableTile); tile = hiddenInteractableTile; }

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
                    { id = id, status = status, cropId = "", plantedAtUnixUtc = 0, growthStage = 0, watered = (tile == wateredTile) };
                }
            }
        }

        interactableMap.RefreshAllTiles();
        _ready = true;

        if (!string.IsNullOrEmpty(_pendingUserToLoad))
        { var uid = _pendingUserToLoad; _pendingUserToLoad = null; LoadFarm(uid); }
    }

    // === Rebind helper ===
    private bool RebindTilemapIfNeeded()
    {
        if (interactableMap && interactableMap.gameObject && interactableMap.gameObject.scene.IsValid()) return true;

        var tms = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        foreach (var tm in tms)
        {
            if (tm && tm.gameObject.name == interactableMapName)
            { interactableMap = tm; Debug.Log($"[TileManager] Rebind tilemap → {interactableMapName}"); return true; }
        }
        if (!interactableMap && tms.Length > 0)
        { interactableMap = tms[0]; Debug.LogWarning($"[TileManager] Rebind fallback → {interactableMap.gameObject.name}"); return true; }

        Debug.LogError("[TileManager] Không tìm thấy Tilemap để rebind.");
        return false;
    }

    // ================== ID / CELL HELPER ==================
    public bool TryWorldToCell(Vector3 world, out Vector3Int cell)
    {
        cell = grid ? grid.WorldToCell(world) : Vector3Int.zero;
        return IsInsideFarm(cell);
    }
    // origin = GÓC DƯỚI-TRÁI
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
        if (size.x <= 0)
        { Debug.LogError($"[IdToCell] size.x<=0 → không map được id={id}."); return originCell; }
        int lx = id % size.x;
        int ly = id / size.x;
        return new Vector3Int(originCell.x + lx, originCell.y + ly, 0);
    }

    // ================== STATE (PUBLIC GETTERS) ==================
    public TileSave GetStateByCell(Vector3Int cell)
    { if (!IsInsideFarm(cell)) return null; int id = CellToId(cell); _state.TryGetValue(id, out var s); return s; }
    public TileSave GetStateById(int id)
    { _state.TryGetValue(id, out var s); return s; }

    // ================== STATE (INTERNAL) ==================
    private TileSave GetOrCreate(int id)
    {
        if (!_state.TryGetValue(id, out var s))
        { s = new TileSave { id = id, status = TileStatus.Hidden, cropId = "", plantedAtUnixUtc = 0, growthStage = 0, watered = false }; _state[id] = s; }
        return s;
    }
    private void SetGroundTile(Vector3Int cell, Tile tile)
    {
        if (!RebindTilemapIfNeeded() || !tile) return;
        interactableMap.SetTile(cell, tile);
    }

    // ================== CROPS (Prefab) ==================
    private void DestroyCropAt(int tileId)
    {
        if (_cropsById.TryGetValue(tileId, out var c) && c)
            Destroy(c.gameObject);
        _cropsById.Remove(tileId);
    }

    private Crop SpawnCrop(string seedId, Vector3Int cell, int stage, bool watered)
    {
        var im = GameManager.instance ? GameManager.instance.itemManager : null;
        var itemData = im?.GetItemDataByServerId(seedId) ?? im?.GetItemDataByName(seedId);
        if (itemData == null || itemData.cropPrefab == null)
        {
            Debug.LogWarning($"[TileManager] Không tìm thấy cropPrefab cho seed '{seedId}'.");
            return null;
        }

        var worldPos = grid.GetCellCenterWorld(cell);
        var go = Instantiate(itemData.cropPrefab, worldPos, Quaternion.identity, cropsParent);
        var crop = go.GetComponent<Crop>();
        if (!crop)
        {
            Debug.LogError("[TileManager] cropPrefab thiếu component Crop!");
            return null;
        }

        // đặt stage tức thì
        try
        {
            crop.SetStageInstant(stage);
        }
        catch
        {
            // fallback nếu phiên bản Crop cũ chưa có API
            var sr = go.GetComponent<SpriteRenderer>();
            var fStage = typeof(Crop).GetField("currentStage", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fStage != null) fStage.SetValue(crop, Mathf.Max(0, stage));
            if (crop.cropData && crop.cropData.growthStages != null && crop.cropData.growthStages.Length > 0 && sr)
            {
                int idx = Mathf.Clamp(stage, 0, crop.cropData.growthStages.Length - 1);
                sr.sprite = crop.cropData.growthStages[idx];
                sr.color = Color.white;
            }
        }

        // nếu đang watered từ server → nhìn thấy đang ướt
        if (watered)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr) sr.color = Color.gray;
        }

        return crop;
    }

    // ================== ACTION API ==================
    public void DoPlow(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoPlow] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoPlow] userId null/empty"); return; }

        int id = CellToId(cell);
        var s = GetStateById(id);
        if (s != null && (s.status == TileStatus.Plowed || s.status == TileStatus.Watered || s.status == TileStatus.Planted))
        { Debug.Log($"[DoPlow] tile {id} đã {s.status} → không gửi Plow"); return; }

        if (!_pending.Add(id)) { Debug.Log($"[DoPlow] tile {id} pending → skip."); return; }

        Debug.Log($"[DoPlow->REQ] userId={userId} tileId={id} statusBefore={(s != null ? s.status : TileStatus.Hidden)}");
        StartCoroutine(FarmlandApiClient.Plow(userId, id, env =>
        {
            try
            {
                Debug.Log($"[DoPlow->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0) { SetPlowed(cell); Debug.Log($"[DoPlow->APPLY] tileId={id} => Plowed"); }
                else { Debug.LogError("[DoPlow] " + (env?.message ?? "Response invalid")); }
            }
            finally { _pending.Remove(id); }
        }));
    }

    public void DoPlant(Vector3Int cell, string userId, string itemId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoPlant] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoPlant] userId null/empty"); return; }
        if (string.IsNullOrEmpty(itemId)) { Debug.LogError("[DoPlant] itemId null/empty"); return; }

        int id = CellToId(cell);
        var before = GetStateById(id)?.status ?? TileStatus.Hidden;

        if (!_pending.Add(id)) { Debug.Log($"[DoPlant] tile {id} pending → skip duplicate."); return; }

        Debug.Log($"[DoPlant->REQ] userId={userId} tileId={id} itemId={itemId} statusBefore={before}");

        StartCoroutine(FarmlandApiClient.Plant(userId, id, itemId, env =>
        {
            try
            {
                Debug.Log($"[DoPlant->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0)
                {
                    SetPlanted(cell, itemId, 0);
                    Debug.Log($"[DoPlant->APPLY] tileId={id} => Planted (seedId={itemId})");
                }
                else
                {
                    Debug.LogError("[DoPlant] " + (env?.message ?? "Response invalid"));
                }
            }
            finally { _pending.Remove(id); }
        }));
    }

    public void DoWater(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoWater] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoWater] userId null/empty"); return; }

        int id = CellToId(cell);

        // chặn theo trạng thái của cây (nếu có)
        if (_cropsById.TryGetValue(id, out var crop) && crop)
        {
            if (crop.IsMature()) { Debug.LogWarning("[DoWater] Cây đã trưởng thành."); return; }
            if (crop.IsWaitingForNextStage()) { Debug.LogWarning("[DoWater] Cây đang chờ qua stage, không thể tưới."); return; }
            if (crop.HasBeenWatered) { Debug.LogWarning("[DoWater] Vừa tưới xong, đợi thêm chút."); return; }
        }
        else
        {
            Debug.LogWarning("[DoWater] Không có cây ở ô này.");
            return;
        }

        var before = GetStateById(id)?.status ?? TileStatus.Hidden;
        if (!_pending.Add(id)) { Debug.Log($"[DoWater] tile {id} pending → skip."); return; }

        Debug.Log($"[DoWater->REQ] userId={userId} tileId={id} statusBefore={before}");
        StartCoroutine(FarmlandApiClient.Water(userId, id, env =>
        {
            try
            {
                Debug.Log($"[DoWater->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0)
                {
                    SetWatered(cell);     // nền ướt
                    crop.Water();         // cây chuyển màu xám + bắt đầu đợi stage
                    Debug.Log($"[DoWater->APPLY] tileId={id} => Watered + Crop.Water()");
                }
                else { Debug.LogError("[DoWater] " + (env?.message ?? "Response invalid")); }
            }
            finally { _pending.Remove(id); }
        }));
    }

    public void DoHarvest(Vector3Int cell, string userId)
    {
        if (!IsInsideFarm(cell)) { Debug.LogWarning("[DoHarvest] Cell ngoài vùng farm"); return; }
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[DoHarvest] userId null/empty"); return; }

        int id = CellToId(cell);
        var before = GetStateById(id)?.status ?? TileStatus.Hidden;
        if (!_pending.Add(id)) { Debug.Log($"[DoHarvest] tile {id} pending → skip."); return; }

        Debug.Log($"[DoHarvest->REQ] userId={userId} tileId={id} statusBefore={before}");
        StartCoroutine(FarmlandApiClient.Harvest(userId, id, env =>
        {
            try
            {
                Debug.Log($"[DoHarvest->RESP] tileId={id} error={env?.error} msg='{env?.message}'");
                if (env != null && env.error == 0)
                {
                    // Nếu đang có cây và đã chín -> để Crop tự spawn item
                    if (_cropsById.TryGetValue(id, out var crop) && crop)
                    {
                        // Crop.Harvest() sẽ: spawn harvestPrefab, ResetTile(), Destroy(gameObject)
                        crop.Harvest();
                        _cropsById.Remove(id); // đã tự Destroy trong Harvest()
                    }
                    else
                    {
                        // fallback: không thấy crop => chỉ reset đất
                        SetHarvested(cell);
                    }

                    Debug.Log($"[DoHarvest->APPLY] tileId={id} => Harvested (spawn drop nếu có)");
                }
                else
                {
                    Debug.LogError("[DoHarvest] " + (env?.message ?? "Response invalid"));
                }
            }
            finally { _pending.Remove(id); }
        }));
    }

    // ================== LOAD FARM (từ BE) ==================
    public void LoadFarm(string userId)
    {
        Debug.Log("[LoadFarm] called with userId=" + userId);
        if (string.IsNullOrEmpty(userId)) { Debug.LogError("[LoadFarm] userId null"); return; }
        if (!RebindTilemapIfNeeded()) { Debug.LogError("[LoadFarm] Tilemap chưa sẵn sàng."); return; }
        if (!_ready) { _pendingUserToLoad = userId; Debug.Log("[LoadFarm] not ready → queue"); return; }

        StartCoroutine(FarmlandApiClient.GetFarmlands(userId, env =>
        {
            if (env != null && env.error == 0 && env.data != null)
            {
                if (!RebindTilemapIfNeeded()) { Debug.LogError("[LoadFarm] Tilemap chưa sẵn sàng."); return; }

                Debug.Log($"[TileManager] LoadFarm OK: {env.data.Length} plots (origin={originCell}, size={size})");

                var ids = new List<int>();
                foreach (var p in env.data) ids.Add(p.tileId);
                int guessed = GuessBestWidth(ids);
                if (guessed > 0 && guessed != size.x)
                { size = new Vector2Int(guessed, Math.Max(size.y, 1)); }

                int maxLy = 0;
                foreach (var id in ids) maxLy = Math.Max(maxLy, (size.x > 0 ? id / size.x : 0));
                if (size.y <= maxLy) size = new Vector2Int(size.x, maxLy + 1);

                ReapplyPlots(env.data);
            }
            else
            { Debug.LogError("[TileManager] LoadFarm failed: " + (env?.message ?? "null response")); }
        }));
    }

    private int GuessBestWidth(List<int> ids)
    {
        int bestW = Math.Max(1, size.x), bestScore = -1;
        for (int w = 3; w <= 16; w++)
        {
            int score = 0;
            foreach (var id in ids)
            {
                int lx = id % w, ly = id / w;
                var cell = new Vector3Int(originCell.x + lx, originCell.y + ly, 0);
                if (IsInsideFarm(cell)) score++;
            }
            if (score > bestScore) { bestScore = score; bestW = w; }
        }
        return bestW;
    }
    private void ReapplyPlots(FarmlandPlotDto[] plots)
    {
        // clear crops cũ
        foreach (var kv in _cropsById) if (kv.Value) Destroy(kv.Value.gameObject);
        _cropsById.Clear();

        foreach (var p in plots) ApplyPlotFromServer(p);

        interactableMap?.RefreshAllTiles();
    }

    private static DateTime? ParseUtc(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        if (iso.StartsWith("0001")) return null; // default
        if (DateTime.TryParse(iso, null, DateTimeStyles.AdjustToUniversal, out var dt))
            return dt.ToUniversalTime();
        return null;
    }

    private void ApplyPlotFromServer(FarmlandPlotDto plot)
    {
        if (plot == null) return;
        if (!RebindTilemapIfNeeded()) return;

        int id = plot.tileId;
        var cell = IdToCell(id);
        bool inside = IsInsideFarm(cell);

        if (debugLogMapping)
            Debug.Log($"[ApplyPlot] id={id} → cell={cell} inside={inside} status={plot.status} watered={plot.watered} width={size.x}");

        if (!inside && !debugForceShowOutside)
        {
            Debug.LogWarning($"[ApplyPlot] BỎ QUA id={id} vì outside farm rect.");
            return;
        }

        var s = GetOrCreate(id);
        s.watered = plot.watered;

        string st = (plot.status ?? "Empty").Trim().ToLowerInvariant();

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
                if (_cropsById.TryGetValue(id, out var wc) && wc)
                {
                    var sr = wc.GetComponent<SpriteRenderer>();
                    if (sr) sr.color = Color.gray;
                }
                break;

            case "planted":
                {
                    // tìm crop active + stage + stageEndsAtUtc (nếu có)
                    string seedId = "";
                    int stage = 0;
                    FarmlandCropDto chosen = null;

                    if (plot.farmlandCrops != null && plot.farmlandCrops.Count > 0)
                    {
                        chosen = plot.farmlandCrops.FirstOrDefault(c => c != null && c.isActive)
                                 ?? plot.farmlandCrops[0];

                        seedId = !string.IsNullOrEmpty(chosen.item?.id) ? chosen.item.id : (chosen.seedId ?? "");
                        stage = Mathf.Max(0, chosen.stage);
                    }

                    s.status = TileStatus.Planted;
                    s.cropId = seedId ?? "";
                    s.growthStage = stage;
                    s.watered = plot.watered;

                    // nền
                    SetGroundTile(cell, s.watered ? wateredTile : plowedTile);

                    // cây
                    DestroyCropAt(id);
                    var crop = SpawnCrop(s.cropId, cell, s.growthStage, s.watered);
                    if (crop) _cropsById[id] = crop;

                    // nếu đang watered từ server → bật đếm chờ qua stage
                    if (s.watered && crop != null)
                    {
                        float? remainingSec = null;
                        var endsAt = ParseUtc(chosen?.stageEndsAtUtc);
                        if (endsAt.HasValue)
                        {
                            var now = DateTime.UtcNow;
                            remainingSec = (float)Math.Max(0, (endsAt.Value - now).TotalSeconds);
                        }
                        crop.StartWaitingFromServer(remainingSec);
                    }
                    break;
                }

            case "harvestable":
                s.status = TileStatus.Harvestable;
                SetGroundTile(cell, plowedTile); // tile riêng nếu có thì thay ở đây
                DestroyCropAt(id);
                break;

            default:
                ResetTile(cell);
                Debug.LogWarning($"[ApplyPlot] Status lạ: '{plot.status}' → reset.");
                break;
        }
    }

    // ================== SET TILE (Ground + Crop) ==================
    public void SetDry(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        if (s.status == TileStatus.Watered)
        { s.status = TileStatus.Plowed; s.watered = false; SetGroundTile(cell, plowedTile); }
    }
    public void SetPlowed(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Plowed; s.watered = false; s.cropId = ""; s.growthStage = 0;
        SetGroundTile(cell, plowedTile);
        DestroyCropAt(id);
    }
    public void SetWatered(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Watered; s.watered = true;
        SetGroundTile(cell, wateredTile);
    }
    public void SetPlanted(Vector3Int cell, string cropSeedId, long serverNowUnixUtc = 0, int stage = 0)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Planted;
        s.cropId = cropSeedId ?? "";
        s.plantedAtUnixUtc = serverNowUnixUtc;
        s.growthStage = Mathf.Max(0, stage);

        SetGroundTile(cell, s.watered ? wateredTile : plowedTile);

        DestroyCropAt(id);
        var crop = SpawnCrop(s.cropId, cell, s.growthStage, s.watered);
        if (crop) _cropsById[id] = crop;
    }
    public void SetHarvested(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;

        int id = CellToId(cell);
        var s = GetOrCreate(id);

        // Reset hoàn toàn về trạng thái chưa đào
        s.status = TileStatus.Hidden;
        s.cropId = "";
        s.growthStage = 0;
        s.watered = false;

        // Nền: về interactableTile (nếu có) hoặc clear
        if (interactableTile) SetGroundTile(cell, interactableTile);
        else interactableMap.SetTile(cell, null);

        // Xoá prefab cây (nếu còn)
        DestroyCropAt(id);

        // Đảm bảo vẽ lại ngay
        interactableMap.RefreshAllTiles();
    }
    public void ResetTile(Vector3Int cell)
    {
        if (!IsInsideFarm(cell)) return;
        int id = CellToId(cell);
        var s = GetOrCreate(id);
        s.status = TileStatus.Hidden; s.cropId = ""; s.growthStage = 0; s.watered = false;

        if (interactableTile) SetGroundTile(cell, interactableTile);
        else interactableMap.SetTile(cell, null);

        DestroyCropAt(id);
    }

    // ================== DEBUG CLICK & GIZMOS ==================
#if UNITY_EDITOR
    private void Update()
    {
        if (Input.GetMouseButtonDown(0) && Camera.main && grid)
        {
            var world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            var cell = grid.WorldToCell(world);
            bool inside = IsInsideFarm(cell);
            int id = inside ? CellToId(cell) : -1;
            Debug.Log($"[DEBUG] clickCell={cell} inside={inside} id={id} origin={originCell} size={size}");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!grid) return;
        Gizmos.color = Color.yellow;
        for (int y = 0; y < Mathf.Max(0, size.y); y++)
            for (int x = 0; x < Mathf.Max(0, size.x); x++)
            {
                var cell = new Vector3Int(originCell.x + x, originCell.y + y, 0);
                var center = grid.GetCellCenterWorld(cell);
                var next = grid.CellToWorld(cell + new Vector3Int(1, 1, 0)) - grid.CellToWorld(cell);
                Gizmos.DrawWireCube(center, new Vector3(next.x, next.y, 0));
            }
    }
#endif
}
