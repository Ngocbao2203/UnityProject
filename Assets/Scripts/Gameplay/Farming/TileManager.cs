using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

using CGP.Gameplay.Items;
using CGP.Networking.DTOs;
using CGP.Networking.Clients; // FarmlandApiClient

namespace CGP.Gameplay.Farming
{
    public class TileManager : MonoBehaviour
    {
        public static TileManager Instance;

        [Header("Grid & Tilemaps")]
        public Grid grid;
        public Tilemap interactableMap;

        [Tooltip("Tùy chọn. Có cũng được, không có cũng được. Dùng để vẽ/clear tile mầm.")]
        [SerializeField] private Tilemap cropMap; // optional

        [Header("Crops (Prefab)")]
        public Transform cropsParent;

        [Header("Binding")]
        [SerializeField] private string interactableMapName = "InteractableMap";
        [SerializeField] private string cropMapName = "CropMap";

        [Header("Farm Rect (CELL coords)")]
        public Vector3Int originCell;
        public Vector2Int size;

        [Header("Tile Assets (ground)")]
        public Tile visibleInteractableTile;
        public Tile hiddenInteractableTile;
        public Tile plowedTile;
        public Tile wateredTile;
        public Tile interactableTile;

        [Header("Crop visuals (fallback)")]
        public Tile defaultSproutTile;

        [Header("Options")]
        [Tooltip("Nếu có cropMap, mỗi lần đổi state sẽ clear tile mầm ở cropMap.")]
        public bool clearSproutTiles = true;

        // ====== STATE ======
        public enum TileState { Empty = 0, Plowed = 1, Watered = 2, Planted = 3, Harvestable = 4 }
        public enum TileStatus { Hidden = 0, Plowed = 1, Watered = 2, Planted = 3, Harvestable = 4 } // legacy enum

        public class TileInfo
        {
            public TileStatus status;
            public bool watered;
            public string cropId;
            public int growthStage;
            public int id;
        }

        [Serializable]
        public class TileSave
        {
            public int id;
            public TileState status;
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
        // KHÔNG static để không mang rác qua reload
        private readonly Dictionary<int, Crop> _cropsById = new();

        private bool _ready = false;
        private string _pendingUserToLoad = null;

        // Debounce request-server theo tile
        private readonly HashSet<int> _inFlight = new();
        private bool TryBeginOp(int tileId) { if (_inFlight.Contains(tileId)) return false; _inFlight.Add(tileId); return true; }
        private void EndOp(int tileId) => _inFlight.Remove(tileId);

        // Khóa chống spawn trùng trong 1 frame
        private readonly HashSet<int> _spawningNow = new();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }
            DontDestroyOnLoad(gameObject);

            if (!grid) grid = FindFirstObjectByType<Grid>();
            if (!interactableMap) interactableMap = FindFirstObjectByType<Tilemap>();
            if (!grid && interactableMap) grid = interactableMap.layoutGrid;

            if (!cropsParent)
            {
                var existed = GameObject.Find("Crops");
                if (existed) cropsParent = existed.transform;
                else
                {
                    var go = new GameObject("Crops");
                    cropsParent = go.transform;
                    if (grid) cropsParent.SetParent(grid.transform, true);
                    cropsParent.position = Vector3.zero;
                }
            }

            // Chỉ tự-find cropMap nếu đang bật clearSproutTiles
            if (clearSproutTiles && cropMap == null)
            {
                var tms = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
                foreach (var tm in tms)
                    if (tm && tm.gameObject.name == cropMapName) { cropMap = tm; break; }
            }

            var managers = FindObjectsByType<TileManager>(FindObjectsSortMode.None);
            if (managers.Length > 1)
                Debug.LogWarning($"[TileManager] Found {managers.Length} instances. Duplicates can cause double spawn!");

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
            }

            if (clearSproutTiles && cropMap == null)
            {
                var tms = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
                foreach (var tm in tms)
                    if (tm && tm.gameObject.name == cropMapName) { cropMap = tm; break; }
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
            }

            foreach (var cell in bounds.allPositionsWithin)
            {
                var tile = interactableMap.GetTile(cell);
                if (!tile) continue;
                if (!IsInsideFarm(cell)) continue;

                int id = CellToId(cell);
                if (!_state.ContainsKey(id))
                {
                    var state = TileState.Empty;
                    if (tile == plowedTile) state = TileState.Plowed;
                    else if (tile == wateredTile) state = TileState.Watered;

                    _state[id] = new TileSave
                    {
                        id = id,
                        status = state,
                        cropId = "",
                        plantedAtUnixUtc = 0,
                        growthStage = 0,
                        watered = (tile == wateredTile)
                    };
                }
            }

