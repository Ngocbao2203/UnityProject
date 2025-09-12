using UnityEngine;

namespace CGP.Gameplay.Systems
{
    public class Movement : MonoBehaviour
    {
        [Header("Movement")]
        public float speed;
        public Animator animator;

        private Vector3 direction;
        private Player player;

        [Header("Footstep SFX")]
        public AudioSource sfx;             // Kéo AudioSource của Player vào đây
        public AudioClip footstepClip;      // File .wav/.mp3 bước chân
        [Tooltip("Khoảng thời gian giữa 2 tiếng bước chân")]
        public float footstepInterval = 0.28f;
        [Tooltip("Random pitch để đỡ lặp lại nhàm chán")]
        public Vector2 pitchRange = new Vector2(0.96f, 1.04f);

        private float footstepTimer;

        private void Awake()
        {
            player = GetComponent<Player>();

            // Đảm bảo AudioSource không tự phát/loop
            if (sfx != null)
            {
                sfx.playOnAwake = false;
                sfx.loop = false;
            }
        }

        private void Update()
        {
            if (!player.canMove)
            {
                AnimateMovement(Vector3.zero); // Giữ nhân vật đứng yên
                direction = Vector3.zero;
                ResetFootstepTimer();
                return;
            }

            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            direction = new Vector3(horizontal, vertical).normalized;

            AnimateMovement(direction);
            HandleFootsteps(direction);
        }

        private void FixedUpdate()
        {
            // (Bạn đang dùng Time.deltaTime trong FixedUpdate — vẫn chạy,
            // nhưng chuẩn hơn là chuyển dòng này sang Update hoặc dùng Rigidbody2D.)
            transform.position += direction * speed * Time.deltaTime;
        }

        private void AnimateMovement(Vector3 direction)
        {
            if (animator != null)
            {
                if (direction.magnitude > 0f)
                {
                    animator.SetBool("isMoving", true);
                    animator.SetFloat("horizontal", direction.x);
                    animator.SetFloat("vertical", direction.y);

                    if (player != null)
                    {
                        // Cập nhật hướng nhìn hiện tại để dùng khi Hoe
                        player.facingDirection = new Vector2(Mathf.Round(direction.x), Mathf.Round(direction.y));
                    }
                }
                else
                {
                    animator.SetBool("isMoving", false);
                }
            }
        }

        private void HandleFootsteps(Vector3 dir)
        {
            if (sfx == null || footstepClip == null)
                return;

            if (dir.magnitude > 0.1f) // đang di chuyển
            {
                footstepTimer -= Time.deltaTime;
                if (footstepTimer <= 0f)
                {
                    // Random nhẹ pitch để đỡ nhàm
                    sfx.pitch = Random.Range(pitchRange.x, pitchRange.y);
                    sfx.PlayOneShot(footstepClip);
                    footstepTimer = footstepInterval;
                }
            }
            else
            {
                ResetFootstepTimer();
            }
        }

        private void ResetFootstepTimer()
        {
            footstepTimer = 0f;
            if (sfx != null) sfx.pitch = 1f;
        }

        // Tự nhận AudioSource khi bạn thêm component
        private void OnValidate()
        {
            if (sfx == null) sfx = GetComponent<AudioSource>();
        }
    }
}
