using UnityEngine;

public class Movement : MonoBehaviour
{
    public float speed = 5f;
    public Animator animator;

    private Vector2 lastMoveDir = Vector2.down;

    void Update()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector2 inputDir = new Vector2(horizontal, vertical);

        if (inputDir.sqrMagnitude > 0.01f)
        {
            lastMoveDir = inputDir.normalized;
        }

        AnimateMovement(inputDir);
        transform.position += (Vector3)inputDir * speed * Time.deltaTime;
    }

    void AnimateMovement(Vector2 inputDir)
    {
        if (animator == null) return;

        bool isMoving = inputDir.sqrMagnitude > 0.01f;
        Vector2 animDir = isMoving ? inputDir.normalized : lastMoveDir;

        animator.SetBool("isMoving", isMoving);
        animator.SetFloat("horizontal", animDir.x);
        animator.SetFloat("vertical", animDir.y);
    }

}