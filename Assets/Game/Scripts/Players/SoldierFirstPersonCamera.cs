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
    private SoldierWeaponLoadout loadout;
    public float positionWeight => presentation.weapon.definition.cameraPositionWeight;
    public float rotationWeight => presentation.weapon.definition.cameraRotationWeight;

    private Vector3 referencePosition;
    private Quaternion referenceRotation;

    void Awake()
    {
        presentation = view.GetComponent<SoldierWeaponPresentation>();
        loadout = GetComponent<SoldierWeaponLoadout>();
    }
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
        if (!view.NeedsCameraAnimation)
        {
            // Hidden AI cameras remain a neutral source of gameplay aim.
            var hiddenCamera = view.playerCamera.transform;
            hiddenCamera.SetLocalPositionAndRotation(view.CameraRestPosition, view.CameraRestRotation);
            bodyAimTarget.position = hiddenCamera.parent.position + hiddenCamera.parent.forward * 10f;
            look.UpdateAimTarget();
            return;
        }
        var profile = loadout.EquippedSights;
        // Use the selected weapon's authored camera baseline after a swap.
        Vector3 idlePosition = profile && profile.cameraReferenceConfigured ? profile.neutralCameraPosition : referencePosition;
        Quaternion idleRotation = profile && profile.cameraReferenceConfigured ? profile.neutralCameraRotation : referenceRotation;
        Vector3 position = view.firstPersonRoot.InverseTransformPoint(animatedCamera.position);
        Quaternion rotation = Quaternion.Inverse(view.firstPersonRoot.rotation) * animatedCamera.rotation;
        Transform cameraTransform = view.playerCamera.transform;
        cameraTransform.localPosition = view.CameraRestPosition +
            view.CameraRestRotation * ((position - idlePosition) * positionWeight);
        cameraTransform.localRotation = view.CameraRestRotation * Quaternion.Slerp(Quaternion.identity,
            rotation * Quaternion.Inverse(idleRotation), rotationWeight);

        // Body aim follows the player look and recoil source, independently of camera placement.
        Vector3 recoilPosition = (position - idlePosition) * positionWeight;
        Quaternion recoilRotation = Quaternion.Slerp(Quaternion.identity, rotation * Quaternion.Inverse(idleRotation), rotationWeight);
        Transform cameraMount = cameraTransform.parent;
        bodyAimTarget.position = cameraMount.TransformPoint(recoilPosition) +
            cameraMount.rotation * recoilRotation * Vector3.forward * 10f;

        // Weapon aim still follows the center of the rendered camera.
        look.UpdateAimTarget();
    }
}
