using UnityEngine;

[CreateAssetMenu(menuName = "Arena Shooter/Match settings")]
public class ArenaMatchSettings : ScriptableObject
{
    [Min(1)] public int scoreLimit = 10;
    [Min(1f)] public float matchDuration = 300f;
    [Min(.1f)] public float respawnDelay = 3f;
    public bool friendlyFire;
    [Header("Respawn safety")]
    [Min(0f)] public float unsafeEnemyDistance = 8f;
    [Min(.5f)] public float spawnClearance = 1.2f;
    [Min(0f)] public float recentSpawnTime = 8f;
    [Header("Opening team placement")]
    [Min(2f)] public float openingTeamRadius = 6f;
    [Min(0f)] public float openingTeamSeparation = 30f;
    public ArenaClassDefinition[] classes;
}
