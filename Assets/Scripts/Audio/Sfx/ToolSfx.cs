using UnityEngine;

public class ToolSfx : MonoBehaviour
{
    [Header("Audio Output")]
    public AudioSource sfx;

    [Header("Hoe Clips")]
    public AudioClip[] hoeSwingClips;
    public AudioClip[] hoeHitClips;

    [Header("Water Clips")]
    public AudioClip[] waterOneShotClips;
    public AudioClip waterLoopClip;

    [Header("Options")]
    [Tooltip("Random pitch để tránh lặp nhàm chán")]
    public Vector2 pitchRange = new(0.97f, 1.03f);

    private void PlayClip(AudioClip[] bank)
    {
        if (sfx == null)
        {
            Debug.LogWarning("[ToolSfx] Không có AudioSource!");
            return;
        }
        if (bank == null || bank.Length == 0)
        {
            Debug.LogWarning("[ToolSfx] Bank rỗng, chưa gán clip!");
            return;
        }

        sfx.pitch = Random.Range(pitchRange.x, pitchRange.y);
        var clip = bank[Random.Range(0, bank.Length)];
        Debug.Log($"[ToolSfx] PlayOneShot {clip?.name}");
        sfx.PlayOneShot(clip);
    }

    // === HOE ===
    public void PlayHoeSwing() => PlayClip(hoeSwingClips);
    public void PlayHoeHit() => PlayClip(hoeHitClips);

    // === WATER ===
    public void PlayWaterOnce() => PlayClip(waterOneShotClips);

    public void StartWaterLoop()
    {
        if (sfx == null || waterLoopClip == null) return;
        if (sfx.isPlaying && sfx.clip == waterLoopClip) return;

        sfx.pitch = Random.Range(pitchRange.x, pitchRange.y);
        sfx.clip = waterLoopClip;
        sfx.loop = true;
        sfx.Play();
        Debug.Log("[ToolSfx] StartWaterLoop");
    }

    public void StopWaterLoop()
    {
        if (sfx == null) return;
        if (sfx.isPlaying && sfx.clip == waterLoopClip) sfx.Stop();

        sfx.loop = false;
        sfx.clip = null;
        sfx.pitch = 1f;
        Debug.Log("[ToolSfx] StopWaterLoop");
    }

    private void OnValidate()
    {
        if (sfx == null)
            sfx = GetComponent<AudioSource>() ?? GetComponentInParent<AudioSource>();
    }
}
