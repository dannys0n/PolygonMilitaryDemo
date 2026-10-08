using UnityEngine;

[CreateAssetMenu(menuName = "Arena Shooter/Match settings")]
public class ArenaMatchSettings : ScriptableObject
{
    [Min(1)] public int scoreLimit = 10;
    [Min(1f)] public float matchDuration = 300f;
    [Min(.1f)] public float respawnDelay = 3f;
    public bool friendlyFire;
    [Tooltip("Indices in the shared player's equipment catalog. First entry is the starting weapon.")]
    public int[] redLoadout = { 0, 5 };
    public int[] blueLoadout = { 0, 5 };
}
