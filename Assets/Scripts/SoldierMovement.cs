using UnityEngine;

public class SoldierMovement : MonoBehaviour
{
    public Animator animator;
    public CharacterController controller;
    public float moveSpeed = 4f;

    private float verticalSpeed;

    void Update()
    {
        Vector2 input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );

        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 movement = transform.right * input.x
                         + transform.forward * input.y;

        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        verticalSpeed += Physics.gravity.y * Time.deltaTime;

        Vector3 velocity = movement * moveSpeed;
        velocity.y = verticalSpeed;

        controller.Move(velocity * Time.deltaTime);

        animator.SetFloat("MoveX", input.x, 0.05f, Time.deltaTime);
        animator.SetFloat("MoveZ", input.y, 0.05f, Time.deltaTime);
    }
}