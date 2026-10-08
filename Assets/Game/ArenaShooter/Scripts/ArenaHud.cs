using UnityEngine;

// Minimal local feedback and test controls. No match rules live in the HUD.
public class ArenaHud : MonoBehaviour
{
    public ArenaMatch match;
    public ArenaParticipant player;
    private string lastShot = "";

    void OnEnable() => player.player.actions.shooting.ShotResolved += OnShot;
    void OnDisable() => player.player.actions.shooting.ShotResolved -= OnShot;

    void OnShot(SoldierHitscan.ShotResult shot) => lastShot = shot.killed ? "Kill" : shot.damaged ? "Hit"
        : shot.collider ? "No damage: " + shot.collider.name : "Miss";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Backspace)) { match.BeginMatch(); lastShot = ""; }
        // Local prototype death/respawn check until there are real opponents.
        if (Input.GetKeyDown(KeyCode.K)) player.health.ApplyDamage(player.health.maxHealth, player.gameObject);
    }

    void OnGUI()
    {
        var loadout = player.player.loadout;
        var ammo = loadout.EquippedState;
        GUI.Label(new Rect(Screen.width / 2f - 5f, Screen.height / 2f - 10f, 20, 20), "+");
        GUI.Box(new Rect(12, 12, 400, 200), "Arena test: " + player.team);
        GUI.Label(new Rect(24, 40, 375, 25), "Red " + match.RedScore + " : " + match.BlueScore + " Blue     " +
            Mathf.CeilToInt(match.TimeRemaining) + "s");
        GUI.Label(new Rect(24, 65, 375, 25), "Health " + player.health.CurrentHealth + "   " +
            loadout.EquippedWeapon.definition.name + "   " + ammo.MagazineAmmo + " / " + ammo.ReserveAmmo);
        GUI.Label(new Rect(24, 90, 375, 25), lastShot);
        string status = !match.IsRunning ? match.Result + " — Backspace to restart"
            : player.IsRespawning ? "Respawn in " + player.RespawnRemaining.ToString("F1") + "s" : "";
        GUI.Label(new Rect(24, 115, 375, 25), status);
        GUI.Label(new Rect(24, 145, 375, 25), "1/2 weapons | R reload | V FPS/TPS | K test death");
        GUI.Label(new Rect(24, 170, 375, 25), "Backspace restarts. Friendly target cannot be damaged.");
    }
}
