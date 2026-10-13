using UnityEngine;

public class SoldierMovement : MonoBehaviour
{
    public Animator animator;
    public CharacterController controller;
    public float moveSpeed = 4f;
    [Min(0f)] public float runSpeed = 6f;
    [Min(0.01f)] public float firstPersonSprintBlendDuration = 0.3f;
    private float firstPersonRunBlend;
    public bool IsRunning { get; private set; }
    private SoldierFirstPersonView view;

    [Min(0f)] public float jumpHeight = 1.2f;

    private float verticalSpeed;
    private bool jumpRequested;
    private SoldierVehiclePlatform vehiclePlatform;

    void Awake()
    {
        view = GetComponent<SoldierFirstPersonView>();
        vehiclePlatform=GetComponent<SoldierVehiclePlatform>();
        if(!vehiclePlatform)vehiclePlatform=gameObject.AddComponent<SoldierVehiclePlatform>();
    }

    public void RequestJump()
    {
        if (!enabled) return;
        jumpRequested = true;
    }

    public void ResetMovement()
    {
        verticalSpeed = 0f;
        vehiclePlatform?.ResetSupport();
        jumpRequested = false;
        IsRunning = false;
        firstPersonRunBlend = 0f;
        animator.SetFloat("RunBlend", 0f);
        animator.SetFloat("MoveX", 0f);
        animator.SetFloat("MoveZ", 0f);
        animator.SetFloat("VerticalSpeed", 0f);
        view.armsAnimator.SetFloat("RunBlend", 0f);
    }

    public void SimulateMovement(Vector2 input, float deltaTime, bool runRequested = false)
    {
        if (!enabled) return;
        input = Vector2.ClampMagnitude(input, 1f);
        IsRunning = runRequested && input.y > .1f && controller.isGrounded
            && !view.IsReloading && !view.IsEquipping && view.AimBlend < .01f
            && !view.WantsToAim;

        Vector3 movement = transform.right * input.x
                         + transform.forward * input.y;
        bool jumping=jumpRequested && controller.isGrounded && verticalSpeed<=0;
        vehiclePlatform.BeginStep(movement,deltaTime,jumping);

        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        if (jumpRequested && controller.isGrounded && verticalSpeed <= 0f)
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
        jumpRequested = false;

        verticalSpeed += Physics.gravity.y * deltaTime;

        Vector3 velocity = movement * (IsRunning ? runSpeed : moveSpeed);
        velocity.y = verticalSpeed;

        CollisionFlags collisions = controller.Move(velocity * deltaTime);
        vehiclePlatform.EndStep(jumping || verticalSpeed>0);
        if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
            verticalSpeed = 0f;

        animator.SetFloat("RunBlend", IsRunning ? 1f : 0f, .15f, deltaTime);
        firstPersonRunBlend = Mathf.MoveTowards(firstPersonRunBlend,
            IsRunning ? 1f : 0f, deltaTime / firstPersonSprintBlendDuration);
        view.armsAnimator.SetFloat("RunBlend", Mathf.SmoothStep(0f, 1f, firstPersonRunBlend));
        animator.SetFloat("MoveX", input.x, 0.05f, deltaTime);
        animator.SetFloat("MoveZ", input.y, 0.05f, deltaTime);
        animator.SetBool("Grounded", controller.isGrounded);
        animator.SetFloat("VerticalSpeed", verticalSpeed);
    }
}
