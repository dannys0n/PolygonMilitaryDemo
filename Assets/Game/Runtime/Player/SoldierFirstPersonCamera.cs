using UnityEngine;

// The demo puts its camera on the animated head socket. Transfer that motion
// after animation evaluation, keeping our existing camera and mouse look.
[DefaultExecutionOrder(200)]
public class SoldierFirstPersonCamera : MonoBehaviour
{
    public SoldierFirstPersonView view;
    public SoldierLook look;
    public Transform animatedCamera;
    public Transform bodyAimTarget;
    private SoldierWeaponPresentation presentation;
    public float positionWeight => presentation.weapon.definition.cameraPositionWeight;
    public float rotationWeight => presentation.weapon.definition.cameraRotationWeight;

    private Vector3 referencePosition;
    private Quaternion referenceRotation;

    void Awake() => presentation = view.GetComponent<SoldierWeaponPresentation>();
    void OnEnable() => view.WeaponChanged += RefreshReferencePose;
    void OnDisable() => view.WeaponChanged -= RefreshReferencePose;
    void Start() => RefreshReferencePose();

    void RefreshReferencePose()
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
        Vector3 position = view.firstPersonRoot.InverseTransformPoint(animatedCamera.position);
        Quaternion rotation = Quaternion.Inverse(view.firstPersonRoot.rotation) * animatedCamera.rotation;
        Transform cameraTransform = view.playerCamera.transform;
        cameraTransform.localPosition = view.CameraRestPosition +
            view.CameraRestRotation * ((position - referencePosition) * positionWeight);
        cameraTransform.localRotation = view.CameraRestRotation * Quaternion.Slerp(Quaternion.identity,
            rotation * Quaternion.Inverse(referenceRotation), rotationWeight);

        // Body aim follows the player look and recoil source, independently of camera placement.
        Vector3 recoilPosition = (position - referencePosition) * positionWeight;
        Quaternion recoilRotation = Quaternion.Slerp(Quaternion.identity, rotation * Quaternion.Inverse(referenceRotation), rotationWeight);
        Transform cameraMount = cameraTransform.parent;
        bodyAimTarget.position = cameraMount.TransformPoint(recoilPosition) +
            cameraMount.rotation * recoilRotation * Vector3.forward * 10f;

        // Weapon aim still follows the center of the rendered camera.
        look.UpdateAimTarget();
    }
}
