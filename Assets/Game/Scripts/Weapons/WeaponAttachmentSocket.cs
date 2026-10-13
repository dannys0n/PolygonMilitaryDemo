using UnityEngine;

// Optional receiver-specific placements relative to this attachment socket.
// The default socket pose still handles attachments without a calibration.
public class WeaponAttachmentSocket : MonoBehaviour
{
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
    public void SetBuiltInSightVisible(WeaponSightDefinition selected)
    {
        foreach(var renderer in builtInSightRenderers)
            if(renderer)renderer.forceRenderingOff=selected!=builtInSight;
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
