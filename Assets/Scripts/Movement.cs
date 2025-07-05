using UnityEngine;

public class Movement : MonoBehaviour
{
    public float speed;
    public Animator animator;

    private Vector3 direction;
    private Player player;

    private void Awake()
    {
        player = GetComponent<Player>();
    }

    private void Update()
    {
        if (!player.canMove)
        {
            AnimateMovement(Vector3.zero); // Giữ nhân vật đứng yên
            direction = Vector3.zero;
            return;
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        direction = new Vector3(horizontal, vertical).normalized;

        AnimateMovement(direction);
    }

    private void FixedUpdate()
    {
        transform.position += direction * speed * Time.deltaTime;
    }

    private void AnimateMovement(Vector3 direction)
    {
        if (animator != null)
        {
            if (direction.magnitude > 0)
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
}
