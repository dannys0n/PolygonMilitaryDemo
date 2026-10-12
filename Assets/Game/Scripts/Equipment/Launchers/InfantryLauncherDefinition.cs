using UnityEngine;

[CreateAssetMenu(menuName="Game/Equipment/Infantry Launcher")]
public class InfantryLauncherDefinition : ScriptableObject
{
    public string displayName;
    public InfantryLauncherVisual model;
    public VehicleRocket projectile;
    public Sprite icon;
    [Min(1)] public int startingRounds=3;
    [Min(.1f)] public float rearmTime=2f;
    // Relative to the existing rifle muzzle frame. Art faces +Z.
    public Vector3 holdPosition=new Vector3(0,-.08f,-.5f);
    public Vector3 holdRotation;
    [Min(.02f)] public float sightEyeDistance=.18f;
    [Range(10f,100f)] public float aimFieldOfView=55f;
}
