using UnityEngine;
using UnityEngine.Serialization;

public class WeaponSightAttachment : MonoBehaviour
{
    [Tooltip("Rear sight / eye reference. Its up axis defines the sight's roll. Leave empty to keep the weapon's rear reference.")]
    [FormerlySerializedAs("aimPoint")] public Transform rearSight;
    [Tooltip("Forward point on the sight line. Leave empty to keep the weapon's front reference.")]
    public Transform frontSight;
    public Renderer scopeLens;
#if UNITY_EDITOR
    // Lets the placement tool save a runtime attachment back to its authoring prefab.
    public WeaponSightAttachment SourcePrefab { get; set; }
#endif
}
