using UnityEngine;

[CreateAssetMenu(menuName = "Soldier/Weapon sight")]
public class WeaponSightDefinition : ScriptableObject
{
    [System.Flags] public enum MountStandard { None=0, Picatinny=1, AKSideRail=2, SUSAT=4, Dedicated=8 }
    [Tooltip("Mechanical interface on this optic. A matching receiver socket or its authored adapter is required.")]
    public MountStandard mountStandard = MountStandard.Picatinny;
    public enum RailFootprint { Compact, Standard, Long }
    [Tooltip("Mounting space and housing clearance required by this optic, independently of magnification.")]
    public RailFootprint railFootprint = RailFootprint.Standard;
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