            _ready = true;
            if (!string.IsNullOrEmpty(_pendingUserToLoad))
            {
                var uid = _pendingUserToLoad;
                _pendingUserToLoad = null;
                LoadFarm(uid);
            }

            // Sau khi đã loại bỏ cơ chế “cây ma” từ Player, không cần quét hợp nhất nữa.
        }

        private bool RebindTilemapIfNeeded()
        {
            if (interactableMap && interactableMap.gameObject && interactableMap.gameObject.scene.IsValid()) return true;

            var tms = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
            foreach (var tm in tms)
                if (tm && tm.gameObject.name == interactableMapName) { interactableMap = tm; return true; }

            return false;
        }

        // ================== LOAD / RELOAD FARM ==================
        public void LoadFarm(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;
            if (!_ready) { _pendingUserToLoad = userId; return; }

            StartCoroutine(FarmlandApiClient.GetFarmlands(userId, env =>
            {
                if (env != null && env.error == 0 && env.data != null)
                {
                    ReapplyPlots(env.data);
                }
            }));
        }

        private void ReloadFarm(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;
            StartCoroutine(FarmlandApiClient.GetFarmlands(userId, env =>
            {
                if (env != null && env.error == 0 && env.data != null)
                {
                    ReapplyPlots(env.data);
                }
            }));
        }

        private void ReapplyPlots(FarmlandPlotDto[] plots)
        {
            // Xóa crop hiện có
            foreach (var kv in _cropsById) if (kv.Value) Destroy(kv.Value.gameObject);
            _cropsById.Clear();
            _spawningNow.Clear();

            // GỘP: cùng tileId -> lấy newest
            var latestByTile = new Dictionary<int, FarmlandPlotDto>();
            foreach (var p in plots)
            {
                if (p == null) continue;
                latestByTile[p.tileId] = p; // lần sau cùng ghi đè
            }

            // Đổ nền ẩn bằng SetTilesBlock
            FillHidden();

            // Áp lại trạng thái từ server
            foreach (var p in latestByTile.Values) ApplyPlotFromServer(p);

            interactableMap?.RefreshAllTiles();
        }

        private void FillHidden()
        {
            var bounds = new BoundsInt(originCell.x, originCell.y, 0, size.x, size.y, 1);
            int count = size.x * size.y;

            var ground = new TileBase[count];
            for (int i = 0; i < count; i++) ground[i] = hiddenInteractableTile;
            interactableMap.SetTilesBlock(bounds, ground);

            if (clearSproutTiles && cropMap != null)
            {
                var clears = new TileBase[count]; // all null
                cropMap.SetTilesBlock(bounds, clears);
            }
        }

        private void ApplyPlotFromServer(FarmlandPlotDto plot)
        {
            if (plot == null) return;
            int id = plot.tileId;
            var cell = IdToCell(id);

            var s = GetOrCreate(id);
            s.watered = plot.watered;

            string st = (plot.status ?? "Empty").Trim().ToLowerInvariant();
#if UNITY_EDITOR
            int cropsCount = plot.farmlandCrops != null ? plot.farmlandCrops.Count : 0;
            Debug.Log($"[ApplyPlotFromServer] tileId={id} cell={cell} status='{plot.status}' watered={plot.watered} cropsCount={cropsCount}");
#endif
            switch (st)
            {
                case "empty":
                    ResetTile(cell);
                    break;

                case "plowed":
                    SetPlowed(cell);
#if UNITY_EDITOR
                    Debug.Log("[ApplyPlotFromServer] -> PLOWED  (SetPlowed)  id=" + id);
#endif
                    break;

                case "watered":
                    SetWatered(cell);
                    break;

                case "planted":
                    {
                        string seedId = "";
                        int stage = 0;
                        var crops = plot.farmlandCrops;
                        FarmlandCropDto chosen = null;
                        if (crops != null)
                        {
                            for (int i = 0; i < crops.Count; i++)
                            {
                                var c = crops[i];
                                if (c == null) continue;
                                if (c.isActive) { chosen = c; break; }
                                if (chosen == null) chosen = c;
                            }
                        }

                        if (chosen != null)
                        {
                            seedId = !string.IsNullOrEmpty(chosen.item?.id) ? chosen.item.id : (chosen.seedId ?? "");
                            stage = Mathf.Max(0, chosen.stage);
                        }

                        s.status = TileState.Planted;
                        s.cropId = seedId ?? "";
                        s.growthStage = stage;
                        s.watered = plot.watered;

                        SetGroundTile(cell, s.watered ? wateredTile : plowedTile);
                        DestroyCropAt(id);
                        var crop = SpawnCrop(s.cropId, cell, s.growthStage, s.watered);
                        if (crop) _cropsById[id] = crop;
                        else Debug.LogWarning($"[ApplyPlotFromServer] SpawnCrop FAILED at tile={id}, seedId='{s.cropId}', stage={s.growthStage}");
                        break;
                    }

                case "harvestable":
                    s.status = TileState.Harvestable;
                    SetGroundTile(cell, plowedTile);
                    DestroyCropAt(id);
                    break;

                default:
                    ResetTile(cell);
                    break;
            }
        }

        // ================== Helpers ==================
        private TileSave GetOrCreate(int id)
        {
            if (!_state.TryGetValue(id, out var s))
            {
                s = new TileSave { id = id, status = TileState.Empty, cropId = "", plantedAtUnixUtc = 0, growthStage = 0, watered = false };
                _state[id] = s;
            }
            return s;
        }

        private void DestroyCropAt(int tileId)
        {
            if (_cropsById.TryGetValue(tileId, out var c) && c)
                Destroy(c.gameObject);
            _cropsById.Remove(tileId);
        }

        private void SafeSetCropMap(Vector3Int cell, TileBase tile)
        {
            if (!clearSproutTiles || cropMap == null) return;
            try { cropMap.SetTile(cell, tile); } catch { }
        }

