using UnityEngine;

public class PanelSfx : MonoBehaviour
{
    [Header("Output")]
    public AudioSource sfx;            // kéo AudioSource UI_SFX vào đây

    [Header("Clips")]
    public AudioClip openClip;         // tiếng mở panel
    public AudioClip closeClip;        // tiếng đóng panel

    [Header("Options")]
    public bool playOnEnable = true;   // phát open khi GameObject bật
    public bool playOnDisable = true;  // phát close khi GameObject tắt
    public Vector2 pitchRange = new(0.98f, 1.02f);

    void Reset()
    {
        // tự tìm AudioSource gần nhất nếu quên kéo
        sfx = GetComponent<AudioSource>() ?? GetComponentInParent<AudioSource>();
    }

    void OnEnable()
    {
        if (!playOnEnable) return;
        Play(openClip);
    }

    void OnDisable()
    {
        if (!playOnDisable) return;
        // Lưu ý: OnDisable gọi ngay trước khi tắt, nên vẫn phát được
        Play(closeClip);
    }

    public void PlayOpen() => Play(openClip);   // gọi thủ công nếu bạn show/hide bằng CanvasGroup
    public void PlayClose() => Play(closeClip);

    void Play(AudioClip clip)
    {
        if (sfx == null || clip == null) return;
        sfx.pitch = Random.Range(pitchRange.x, pitchRange.y);
        sfx.PlayOneShot(clip);
    }
}
