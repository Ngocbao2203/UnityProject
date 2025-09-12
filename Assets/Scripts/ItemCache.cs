using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CGP.Networking.DTOs;       // để nhận ItemDto
using CGP.Networking.Clients;   // để gọi ItemApiClient

public class ItemCache : MonoBehaviour
{
    public static ItemCache Instance { get; private set; }
    private readonly Dictionary<string, ItemDto> map = new();
    public bool IsReady { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public IEnumerator EnsureLoaded()
    {
        if (IsReady) yield break;

        ApiListEnvelope<ItemDto> res = null;
        string err = null;

        // gọi API
        yield return ItemApiClient.GetAllItems(r => res = r, e => err = e);

        map.Clear();
        if (res?.data != null)
        {
            foreach (var it in res.data)
                if (!string.IsNullOrEmpty(it.id))
                    map[it.id] = it;
        }
        IsReady = true;

        if (res == null) Debug.LogWarning("[ItemCache] Load failed: " + err);
    }

    public bool TryGet(string id, out ItemDto item) => map.TryGetValue(id, out item);
}