#if UNITY_EDITOR
        private void DebugTile(Vector3Int cell, string where)
        {
            var t = interactableMap ? interactableMap.GetTile<Tile>(cell) : null;
            var tName = t ? t.name : "null";
            var spr = (t && t.sprite) ? t.sprite.name : "null";
            Debug.Log($"[TileDbg] {where} cell={cell} tile={tName} sprite={spr}");
        }
#endif

        private void SetGroundTile(Vector3Int cell, Tile tile)
        {
#if UNITY_EDITOR
            var before = interactableMap ? interactableMap.GetTile<Tile>(cell) : null;
#endif
            interactableMap.SetTile(cell, tile);
            SafeSetCropMap(cell, null);
#if UNITY_EDITOR
            Debug.Log($"[TileDbg] SetGroundTile {cell}  {before?.name ?? "null"} -> {tile?.name ?? "null"}");
            DebugTile(cell, "After SetGroundTile");
#endif
        }

        public void SetPlowed(Vector3Int cell)
        {
            int id = CellToId(cell);
            var s = GetOrCreate(id);
            s.status = TileState.Plowed; s.watered = false; s.cropId = ""; s.growthStage = 0;
            SetGroundTile(cell, plowedTile);
            DestroyCropAt(id);
        }

        public void SetWatered(Vector3Int cell)
        {
            int id = CellToId(cell);
            var s = GetOrCreate(id);

            s.watered = true;

            if (s.status == TileState.Planted)
                SetGroundTile(cell, wateredTile);
            else
            {
                s.status = TileState.Watered;
                SetGroundTile(cell, wateredTile);
            }
        }

        public void ResetTile(Vector3Int cell)
        {
            int id = CellToId(cell);
            var s = GetOrCreate(id);
            s.status = TileState.Empty;
            s.cropId = "";
            s.growthStage = 0;
            s.watered = false;

            interactableMap.SetTile(cell, hiddenInteractableTile);
            SafeSetCropMap(cell, null);

            DestroyCropAt(id);
        }

        public bool SetDry(Vector3Int cell)
        {
            if (!IsInsideFarm(cell)) return false;

            int id = CellToId(cell);
            var s = GetOrCreate(id);

            if (!s.watered && s.status != TileState.Watered) return true;

            s.watered = false;
            if (s.status == TileState.Watered) s.status = TileState.Plowed;

            SetGroundTile(cell, s.status == TileState.Empty ? hiddenInteractableTile : plowedTile);
            return true;
        }

        public Vector3Int IdToCell(int id)
        {
            int lx = id % size.x;
            int ly = id / size.x;
            return new Vector3Int(originCell.x + lx, originCell.y + ly, 0);
        }
        public int CellToId(Vector3Int cell)
        {
            int lx = cell.x - originCell.x;
            int ly = cell.y - originCell.y;
            return lx + ly * size.x;
        }
        public bool IsInsideFarm(Vector3Int cell)
        {
            return cell.x >= originCell.x && cell.x < originCell.x + size.x
                 && cell.y >= originCell.y && cell.y < originCell.y + size.y;
        }

        private Crop SpawnCrop(string seedId, Vector3Int cell, int stage, bool watered)
        {
            var im = GameManager.instance ? GameManager.instance.itemManager : null;
            var itemData = im?.GetItemDataByServerId(seedId) ?? im?.GetItemDataByName(seedId);
            if (itemData == null || itemData.cropPrefab == null) return null;

            int id = CellToId(cell);
            Vector3 worldCenter = grid.GetCellCenterWorld(cell);
            worldCenter.z = -0.1f;

            // Khóa chống spawn đồng thời
            if (_spawningNow.Contains(id))
                return _cropsById.TryGetValue(id, out var waitExisted) ? waitExisted : null;
            _spawningNow.Add(id);

            // Đã có -> cập nhật
            if (_cropsById.TryGetValue(id, out var existed) && existed != null)
            {
                existed.seedId = seedId;
                existed.tileId = id;
                existed.transform.position = worldCenter;
                existed.SetStageInstant(stage);
                if (watered) existed.StartWaitingFromServer();
                _spawningNow.Remove(id);
                return existed;
            }

            // Chưa có -> tạo mới
            var go = Instantiate(itemData.cropPrefab, worldCenter, Quaternion.identity);
            if (cropsParent) go.transform.SetParent(cropsParent, true);
#if UNITY_EDITOR
            go.name = $"Crop_{seedId}_tile{id}";
#endif
            var crop = go.GetComponent<Crop>();
            if (crop)
            {
                crop.seedId = seedId;
                crop.tileId = id;
                crop.SetStageInstant(stage);
                if (watered) crop.StartWaitingFromServer();
            }

            _cropsById[id] = crop;
            _spawningNow.Remove(id);
            return crop;
        }

        // ===== Legacy compatibility API =====
        private static TileStatus ToLegacy(TileState st)
        {
            return st switch
            {
                TileState.Plowed => TileStatus.Plowed,
                TileState.Watered => TileStatus.Watered,
                TileState.Planted => TileStatus.Planted,
                TileState.Harvestable => TileStatus.Harvestable,
                _ => TileStatus.Hidden,
            };
        }

        public TileInfo GetStateByCell(Vector3Int cell)
        {
            if (!IsInsideFarm(cell))
                return new TileInfo { status = TileStatus.Hidden, watered = false, cropId = "", growthStage = 0, id = -1 };

            var s = GetOrCreate(CellToId(cell));
            return new TileInfo
            {
                status = ToLegacy(s.status),
                watered = s.watered,
                cropId = s.cropId,
                growthStage = s.growthStage,
                id = s.id
            };
        }

        public TileStatus GetStateByCellStatus(Vector3Int cell) => GetStateByCell(cell)?.status ?? TileStatus.Hidden;

        // ================= LOCAL-ONLY (offline/editor) =================
        public bool DoPlow(Vector3Int cell)
        {
            if (!IsInsideFarm(cell)) return false;
            SetPlowed(cell);
            return true;
        }
        public bool DoWater(Vector3Int cell)
        {
            if (!IsInsideFarm(cell)) return false;
            SetWatered(cell);
            return true;
        }
        public bool DoPlant(Vector3Int cell) => DoPlant(cell, null);

        public bool DoPlant(Vector3Int cell, string seedId)
        {
            if (!IsInsideFarm(cell)) return false;
            int id = CellToId(cell);
            var s = GetOrCreate(id);

            s.status = TileState.Planted;
            s.cropId = string.IsNullOrEmpty(seedId) ? (s.cropId ?? "") : seedId;
            s.growthStage = 0;

            DestroyCropAt(id);

            if (string.IsNullOrEmpty(s.cropId))
            {
                SetGroundTile(cell, plowedTile);
                return true;
            }

            var crop = SpawnCrop(s.cropId, cell, 0, s.watered);
            if (crop) _cropsById[id] = crop;
            SetGroundTile(cell, s.watered ? wateredTile : plowedTile);
            return true;
        }

        public bool DoHarvest(Vector3Int cell)
        {
            if (!IsInsideFarm(cell)) return false;

            int tileId = CellToId(cell);
            if (!_cropsById.TryGetValue(tileId, out var crop) || crop == null)
            {
                Debug.Log("[Harvest] Không có crop ở ô này.");
                return false;
            }
            if (!crop.IsMature())
            {
                Debug.Log("[Harvest] Cây chưa chín -> không thu hoạch.");
                return false;
            }

            crop.Harvest();
            return true;
        }

        // ================= SERVER-PERSIST (ghi DB) =================
        public void DoPlow(Vector3Int cell, string userId)
        {
            if (!IsInsideFarm(cell) || string.IsNullOrEmpty(userId)) return;
            int tileId = CellToId(cell);

            var s = GetOrCreate(tileId);
            if (s.status != TileState.Empty)
            {
                Debug.Log($"[Plow] Skip local={s.status}");
                return;
            }
            if (!TryBeginOp(tileId)) return;

            StartCoroutine(FarmlandApiClient.Plow(userId, tileId, env =>
            {
                EndOp(tileId);
                if (env != null && env.error == 0)
                {
                    // Đất khô sau khi cuốc
                    SetPlowed(cell);
                }
                else
                {
                    Debug.LogWarning($"[Plow] Rejected tile={tileId} msg={env?.message}");
                    ReloadFarm(userId);
                }
            }));
        }

        public void DoWater(Vector3Int cell, string userId)
        {
            if (!IsInsideFarm(cell) || string.IsNullOrEmpty(userId)) return;
            int tileId = CellToId(cell);

            StartCoroutine(FarmlandApiClient.Water(userId, tileId, env =>
            {
                if (env != null && env.error == 0)
                {
                    SetWatered(cell);
                    if (_cropsById.TryGetValue(tileId, out var crop) && crop)
                        crop.Water();
                }
                else
                {
                    Debug.LogWarning($"[Water] Server rejected tile={tileId} msg={env?.message}");
                }
            }));
        }

        public void DoPlant(Vector3Int cell, string userId, string seedId)
        {
            if (!IsInsideFarm(cell) || string.IsNullOrEmpty(userId)) return;
            int tileId = CellToId(cell);
            seedId ??= "";

            var s = GetOrCreate(tileId);
            if (s.status != TileState.Plowed && s.status != TileState.Watered)
            {
                Debug.Log($"[Plant] Skip local={s.status}");
                return;
            }
            if (!string.IsNullOrEmpty(s.cropId) || s.status == TileState.Planted)
            {
                Debug.Log("[Plant] Skip already planted");
                return;
            }
            if (!TryBeginOp(tileId)) return;

            StartCoroutine(FarmlandApiClient.Plant(userId, tileId, seedId, env =>
            {
                EndOp(tileId);
                if (env != null && env.error == 0)
                {
                    s = GetOrCreate(tileId);
                    s.status = TileState.Planted;
                    s.cropId = seedId;
                    s.growthStage = 0;

                    DestroyCropAt(tileId);
                    var crop = SpawnCrop(seedId, cell, 0, s.watered);
                    if (crop) _cropsById[tileId] = crop;

                    SetGroundTile(cell, s.watered ? wateredTile : plowedTile);
                }
                else
                {
                    Debug.LogWarning($"[Plant] Rejected tile={tileId} seed={seedId} msg={env?.message}");
                    ReloadFarm(userId);
                }
            }));
        }

        public void DoHarvest(Vector3Int cell, string userId)
        {
            if (!IsInsideFarm(cell) || string.IsNullOrEmpty(userId)) return;

            int tileId = CellToId(cell);
            var s = GetOrCreate(tileId);

            if (!_cropsById.TryGetValue(tileId, out var crop) || crop == null)
            {
                Debug.Log("[Harvest] Không có crop ở ô này.");
                return;
            }
            if (!crop.IsMature() && s.status != TileState.Harvestable)
            {
                Debug.Log("[Harvest] Cây chưa chín -> không gửi request.");
                return;
            }

            if (!TryBeginOp(tileId)) return;

            StartCoroutine(FarmlandApiClient.Harvest(userId, tileId, env =>
            {
                EndOp(tileId);
                if (env != null && env.error == 0)
                {
                    crop.Harvest();
                }
                else
                {
                    Debug.LogWarning($"[Harvest] Rejected tile={tileId} msg={env?.message}");
                    ReloadFarm(userId);
                }
            }));
        }
    }
}
