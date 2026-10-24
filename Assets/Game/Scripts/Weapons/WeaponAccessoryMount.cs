using UnityEngine;

// Optional receiver hardware shared by specific light/laser attachments.
public class WeaponAccessoryMount : MonoBehaviour
{
    public WeaponAttachmentDefinition[] accessories = System.Array.Empty<WeaponAttachmentDefinition>();
    public Renderer[] renderers = System.Array.Empty<Renderer>();
    public void SetVisible(bool visible)
    {
        foreach (var renderer in renderers)
            if (renderer) renderer.forceRenderingOff = !visible;
    }
}
