using UnityEngine;

[CreateAssetMenu(menuName = "Soldier/Weapon sight")]
public class WeaponSightDefinition : ScriptableObject
{
    public string id;
    public string displayName;
    [Tooltip("Leave empty to use the weapon's built-in iron sight point.")]
    public WeaponSightAttachment prefab;
    [Min(.03f)] public float eyeDistance = .35f;
    [Range(20f, 90f)] public float fieldOfView = 50f;
    [Min(.01f)] public float sensitivityMultiplier = 1f;
}
