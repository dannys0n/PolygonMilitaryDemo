using UnityEngine;

public class SoldierLook : MonoBehaviour
{
    public Transform playerCamera;
    public Animator animator;
    public float sensitivity = 2f;
    public Transform aimTarget;
    public float bodyPitchLimit = 45f;

    private float pitch;

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (aimTarget == null && animator != null)
        {
            foreach (Transform child in animator.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "AimTarget")
                {
                    aimTarget = child;
                    break;
                }
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (animator != null)
                animator.SetFloat("Turn", 0f, 0.1f, Time.deltaTime);
            return;
        }

        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

        transform.Rotate(0f, mouseX, 0f);

        if (animator != null)
        {
            float turn = Mathf.Clamp(mouseX, -1f, 1f);
            animator.SetFloat("Turn", turn, 0.1f, Time.deltaTime);
        }

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -80f, 80f);

        playerCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (aimTarget != null)
        {
            float bodyPitch = Mathf.Clamp(pitch, -bodyPitchLimit, bodyPitchLimit);
            Vector3 aimDirection = transform.rotation
                                 * Quaternion.Euler(bodyPitch, 0f, 0f)
                                 * Vector3.forward;
            aimTarget.position = playerCamera.position + aimDirection * 10f;
        }
    }
}
