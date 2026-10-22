using UnityEngine;

// Test-only results of converting an existing weapon to the shared UE4 skeleton.
[CreateAssetMenu(menuName="Soldier/Animation conversion proof")]
public sealed class WeaponAnimationConversionProof : ScriptableObject
{
    [HideInInspector] public RuntimeAnimatorController thirdPersonSourceController, thirdPersonSourceParts;
    public WeaponCatalog.Entry source, converted;
    public bool originalProportions;
    [HideInInspector] public string comparisonFingerprint;
    public GameObject thirdPersonProof;
    public GameObject sharedFirstPersonRig, sharedBodyPrefab, sharedBodyWeaponPrefab;
    public Vector3 bodyWeaponPosition, bodyWeaponEuler, bodyWeaponScale=Vector3.one;
    public AnimationClip idle, reload, fire, aimFire, bodyIdle, bodyReload;
}
