using UnityEngine;

public class SoldierLook : MonoBehaviour
{
    public Transform playerCamera;
    public Animator animator;
    public float sensitivity = 2f;
    public Transform aimTarget;
    public Camera aimCamera;

    private float pitch;

    // Angular mouse/input deltas in degrees; the caller chooses input sensitivity.
    public void ApplyLookDelta(Vector2 delta)
    {
        if (!enabled) return;
        float mouseX = delta.x;
        float mouseY = delta.y;

        transform.Rotate(0f, mouseX, 0f);

        float turn = Mathf.Clamp(mouseX, -1f, 1f);
        animator.SetFloat("Turn", turn, 0.1f, Time.deltaTime);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -80f, 80f);

        playerCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        UpdateAimTarget();
    }

    public void UpdateAimTarget()
    {
        Ray centerRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        aimTarget.position = centerRay.GetPoint(10f);
    }

    public void ResetLook()
    {
        pitch = 0f;
        playerCamera.localRotation = Quaternion.identity;
        animator.SetFloat("Turn", 0f);
        UpdateAimTarget();
    }
}
