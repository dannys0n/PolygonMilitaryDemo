using UnityEngine;
[CreateAssetMenu(menuName="Soldier/Weapon attachment")]
public class WeaponAttachmentDefinition : ScriptableObject
{
    public enum MountKind { Muzzle, Rail, Grip, RearIron, FrontIron, SightRiser, Handguard, RailLeft }
    public string id,displayName;
    public MountKind mount;
    public GameObject prefab;
    public Vector3 positionOffset,rotationOffset;
    public Vector3 scale=Vector3.one;
    [Min(0)] public float sightLift;
}
