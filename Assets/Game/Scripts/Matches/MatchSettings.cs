using UnityEngine;

[CreateAssetMenu(menuName = "Game/Match settings")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaMatchSettings")]
public class MatchSettings : ScriptableObject
{
    [Min(1)] public int scoreLimit = 10;
    [Min(1f)] public float matchDuration = 300f;
    [Min(.1f)] public float respawnDelay = 3f;
    public bool friendlyFire;
    [Header("Respawn safety")]
    [Min(0f)] public float unsafeEnemyDistance = 8f;
    [Min(.5f)] public float spawnClearance = 1.2f;
    [Min(1f)] public float spawnSearchRadius = 12f;
    [Min(0f)] public float recentSpawnTime = 8f;
    [Header("Opening team placement")]
    [Min(2f)] public float openingTeamRadius = 6f;
    [Tooltip("Keep the opening roster spread out so agents can leave HQ without a packed avoidance queue.")]
    [Min(1f)] public float openingSpacing = 2.4f;
    [Min(0f)] public float openingTeamSeparation = 30f;
    [Header("Equipment source") ]
    public WeaponCatalog weaponCatalog;
    public bool polishedWeaponsOnly;
}
