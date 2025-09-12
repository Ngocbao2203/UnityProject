using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using CGP.Gameplay.Items;            // dùng ItemData
using CGP.Networking.Clients;        // gọi API
using CGP.Networking.DTOs;
using CGP.Framework;
using CGP.Gameplay.Auth;

namespace CGP.Gameplay.Shop
{
    public class ShopPublishHelper : MonoBehaviour
    {
        public ShopManager shop;
        public int defaultPrice = 100;

        [ContextMenu("Publish Inspector Items To ShopPrice")]
        public void Publish() => StartCoroutine(CoPublish());

        private IEnumerator CoPublish()
        {
            if (shop == null || shop.itemList == null || shop.itemList.Count == 0)
            {
                Debug.LogWarning("[Publish] shop/itemList rỗng.");
                yield break;
            }

            foreach (var pd in shop.itemList)
            {
                if (!pd || !pd.itemData)
                {
                    Debug.LogWarning("[Publish] Bỏ qua entry thiếu ProductData/ItemData.");
                    continue;
                }

                // 1) Đảm bảo Item tồn tại trên server (giữ nguyên ItemType từ ItemData)
                var itemId = NormalizeGuid(pd.itemData.id);
                yield return EnsureItemOnServer(pd, id => itemId = id);
                if (string.IsNullOrEmpty(itemId))
                {
                    Debug.LogError($"[Publish] Không thể tạo/tìm Item cho {pd.productName}.");
                    continue;
                }

                // 2) Add vào ShopPrice
                var price = pd.price > 0 ? pd.price : defaultPrice;
                string json = $"{{\"ItemId\":\"{itemId}\",\"Price\":{price}}}";
                Debug.Log($"[Publish] → AddItemToShop JSON: {json}");

                using var req = new UnityWebRequest(ApiRoutes.ShopPrice.ADD_ITEM_TO_SHOP, UnityWebRequest.kHttpVerbPOST);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                SetAuth(req);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200)
                    Debug.Log($"[Publish] OK: {pd.productName} ({itemId}) price={price}");
                else
                    Debug.LogError($"[Publish] FAIL AddItemToShop: {pd.productName} ({itemId}) HTTP {req.responseCode} | {req.downloadHandler.text}");
            }
        }

        // ---------- Ensure Item exists (GET by Id → nếu không có thì CREATE) ----------
        private IEnumerator EnsureItemOnServer(ProductData pd, System.Action<string> onIdReady)
        {
            var id = NormalizeGuid(pd.itemData.id);

            // Có Id sẵn → kiểm tra tồn tại
            if (!string.IsNullOrEmpty(id))
            {
                var url = ApiRoutes.Item.GET_BY_ID.Replace("{id}", id);
                using var get = UnityWebRequest.Get(url);
                SetAuth(get);
                Debug.Log($"[Publish] → Check Item exists: {id}");
                yield return get.SendWebRequest();

                if (get.result == UnityWebRequest.Result.Success && get.responseCode == 200)
                {
                    Debug.Log($"[Publish] ✔ Item exists: {id}");
                    onIdReady?.Invoke(id);
                    yield break;
                }
                else
                {
                    Debug.LogWarning($"[Publish] ItemId {id} chưa có trên server ({get.responseCode}). Sẽ tạo mới.");
                }
            }
            else
            {
                // Unity asset chưa có id → sinh mới
                id = System.Guid.NewGuid().ToString();
                pd.itemData.id = id;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(pd.itemData);
#endif
                Debug.Log($"[Publish] ItemData chưa có Id → sinh mới: {id}");
            }

            // Tạo mới theo schema Swagger (PascalCase) với ItemType giữ nguyên
            var create = new CreateItemReq
            {
                Id = id,
                NameItem = string.IsNullOrEmpty(pd.productName) ? (pd.itemData ? pd.itemData.itemName : "Item") : pd.productName,
                Description = pd.itemData ? pd.itemData.description : pd.productName,
                ItemType = MapItemType(pd.itemData), // "Crop"/"Seed"/"Tool"/"Other"
                IsStackable = pd.itemData ? pd.itemData.isStackable : true
            };
            var json = JsonUtility.ToJson(create);
            Debug.Log($"[Publish] → CreateItem JSON: {json}");

            using var post = new UnityWebRequest(ApiRoutes.Item.CREATE, UnityWebRequest.kHttpVerbPOST);
            post.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            post.downloadHandler = new DownloadHandlerBuffer();
            post.SetRequestHeader("Content-Type", "application/json");
            SetAuth(post);

            yield return post.SendWebRequest();

            if (post.result == UnityWebRequest.Result.Success && post.responseCode == 200)
            {
                var resp = JsonUtility.FromJson<CreateItemResp>(post.downloadHandler.text);
                if (resp != null && resp.error == 0 && resp.data != null && !string.IsNullOrEmpty(resp.data.id))
                {
                    id = NormalizeGuid(resp.data.id);
                    pd.itemData.id = id; // lưu lại asset để lần sau dùng luôn
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(pd.itemData);
#endif
                    Debug.Log($"[Publish] ✔ Created Item: {pd.productName} → {id} (type={create.ItemType})");
                    onIdReady?.Invoke(id);
                    yield break;
                }
                else
                {
                    Debug.LogError($"[Publish] CreateItem returned error: {post.downloadHandler.text}");
                }
            }
            else
            {
                Debug.LogError($"[Publish] CreateItem HTTP {post.responseCode} | {post.downloadHandler.text}");
            }

            onIdReady?.Invoke(null);
        }

        // ---------- helpers ----------
        private static string NormalizeGuid(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Trim('{', '}'); // KHÔNG đổi lowercase

        private static string MapItemType(ItemData d)
        {
            // Trả về đúng text enum mà BE đang dùng
            if (!d) return "Other";
            return d.itemType.ToString(); // Seed / Tool / Crop / Other
        }

        private static void SetAuth(UnityWebRequest req)
        {
            var token = AuthManager.Instance?.ResolveToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", "Bearer " + token);
        }

        // ---------- DTOs cho JsonUtility ----------
        [System.Serializable]
        private class CreateItemReq
        {
            public string Id;
            public string NameItem;
            public string Description;
            public string ItemType;
            public bool IsStackable;
        }

        [System.Serializable]
        private class CreateItemResp
        { public int error; public string message; public ItemDto data; }

        [System.Serializable]
        private class ItemDto
        { public string id; public string nameItem; public string itemType; public bool isStackable; }
    }
}
