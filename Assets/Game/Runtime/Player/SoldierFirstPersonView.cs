using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-10)]
public class SoldierFirstPersonView : MonoBehaviour
{
    public Camera playerCamera;
    public Transform thirdPersonRoot;
    public Transform firstPersonRoot;
    public Animator armsAnimator;
    public Animator weaponAnimator;
    public CharacterController controller;
    public bool firstPerson = true;
    public int firstPersonLayer = 8;
    public int thirdPersonLayer = 9;

    private SoldierWeaponDefinition definition;
    public event System.Action WeaponChanged;
    public float reloadSwayWeight => definition.reloadSwayWeight;
    public float reloadWalkWeight => definition.reloadWalkWeight;
    public float locomotionWeight => definition.locomotionWeight;
    public float aimTransitionWeight => definition.aimTransitionWeight;
    public float motionBlendTime => definition.motionBlendTime;
    public float walkingSpeed => definition.walkingSpeed;
    public Vector3 viewOffset => definition.viewOffset;
    public Vector3 hipFireOffset => definition.hipFireOffset;
    public float aimFieldOfView => definition.aimFieldOfView;
    public float aimBlendSpeed => definition.aimBlendSpeed;
    public Vector3 aimOffset => definition.aimOffset;

    private Vector3 thirdPersonCameraPosition;
    private Quaternion thirdPersonCameraRotation;
    private int originalCullingMask;
    private float originalNearClip;
    private float originalFieldOfView;
    private float aimBlend;
    private bool wasAiming;
    private int locomotionLayer;
    private int aimTransitionLayer;
    private Renderer[] firstPersonRenderers;

    public Vector3 CameraRestPosition => firstPerson ? Vector3.zero : thirdPersonCameraPosition;
    public Quaternion CameraRestRotation => firstPerson ? Quaternion.identity : thirdPersonCameraRotation;

    public bool IsReloading =>
        (armsAnimator.GetCurrentAnimatorStateInfo(0).IsName("Reload") ||
         (armsAnimator.IsInTransition(0) && armsAnimator.GetNextAnimatorStateInfo(0).IsName("Reload")));

    private void Awake()
    {
        definition = GetComponent<SoldierWeaponPresentation>().weapon.definition;
        thirdPersonCameraPosition = playerCamera.transform.localPosition;
        thirdPersonCameraRotation = playerCamera.transform.localRotation;
        originalCullingMask = playerCamera.cullingMask;
        originalNearClip = playerCamera.nearClipPlane;
        originalFieldOfView = playerCamera.fieldOfView;

        // These are visibility groups, not gameplay or networking ownership.
        foreach (Transform child in thirdPersonRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = thirdPersonLayer;
        foreach (Transform child in firstPersonRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = firstPersonLayer;
        firstPersonRenderers = firstPersonRoot.GetComponentsInChildren<Renderer>(true);
        armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        weaponAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (Renderer mesh in firstPersonRenderers)
            mesh.shadowCastingMode = ShadowCastingMode.Off;

        locomotionLayer = armsAnimator.GetLayerIndex("Locomotion");
        aimTransitionLayer = armsAnimator.GetLayerIndex("Aim Transitions");
        SetFirstPerson(firstPerson);
    }

    private void Update()
    {
        if (Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.V))
            SetFirstPerson(!firstPerson);
        bool aiming = firstPerson && Cursor.lockState == CursorLockMode.Locked
            && Input.GetMouseButton(1) && !IsReloading;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, aimBlendSpeed * Time.deltaTime);
        if (aiming != wasAiming && !IsReloading)
            armsAnimator.CrossFadeInFixedTime(aiming ? "Aim In" : "Aim Out", 0.05f, aimTransitionLayer, 0f);
        wasAiming = aiming;
        armsAnimator.SetFloat("AimBlend", aimBlend);
        if (firstPerson) playerCamera.fieldOfView = Mathf.Lerp(originalFieldOfView, aimFieldOfView, aimBlend);
        firstPersonRoot.localPosition = viewOffset + hipFireOffset * (1f - aimBlend) + aimOffset * aimBlend;
    }

    private void LateUpdate()
    {
        // Use actual movement so walking bob stops against walls and in the air.
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float movement = controller.isGrounded ? Mathf.Clamp01(velocity.magnitude / walkingSpeed) : 0f;
        if (IsReloading) movement *= reloadWalkWeight;
        armsAnimator.SetFloat("Movement", movement, motionBlendTime, Time.deltaTime);
        float weight = IsReloading ? locomotionWeight * reloadSwayWeight : locomotionWeight;
        armsAnimator.SetLayerWeight(locomotionLayer, Mathf.MoveTowards(
            armsAnimator.GetLayerWeight(locomotionLayer), weight, Time.deltaTime / motionBlendTime));
        armsAnimator.SetLayerWeight(aimTransitionLayer, IsReloading ? 0f : aimTransitionWeight);
    }

    public void ApplyWeaponDefinition(SoldierWeaponDefinition selectedDefinition)
    {
        definition = selectedDefinition;
        aimBlend = 0f;
        wasAiming = false;
        locomotionLayer = armsAnimator.GetLayerIndex("Locomotion");
        aimTransitionLayer = armsAnimator.GetLayerIndex("Aim Transitions");
        firstPersonRoot.localPosition = viewOffset + hipFireOffset;
        playerCamera.fieldOfView = originalFieldOfView;
        armsAnimator.Update(0f);
        weaponAnimator.Update(0f);
        WeaponChanged?.Invoke();
    }

    public void SetFirstPerson(bool enabled)
    {
        firstPerson = enabled;
        aimBlend = 0f;
        wasAiming = false;
        playerCamera.fieldOfView = originalFieldOfView;
        firstPersonRoot.gameObject.SetActive(true);
        foreach (Renderer mesh in firstPersonRenderers) mesh.enabled = enabled;
        playerCamera.transform.localPosition = enabled ? Vector3.zero : thirdPersonCameraPosition;
        playerCamera.transform.localRotation = enabled ? Quaternion.identity : thirdPersonCameraRotation;
        playerCamera.nearClipPlane = enabled ? 0.03f : originalNearClip;
        playerCamera.cullingMask = enabled
            ? (originalCullingMask | (1 << firstPersonLayer)) & ~(1 << thirdPersonLayer)
            : originalCullingMask & ~(1 << firstPersonLayer);
    }

    public void PlayFire()
    {
        armsAnimator.CrossFadeInFixedTime("Fire", 0.03f, 0, 0f);
        weaponAnimator.Play("Fire", 0, 0f);
    }

    public void PlayReload()
    {
        armsAnimator.Play("Empty", aimTransitionLayer, 0f);
        armsAnimator.SetLayerWeight(aimTransitionLayer, 0f);
        armsAnimator.CrossFadeInFixedTime("Reload", motionBlendTime, 0, 0f);
        weaponAnimator.CrossFadeInFixedTime("Reload", motionBlendTime, 0, 0f);
    }
}

