using UnityEngine;
using UnityEngine.Serialization;

public class WeaponSightAttachment : MonoBehaviour
{
    [Tooltip("Rear sight / eye reference. Its up axis defines the sight's roll. Leave empty to keep the weapon's rear reference.")]
    [FormerlySerializedAs("aimPoint")] public Transform rearSight;
    [Tooltip("Forward point on the sight line. Leave empty to keep the weapon's front reference.")]
    public Transform frontSight;
    public Renderer scopeLens;
    [Header("Alternate built-in sight")]
    [Tooltip("Optional top iron/red-dot sight line. Both references are required to toggle aiming without replacing this optic.")]
    public Transform alternateRearSight, alternateFrontSight;
    [Min(.03f)] public float alternateEyeDistance = .2f;
    [Range(1f,90f)] public float alternateFieldOfView = 50f;
    [Min(.01f)] public float alternateSensitivityMultiplier = 1f;
    public bool HasAlternateSight => alternateRearSight && alternateFrontSight
        && (alternateFrontSight.position-alternateRearSight.position).sqrMagnitude > .000001f;
    // Selection-time fitting only; glass and reticle planes are not mounting feet.
    public static float MountingBaseY(Transform optic)
    {
        float bottom = float.PositiveInfinity;
        var requiredSupport = optic.Find("RequiredSightRiser");
        foreach (var renderer in optic.GetComponentsInChildren<Renderer>(true))
        {
            if (requiredSupport && renderer.transform.IsChildOf(requiredSupport)) continue;
            string name = renderer.name.ToLowerInvariant();
            if (name.Contains("glass") || name.Contains("lens") || name.Contains("reticle") || name == "point") continue;
            if (renderer.sharedMaterial && renderer.sharedMaterial.shader.name == "Game/Scope Surface") continue;
            var bounds = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                bottom = Mathf.Min(bottom, optic.parent.InverseTransformPoint(renderer.transform.TransformPoint(corner)).y);
            }
        }
        return float.IsPositiveInfinity(bottom) ? optic.localPosition.y : bottom;
    }
#if UNITY_EDITOR
    // Lets the placement tool save a runtime attachment back to its authoring prefab.
    public WeaponSightAttachment SourcePrefab { get; set; }
#endif
}
