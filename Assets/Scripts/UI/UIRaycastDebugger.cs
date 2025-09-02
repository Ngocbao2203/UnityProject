using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIRaycastDebugger : MonoBehaviour
{
    public GraphicRaycaster raycaster; // để trống cũng được, sẽ tự lấy từ Canvas chính
    EventSystem es;

    void Awake()
    {
        es = EventSystem.current;
        if (!es) Debug.LogError("[UIRaycast] MISSING EventSystem in scene!");
        if (!raycaster)
        {
            raycaster = Object.FindFirstObjectByType<GraphicRaycaster>();
            if (!raycaster) Debug.LogError("[UIRaycast] MISSING GraphicRaycaster on Canvas!");
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            var data = new PointerEventData(es) { position = Input.mousePosition };
            var results = new List<RaycastResult>();
            raycaster.Raycast(data, results);

            if (results.Count == 0)
            {
                Debug.Log("[UIRaycast] No UI hit.");
            }
            else
            {
                Debug.Log($"[UIRaycast] Top hits ({results.Count}):");
                for (int i = 0; i < results.Count; i++)
                {
                    var go = results[i].gameObject;
                    string cg = "";
                    var cgComp = go.GetComponentInParent<CanvasGroup>();
                    if (cgComp)
                        cg = $" | CanvasGroup(parent): interactable={cgComp.interactable}, blocksRaycasts={cgComp.blocksRaycasts}, alpha={cgComp.alpha}";
                    var img = go.GetComponent<Image>();
                    string imgInfo = img ? $" | Image.raycastTarget={img.raycastTarget}" : "";
                    Debug.Log($"  {i + 1}. {go.name}{imgInfo}{cg}");
                }
            }
        }
    }
}
