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
#if UNITY_EDITOR
    // Lets the placement tool save a runtime attachment back to its authoring prefab.
    public WeaponSightAttachment SourcePrefab { get; set; }
#endif
}
