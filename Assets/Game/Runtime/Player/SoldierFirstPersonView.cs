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
    [Header("Reload continuity between views")]
    public bool synchronizeReload;
    public Animator characterAnimator;
    [Header("First person motion")]
    [Range(0f, 1f)] public float reloadSwayWeight = 0.65f;
    [Range(0f, 1f)] public float reloadWalkWeight = 0.35f;
    [Range(0f, 1f)] public float locomotionWeight = 1f;
    [Range(0f, 1f)] public float aimTransitionWeight = 1f;
    [Min(0.01f)] public float motionBlendTime = 0.12f;
    [Min(0.01f)] public float walkingSpeed = 4f;
    public bool firstPerson = true;
    public Vector3 viewOffset;
    [Header("Hip-fire placement")]
    [Tooltip("Camera-local offset for the arms and weapon when not aiming. Blends out during ADS.")]
    public Vector3 hipFireOffset;
    [Header("Aim down sights")]
    [Range(20f, 90f)] public float aimFieldOfView = 50f;
    [Min(0.01f)] public float aimBlendSpeed = 8f;
    public Vector3 aimOffset;
    public int firstPersonLayer = 8;
    public int thirdPersonLayer = 9;

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
        {
            if (aiming != wasAiming && !IsReloading)
                armsAnimator.CrossFadeInFixedTime(aiming ? "Aim In" : "Aim Out", 0.05f, aimTransitionLayer, 0f);
            wasAiming = aiming;
            armsAnimator.SetFloat("AimBlend", aimBlend);
            if (firstPerson) playerCamera.fieldOfView = Mathf.Lerp(originalFieldOfView, aimFieldOfView, aimBlend);
        }
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

    public void SetFirstPerson(bool enabled)
    {
        firstPerson = enabled;
        aimBlend = 0f;
        wasAiming = false;
        playerCamera.fieldOfView = originalFieldOfView;
        firstPersonRoot.gameObject.SetActive(true);
        foreach (Renderer mesh in firstPersonRenderers) mesh.enabled = enabled;
        if (enabled && synchronizeReload)
        {
            int layer = characterAnimator.GetLayerIndex("Reload");
            var state = characterAnimator.GetCurrentAnimatorStateInfo(layer);
            if (characterAnimator.IsInTransition(layer))
            {
                var next = characterAnimator.GetNextAnimatorStateInfo(layer);
                if (next.IsName("Reload")) state = next;
            }
            if (state.IsName("Reload"))
            {
                armsAnimator.Play("Reload", 0, Mathf.Clamp01(state.normalizedTime));
                weaponAnimator.Play("Reload", 0, Mathf.Clamp01(state.normalizedTime));
            }
        }
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

