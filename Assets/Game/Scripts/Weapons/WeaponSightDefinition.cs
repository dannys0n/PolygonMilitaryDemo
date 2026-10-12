using UnityEngine;

[CreateAssetMenu(menuName = "Soldier/Weapon sight")]
public class WeaponSightDefinition : ScriptableObject
{
    public string id;
    public string displayName;
    [Tooltip("Leave empty to use the weapon's built-in iron sight point.")]
    public WeaponSightAttachment prefab;
    [Min(.03f)] public float eyeDistance = .35f;
    [Range(1f, 90f)] public float fieldOfView = 50f;
    [Min(.01f)] public float sensitivityMultiplier = 1f;
    [Header("Picture-in-picture optics")]
    public bool pictureInPicture;
    [Tooltip("Optical image scale relative to the surrounding ADS view. 1x is unmagnified; lens size is accounted for automatically.")]
    [Range(1f,20f)] public float magnification=4f;
    [Range(128,1024)] public int scopeTextureSize=512;
}