using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target; // The target to follow
    [SerializeField] private float followSpeed = 10.0f;

    Vector3 camOffset; // Offset from the target
    void Start()
    {
        camOffset = transform.position - target.position; // Calculate the initial offset
    }

    // Update is called once per frame
    private void FixedUpdate()
    {
        transform.position = Vector3.Lerp(transform.position, target.position + camOffset, Time.fixedDeltaTime * followSpeed);
    }
}
