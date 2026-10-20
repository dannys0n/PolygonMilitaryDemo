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
    WeaponAttachmentDefinition replacementRearDefinition;
    public WeaponSightDefinition defaultSight;
    public WeaponSightDefinition[] compatibleSights;
    [Header("Neutral weapon pose, relative to FirstPersonView")]
    public Vector3 referenceWeaponPosition;
    public Quaternion referenceWeaponRotation = Quaternion.identity;
    public Vector3 referenceWeaponScale = Vector3.one;
    public Vector3 referenceCameraPosition;
    public Quaternion referenceCameraRotation = Quaternion.identity;

    public WeaponSightDefinition BareSight => System.Array.Find(compatibleSights, sight => sight && !sight.prefab);
    public WeaponSightDefinition SelectedSight { get; private set; }
    public bool AlternateSightActive { get; private set; }
    WeaponSightAttachment AimAttachment => firstAttachment ? firstAttachment : thirdAttachment;
    public bool HasAlternateSight => AimAttachment && AimAttachment.HasAlternateSight;
    public float FieldOfView => AlternateSightActive ? AimAttachment.alternateFieldOfView : SelectedSight.fieldOfView;
    public float SensitivityMultiplier => AlternateSightActive ? AimAttachment.alternateSensitivityMultiplier : SelectedSight.sensitivityMultiplier;
    public bool UsesPictureInPicture => SelectedSight && SelectedSight.pictureInPicture && !AlternateSightActive;
    public Transform ActiveAimPoint => AlternateSightActive ? AimAttachment.alternateRearSight
        : firstAttachment && firstAttachment.rearSight ? firstAttachment.rearSight
        : replacementRear ? replacementRear : rearSight;
    public Transform ActiveFrontAimPoint => AlternateSightActive ? AimAttachment.alternateFrontSight
        : firstAttachment && firstAttachment.frontSight ? firstAttachment.frontSight
        : replacementFront ? replacementFront : frontSight;
    public WeaponAttachmentDefinition ActiveRearIronDefinition => firstAttachment && firstAttachment.rearSight
        ? null : replacementRear ? replacementRearDefinition : null;
    public bool UsesOpticEyeDistance => firstAttachment && firstAttachment.rearSight;
    public float EyeDistance => AlternateSightActive ? AimAttachment.alternateEyeDistance : UsesOpticEyeDistance ? SelectedSight.eyeDistance
        : ActiveRearIronDefinition ? ActiveRearIronDefinition.eyeDistance
        : loadout.EquippedWeapon.definition.builtInIronEyeDistance;
    public int Revision { get; private set; }
    public WeaponSightAttachment FirstPersonAttachment => firstAttachment;
    SoldierFirstPersonView owner;
    SoldierWeaponLoadout loadout;
    WeaponSightAttachment firstAttachment, thirdAttachment;
    float sightLift;

    public bool Supports(WeaponSightDefinition sight)
    {
        if(!sight)return true;
        if(System.Array.IndexOf(compatibleSights,sight)<0)return false;
        if(!sight.prefab)return true;
        var mount=thirdPersonMount?thirdPersonMount:firstPersonMount;
        return mount && mount.TryGetComponent<WeaponAttachmentSocket>(out var socket)
            ? socket.SupportsSight(sight) : sight.mountStandard==WeaponSightDefinition.MountStandard.Picatinny;
    }

    public void Initialize(SoldierFirstPersonView view)
    {
        owner = view;
        loadout = view.GetComponent<SoldierWeaponLoadout>();

    }

    public void SetSight(WeaponSightDefinition sight)
    {
        sight = sight ? sight : BareSight;
        if(!sight)throw new System.InvalidOperationException($"{name} has no bare sight configuration.");
        if (!Supports(sight)) throw new System.ArgumentException($"{sight.name} is incompatible with {name}.");
        if (SelectedSight == sight) return;
        if(owner && owner.TryGetComponent<SoldierScopeView>(out var scope))scope.ClearPresentation();
        Remove(firstAttachment); Remove(thirdAttachment);
        SelectedSight = sight;
        AlternateSightActive = false;
        if(firstPersonMount && firstPersonMount.TryGetComponent<WeaponAttachmentSocket>(out var firstSocket))firstSocket.SetBuiltInSightVisible(sight);
        if(thirdPersonMount && thirdPersonMount.TryGetComponent<WeaponAttachmentSocket>(out var thirdSocket))thirdSocket.SetBuiltInSightVisible(sight);
        if (sight.prefab)
        {
            if (firstPersonMount) firstAttachment = Create(sight.prefab, firstPersonMount, owner.firstPersonLayer, owner.firstPerson, true);
            thirdAttachment = Create(sight.prefab, thirdPersonMount, owner.thirdPersonLayer, true, false);
        }
        else firstAttachment = thirdAttachment = null;
        ApplySightLift();
        if(TryGetComponent<SoldierWeaponAttachments>(out var attachments))attachments.RefreshIronVisibility();
        Revision++;
        // Returned attachments leave the rig immediately, so no stale renderer references remain.
        owner.RefreshVisuals();
    }

    public void ClearPresentation()
    {
        if(owner && owner.TryGetComponent<SoldierScopeView>(out var scope))scope.ClearPresentation();
        Remove(firstAttachment); Remove(thirdAttachment);
        firstAttachment=thirdAttachment=null; replacementRear=replacementFront=null;
        SelectedSight=null; AlternateSightActive=false; replacementRearDefinition=null; sightLift=0; Revision++;
    }

    public bool SetAlternateSight(bool active)
    {
        if (active && !HasAlternateSight) return false;
        if (AlternateSightActive == active) return true;
        AlternateSightActive = active;
        Revision++;
        return true;
    }

    public void Cycle()
    {
        int index = System.Array.IndexOf(compatibleSights, SelectedSight);
        for(int i=1;i<=compatibleSights.Length;i++)
        {
            var next=compatibleSights[(index+i)%compatibleSights.Length];
            if(next && Supports(next)){SetSight(next);return;}
        }
    }

    static void Remove(WeaponSightAttachment attachment)
    {
        if (!attachment) return;
        var support=attachment.transform.Find("RequiredSightRiser");
        if(support)EquipmentVisualPool.Return(support.gameObject);
        EquipmentVisualPool.Return(attachment.gameObject);
    }

    static WeaponSightAttachment Create(WeaponSightAttachment prefab, Transform mount, int layer, bool visible, bool firstPerson)
    {
        var attachment = EquipmentVisualPool.Rent(prefab.gameObject, mount).GetComponent<WeaponSightAttachment>();
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

    public void SetIronReplacements(Transform rear, Transform front, WeaponAttachmentDefinition rearDefinition)
    {
        replacementRear = rear; replacementFront = front; replacementRearDefinition = rearDefinition; Revision++;
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
        ApplySightSupport(firstAttachment, firstPersonMount);
        ApplySightSupport(thirdAttachment, thirdPersonMount);
    }

    void ApplySightSupport(WeaponSightAttachment attachment, Transform mount)
    {
        if (!attachment) return;
        var placement = mount.TryGetComponent<WeaponAttachmentSocket>(out var socket)
            ? socket.SupportFor(SelectedSight) : default;
        var required = placement.riser;
        float lift = Mathf.Max(sightLift, required ? required.sightLift : 0f);
        attachment.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
        attachment.transform.localScale=SelectedSight.prefab.transform.localScale;
        if(socket)socket.Place(SelectedSight,attachment.transform);
        if (sightLift > 0f)
            attachment.transform.localPosition += Vector3.up * (sightLift - WeaponSightAttachment.MountingBaseY(attachment.transform));
        else
        {
            attachment.transform.localPosition += Vector3.up * lift;
            if (required) attachment.transform.localPosition -= new Vector3(placement.contactPoint.x, placement.contactPoint.y, 0f);
        }
        var old = attachment.transform.Find("RequiredSightRiser");
        if (old)
        {
            EquipmentVisualPool.Return(old.gameObject);
        }
        // An explicitly equipped riser already supplies the support mesh.
        if (!required || sightLift > 0f) return;
        var support = EquipmentVisualPool.Rent(required.prefab, attachment.transform);
        support.name = "RequiredSightRiser";
        support.transform.SetLocalPositionAndRotation(required.positionOffset + Vector3.forward * placement.contactPoint.z - attachment.transform.localPosition,
            Quaternion.Euler(required.rotationOffset));
        support.transform.localScale = Vector3.Scale(required.scale, placement.supportScale);
        foreach (var node in support.GetComponentsInChildren<Transform>(true))
            node.gameObject.layer = attachment.gameObject.layer;
        bool visible = mount != firstPersonMount || owner.firstPerson;
        foreach (var renderer in support.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
            if (mount == firstPersonMount) renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    public void GetAdsPlacement(Vector3 rootScale, out Vector3 position, out Quaternion rotation)
    {
        // Sight markers share the weapon root coordinate space, regardless of parent/socket edits.
        // The saved neutral weapon pose preserves authored sway and recoil.
        var point = ActiveAimPoint;
        var view = loadout.FirstPersonView;
        var firstPersonWeaponRoot = view.sightPlacementRoot ? view.sightPlacementRoot : view.transform;
        Vector3 localPoint = firstPersonWeaponRoot.InverseTransformPoint(point.position);
        Vector3 localFront = firstPersonWeaponRoot.InverseTransformPoint(ActiveFrontAimPoint.position);
        Vector3 localUp = firstPersonWeaponRoot.InverseTransformDirection(point.up);
        Quaternion localRotation = Quaternion.LookRotation(localFront - localPoint, localUp);
        Vector3 referencePoint = referenceWeaponPosition + referenceWeaponRotation * Vector3.Scale(referenceWeaponScale, localPoint);
        Quaternion referenceRotation = referenceWeaponRotation * localRotation;
        rotation = referenceCameraRotation * Quaternion.Inverse(referenceRotation);
        Vector3 target = referenceCameraPosition + referenceCameraRotation * Vector3.forward * EyeDistance;
        position = target - rotation * Vector3.Scale(rootScale, referencePoint);
    }
}
