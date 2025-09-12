using UnityEngine;
using UnityEngine.UI;

public class BackgroundScroll : MonoBehaviour
{
    public float scrollSpeed = 0.05f;
    private RawImage image;

    void Start()
    {
        image = GetComponent<RawImage>();
    }

    void Update()
    {
        if (image)
        {
            var uv = image.uvRect;
            uv.x += scrollSpeed * Time.deltaTime;
            image.uvRect = uv;
        }
    }
}
