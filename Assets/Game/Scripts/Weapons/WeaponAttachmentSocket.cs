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
