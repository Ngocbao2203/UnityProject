using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(Rigidbody2D))]
public class Item : MonoBehaviour
{
    [SerializeField]
    private ItemData data; // Sử dụng SerializeField để chỉnh sửa trong Editor
    [HideInInspector]
    public Rigidbody2D rb2d;

    public ItemData Data => data; // Getter để truy cập an toàn

    private void Awake()
    {
        rb2d = GetComponent<Rigidbody2D>();
    }
}