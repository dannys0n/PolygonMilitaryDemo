using UnityEngine;

public class SoldierLook : MonoBehaviour
{
    public Transform playerCamera;
    public Animator animator;
    public float sensitivity = 2f;
    public Transform aimTarget;
    public Camera aimCamera;

    private float pitch;

    void Start()
    {
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
            animator.SetFloat("Turn", 0f, 0.1f, Time.deltaTime);
            return;
        }

        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

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
}
