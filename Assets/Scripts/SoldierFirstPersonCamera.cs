using UnityEngine;

// The demo puts its camera on the animated head socket. Transfer that motion
// after animation evaluation, keeping our existing camera and mouse look.
[DefaultExecutionOrder(200)]
public class SoldierFirstPersonCamera : MonoBehaviour
{
    public SoldierFirstPersonView view;
    public SoldierLook look;
    public Transform animatedCamera;
    [Range(0f, 1f)] public float positionWeight = 1f;
    [Range(0f, 1f)] public float rotationWeight = 1f;

    private Vector3 referencePosition;
    private Quaternion referenceRotation;

    void Start()
    {
        // Sample the imported idle pose rather than the FBX's bind pose.
        bool wasActive = view.firstPersonRoot.gameObject.activeSelf;
        view.firstPersonRoot.gameObject.SetActive(true);
        view.armsAnimator.Update(0f);
        referencePosition = view.firstPersonRoot.InverseTransformPoint(animatedCamera.position);
        referenceRotation = Quaternion.Inverse(view.firstPersonRoot.rotation) * animatedCamera.rotation;
        view.firstPersonRoot.gameObject.SetActive(wasActive);
    }

    void LateUpdate()
    {
        if (!view.firstPerson)
            return;

        Vector3 position = view.firstPersonRoot.InverseTransformPoint(animatedCamera.position);
        Quaternion rotation = Quaternion.Inverse(view.firstPersonRoot.rotation) * animatedCamera.rotation;
        Transform cameraTransform = view.playerCamera.transform;
        cameraTransform.localPosition = (position - referencePosition) * positionWeight;
        cameraTransform.localRotation = Quaternion.Slerp(Quaternion.identity,
            rotation * Quaternion.Inverse(referenceRotation), rotationWeight);

        // The target follows the rendered camera, including its authored recoil.
        look.UpdateAimTarget();
    }
}
