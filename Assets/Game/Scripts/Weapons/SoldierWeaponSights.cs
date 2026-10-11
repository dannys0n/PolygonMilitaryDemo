using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

// Equipment owns its sights. The player keeps the arms and animation controller.
public class SoldierWeaponSights : MonoBehaviour
{
    public Transform firstPersonMount, thirdPersonMount;
    [Header("Default weapon sight line")]
    [FormerlySerializedAs("ironSight")] public Transform rearSight;
    [FormerlySerializedAs("frontIronSight")] public Transform frontSight;
#if UNITY_EDITOR
    // Spawned players can lose prefab links; the placement tool still needs their authoring asset.
    [HideInInspector] public GameObject authoringPrefab;
#endif
    public bool cameraReferenceConfigured;
    public Vector3 neutralCameraPosition;
    public Quaternion neutralCameraRotation = Quaternion.identity;
    Transform replacementRear, replacementFront;
    public WeaponSightDefinition defaultSight;
    public WeaponSightDefinition[] compatibleSights;
    [Header("Neutral ADS reference, relative to FirstPersonView")]
    public Vector3 referenceMountPosition;
    public Quaternion referenceMountRotation = Quaternion.identity;
    public Vector3 referenceMountScale = Vector3.one;
    public Vector3 referenceCameraPosition;
    public Quaternion referenceCameraRotation = Quaternion.identity;

    public WeaponSightDefinition SelectedSight { get; private set; }
    public Transform ActiveAimPoint => firstAttachment && firstAttachment.rearSight ? firstAttachment.rearSight
        : replacementRear ? replacementRear : rearSight;
    public Transform ActiveFrontAimPoint => firstAttachment && firstAttachment.frontSight ? firstAttachment.frontSight
        : replacementFront ? replacementFront : frontSight;
    public int Revision { get; private set; }
    public WeaponSightAttachment FirstPersonAttachment => firstAttachment;
    SoldierFirstPersonView owner;
    WeaponSightAttachment firstAttachment, thirdAttachment;
    float sightLift;

    public bool Supports(WeaponSightDefinition sight) => !sight || System.Array.IndexOf(compatibleSights, sight) >= 0;

    public void Initialize(SoldierFirstPersonView view)
    {
        owner = view;
        if (!SelectedSight) SetSight(defaultSight);
    }

    public void SetSight(WeaponSightDefinition sight)
    {
        sight = sight ? sight : defaultSight;
        if (!Supports(sight)) throw new System.ArgumentException($"{sight.name} is incompatible with {name}.");
        if (SelectedSight == sight) return;
        Remove(firstAttachment); Remove(thirdAttachment);
        SelectedSight = sight;
        if (sight.prefab)
        {
            firstAttachment = Create(sight.prefab, firstPersonMount, owner.firstPersonLayer, owner.firstPerson, true);
            thirdAttachment = Create(sight.prefab, thirdPersonMount, owner.thirdPersonLayer, true, false);
        }
        else firstAttachment = thirdAttachment = null;
        ApplySightLift();
        Revision++;
        // Removed attachments are detached before Destroy, so no stale renderer references remain.
        owner.RefreshVisuals();
    }

    public void Cycle()
    {
        int index = System.Array.IndexOf(compatibleSights, SelectedSight);
        SetSight(compatibleSights[(index + 1) % compatibleSights.Length]);
    }

    static void Remove(WeaponSightAttachment attachment)
    {
        if (!attachment) return;
        attachment.gameObject.SetActive(false);
        attachment.transform.SetParent(null);
        if (Application.isPlaying) Destroy(attachment.gameObject); else DestroyImmediate(attachment.gameObject);
    }

    static WeaponSightAttachment Create(WeaponSightAttachment prefab, Transform mount, int layer, bool visible, bool firstPerson)
    {
        var attachment = Instantiate(prefab, mount);
#if UNITY_EDITOR
        attachment.SourcePrefab = prefab;
#endif
        attachment.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var node in attachment.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
        foreach (var renderer in attachment.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
            if (firstPerson) renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        return attachment;
    }

    public void SetIronReplacements(Transform rear, Transform front)
    {
        replacementRear = rear; replacementFront = front; Revision++;
    }

    public void SetRiser(float height)
    {
        if (Mathf.Approximately(sightLift, height)) return;
        sightLift = height;
        ApplySightLift();
        Revision++;
    }

    void ApplySightLift()
    {
        if (firstAttachment) firstAttachment.transform.localPosition = Vector3.up * sightLift;
        if (thirdAttachment) thirdAttachment.transform.localPosition = Vector3.up * sightLift;
    }

    public void GetAdsPlacement(Vector3 rootScale, out Vector3 position, out Quaternion rotation)
    {
        // Read only the sight relative to its mounting socket. Animated weapon motion
        // never enters this calculation, so recoil and sway are not canceled.
        var point = ActiveAimPoint;
        Vector3 localPoint = firstPersonMount.InverseTransformPoint(point.position);
        Vector3 localFront = firstPersonMount.InverseTransformPoint(ActiveFrontAimPoint.position);
        Vector3 localUp = firstPersonMount.InverseTransformDirection(point.up);
        Quaternion localRotation = Quaternion.LookRotation(localFront - localPoint, localUp);
        Vector3 referencePoint = referenceMountPosition + referenceMountRotation * Vector3.Scale(referenceMountScale, localPoint);
        Quaternion referenceRotation = referenceMountRotation * localRotation;
        rotation = referenceCameraRotation * Quaternion.Inverse(referenceRotation);
        Vector3 target = referenceCameraPosition + referenceCameraRotation * Vector3.forward * SelectedSight.eyeDistance;
        position = target - rotation * Vector3.Scale(rootScale, referencePoint);
    }
}
