using UnityEngine;

// Optional receiver-specific placements relative to this attachment socket.
// The default socket pose still handles attachments without a calibration.
public class WeaponAttachmentSocket : MonoBehaviour
{
    [Header("Optic compatibility")]
    [Tooltip("Interfaces provided by this receiver and its authored optic-mount hardware.")]
    public WeaponSightDefinition.MountStandard supportedSightMounts = WeaponSightDefinition.MountStandard.Picatinny;
    public bool SupportsSight(WeaponSightDefinition sight)
    {
        if(!sight || !sight.prefab)return true;
        if(sight.mountStandard==WeaponSightDefinition.MountStandard.Dedicated)return sight==builtInSight;
        return (supportedSightMounts & sight.mountStandard)!=0;
    }
    [System.Serializable] public struct Placement
    {
        public WeaponAttachmentDefinition attachment;
        [Tooltip("Optional optic target. Set either attachment or sight for this placement.")]
        public WeaponSightDefinition sight;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }
    [System.Serializable] public struct SightSupport
    {
        public WeaponSightDefinition sight;
        public WeaponAttachmentDefinition riser;
        public Vector3 contactPoint;
        public Vector3 supportScale;
    }
    public SightSupport[] requiredSightSupports = System.Array.Empty<SightSupport>();

    public SightSupport SupportFor(WeaponSightDefinition sight)
    {
        foreach (var support in requiredSightSupports)
            if (support.sight == sight) return support;
        return default;
    }

    public WeaponAttachmentDefinition RequiredRiser(WeaponSightDefinition sight)
    {
        foreach (var support in requiredSightSupports)
            if (support.sight == sight) return support.riser;
        return null;
    }

    public Placement[] placements = System.Array.Empty<Placement>();
    [Header("Built-in optic housing")]
    public WeaponSightDefinition builtInSight;
    public Renderer[] builtInSightRenderers = System.Array.Empty<Renderer>();
    [Header("Optic mounting hardware")]
    [Tooltip("Receiver rails or bridges shown only while a rail-mounted optic is equipped.")]
    public Renderer[] opticMountRenderers = System.Array.Empty<Renderer>();
    [Tooltip("Side-mounted optics which bypass this weapon's top rail or bridge.")]
    public WeaponSightDefinition[] sideMountSights = System.Array.Empty<WeaponSightDefinition>();
    public void SetBuiltInSightVisible(WeaponSightDefinition selected)
    {
        foreach(var renderer in builtInSightRenderers)
            if(renderer)renderer.forceRenderingOff=selected!=builtInSight;
        bool showMount = selected && selected.prefab && selected.mountStandard!=WeaponSightDefinition.MountStandard.AKSideRail && System.Array.IndexOf(sideMountSights, selected) < 0;
        foreach (var renderer in opticMountRenderers)
            if (renderer) renderer.forceRenderingOff = !showMount;
    }

    public void Place(WeaponAttachmentDefinition attachment, Transform instance)
    {
        foreach (var placement in placements)
        {
            if (placement.attachment != attachment) continue;
            instance.SetLocalPositionAndRotation(placement.position, placement.rotation);
            instance.localScale = placement.scale;
            return;
        }
    }
    public void Place(WeaponSightDefinition sight, Transform instance)
    {
        foreach(var placement in placements)
        {
            if(!placement.sight || placement.sight!=sight)continue;
            instance.SetLocalPositionAndRotation(placement.position,placement.rotation);
            instance.localScale=placement.scale;
            return;
        }
    }
}
