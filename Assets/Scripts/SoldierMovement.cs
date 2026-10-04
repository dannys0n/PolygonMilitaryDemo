using UnityEngine;

public class SoldierMovement : MonoBehaviour
{
    public Animator animator;
    public CharacterController controller;
    public float moveSpeed = 4f;
    [Min(0f)] public float jumpHeight = 1.2f;

    private float verticalSpeed;
    private bool jumpRequested;

    public void RequestJump()
    {
        jumpRequested = true;
    }

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

        if (jumpRequested && controller.isGrounded && verticalSpeed <= 0f)
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
        jumpRequested = false;

        verticalSpeed += Physics.gravity.y * Time.deltaTime;

        Vector3 velocity = movement * moveSpeed;
        velocity.y = verticalSpeed;

        CollisionFlags collisions = controller.Move(velocity * Time.deltaTime);
        if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
            verticalSpeed = 0f;

        animator.SetFloat("MoveX", input.x, 0.05f, Time.deltaTime);
        animator.SetFloat("MoveZ", input.y, 0.05f, Time.deltaTime);
        animator.SetBool("Grounded", controller.isGrounded);
        animator.SetFloat("VerticalSpeed", verticalSpeed);
    }
}