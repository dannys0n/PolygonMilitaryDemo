using UnityEngine;

public class SoldierLook : MonoBehaviour
{
    public Transform playerCamera;
    public Animator animator;
    public Transform aimTarget;
    public Camera aimCamera;

    private float pitch,yaw;
    private SoldierFirstPersonView view;
    void Awake()=>view=GetComponent<SoldierFirstPersonView>();
    public float Yaw=>yaw;
    void OnEnable()=>yaw=transform.eulerAngles.y;
    void Update(){ApplyCamera();UpdateAimTarget();}
    void LateUpdate(){ApplyCamera();UpdateAimTarget();}
    void ApplyCamera()
    {
        // TPS and its subject share the same interpolated heading. FPS keeps raw look input.
        float cameraYaw=view&&!view.firstPerson?transform.eulerAngles.y:yaw;
        playerCamera.rotation=Quaternion.Euler(pitch,cameraYaw,0);
    }
    public float Pitch => pitch;

    // Angular mouse/input deltas in degrees; the caller chooses input sensitivity.
    public void ApplyLookDelta(Vector2 delta)
    {
        if (!enabled) return;
        float mouseX = delta.x;
        float mouseY = delta.y;

        yaw+=mouseX;

        float turn = Mathf.Clamp(mouseX, -1f, 1f);
        animator.SetFloat("Turn", turn, 0.1f, Time.deltaTime);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -80f, 80f);

        ApplyCamera();

        UpdateAimTarget();
    }

    public void UpdateAimTarget()
    {
        // Non-rendering AI/vehicle cameras need only their aim direction, not a render projection.
        Ray centerRay = aimCamera.isActiveAndEnabled
            ? aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
            : new Ray(aimCamera.transform.position, aimCamera.transform.forward);
        aimTarget.position = centerRay.GetPoint(10f);
    }

    public void ResetLook()
    {
        pitch = 0f;
        yaw=transform.eulerAngles.y;ApplyCamera();
        animator.SetFloat("Turn", 0f);
        UpdateAimTarget();
    }
}
