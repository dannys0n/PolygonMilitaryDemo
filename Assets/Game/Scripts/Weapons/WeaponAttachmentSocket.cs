using UnityEngine;

// Optional receiver-specific placements relative to this attachment socket.
// The default socket pose still handles attachments without a calibration.
public class WeaponAttachmentSocket : MonoBehaviour
{
    [System.Serializable] public struct Placement
    {
        public WeaponAttachmentDefinition attachment;
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
}
