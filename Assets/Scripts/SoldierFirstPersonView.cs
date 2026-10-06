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
    [Header("Combined arms and weapon reload")]
    public bool useCombinedReload;
    public AnimationClip combinedReloadClip;
    private bool combinedReloadPlaying;
    private float reloadTime;
    [Header("First person motion")]
    public bool sharedReloadSway;
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

    public bool IsReloading => useCombinedReload ? combinedReloadPlaying : firstPerson &&
        (armsAnimator.GetCurrentAnimatorStateInfo(0).IsName("Reload") ||
         (armsAnimator.IsInTransition(0) && armsAnimator.GetNextAnimatorStateInfo(0).IsName("Reload")));

    void Awake()
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
        foreach (Renderer mesh in firstPersonRoot.GetComponentsInChildren<Renderer>(true))
            mesh.shadowCastingMode = ShadowCastingMode.Off;

        locomotionLayer = armsAnimator.GetLayerIndex("Locomotion");
        aimTransitionLayer = armsAnimator.GetLayerIndex("Aim Transitions");
        SetFirstPerson(firstPerson);
    }

    void Update()
    {
        if (Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.V))
            SetFirstPerson(!firstPerson);
        bool aiming = firstPerson && Cursor.lockState == CursorLockMode.Locked
            && Input.GetMouseButton(1) && !IsReloading;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, aimBlendSpeed * Time.deltaTime);
        if (firstPerson)
        {
            if (aiming != wasAiming && !IsReloading)
                armsAnimator.CrossFadeInFixedTime(aiming ? "Aim In" : "Aim Out", 0.05f, aimTransitionLayer, 0f);
            wasAiming = aiming;
            armsAnimator.SetFloat("AimBlend", aimBlend);
            playerCamera.fieldOfView = Mathf.Lerp(originalFieldOfView, aimFieldOfView, aimBlend);
        }
        firstPersonRoot.localPosition = viewOffset + hipFireOffset * (1f - aimBlend) + aimOffset * aimBlend;
    }

    void LateUpdate()
    {
        if (combinedReloadPlaying)
        {
            reloadTime = Mathf.Min(reloadTime + Time.deltaTime, combinedReloadClip.length);
            combinedReloadClip.SampleAnimation(firstPersonRoot.gameObject, reloadTime);
            if (reloadTime >= combinedReloadClip.length)
            {
                combinedReloadPlaying = false;
                if (firstPerson)
                    armsAnimator.Play("Idle", 0, 0f);
            }
        }

        if (!firstPerson)
            return;

        // Use actual movement so walking bob stops against walls and in the air.
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float movement = controller.isGrounded ? Mathf.Clamp01(velocity.magnitude / walkingSpeed) : 0f;
        if (sharedReloadSway && IsReloading) movement *= reloadWalkWeight;
        armsAnimator.SetFloat("Movement", movement, motionBlendTime, Time.deltaTime);
        float weight = IsReloading ? (sharedReloadSway ? locomotionWeight * reloadSwayWeight : 0f) : locomotionWeight;
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
        firstPersonRoot.gameObject.SetActive(enabled);
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
        if (enabled && useCombinedReload)
        {
            armsAnimator.Play(combinedReloadPlaying ? "Reload" : "Idle", 0,
                combinedReloadPlaying ? reloadTime / combinedReloadClip.length : 0f);
            weaponAnimator.Play("Idle", 0, 0f);
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
        if (!firstPerson)
            return;
        armsAnimator.CrossFadeInFixedTime("Fire", 0.03f, 0, 0f);
        weaponAnimator.Play("Fire", 0, 0f);
    }

    public void PlayReload()
    {
        if (useCombinedReload)
        {
            if (combinedReloadPlaying)
                return;
            combinedReloadPlaying = true;
            reloadTime = 0f;
        }
        if (!firstPerson)
            return;
        armsAnimator.Play("Empty", aimTransitionLayer, 0f);
        if (!sharedReloadSway) armsAnimator.SetLayerWeight(locomotionLayer, 0f);
        armsAnimator.SetLayerWeight(aimTransitionLayer, 0f);
        // Combined reloads already contain weapon-part movement in the arms clip.
        if (sharedReloadSway)
        {
            armsAnimator.CrossFadeInFixedTime("Reload", motionBlendTime, 0, 0f);
            weaponAnimator.CrossFadeInFixedTime("Reload", motionBlendTime, 0, 0f);
        }
        else
        {
            armsAnimator.Play("Reload", 0, 0f);
            weaponAnimator.Play(useCombinedReload ? "Idle" : "Reload", 0, 0f);
        }
    }
    void OnDisable()
    {
        if (combinedReloadPlaying)
            combinedReloadClip.SampleAnimation(firstPersonRoot.gameObject, combinedReloadClip.length);
        combinedReloadPlaying = false;
    }
}

