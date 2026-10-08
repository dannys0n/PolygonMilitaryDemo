using UnityEngine;

// Temporary feedback for the local gameplay test, separate from shared player logic.
public class ArenaTargetPractice : MonoBehaviour
{
    public SoldierWeaponLoadout player;
    public SoldierHealth[] targets;
    private string lastShot = "Aim at a target and fire.";

    void OnEnable() => player.actions.shooting.ShotResolved += OnShot;
    void OnDisable() => player.actions.shooting.ShotResolved -= OnShot;

    void OnShot(SoldierHitscan.ShotResult shot) => lastShot = shot.collider
        ? (shot.damaged ? "Hit: " : "Blocked: ") + shot.collider.name : "Miss";

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.T)) return;
        foreach (SoldierHealth target in targets) target.ResetHealth();
        player.ResetAmmunition();
        lastShot = "Targets and ammo reset.";
    }

    void OnGUI()
    {
        var state = player.EquippedState;
        GUI.Label(new Rect(Screen.width / 2f - 5f, Screen.height / 2f - 10f, 20f, 20f), "+");
        GUI.Box(new Rect(12, 12, 370, 180), "Target practice");
        GUI.Label(new Rect(24, 40, 350, 25), player.EquippedWeapon.definition.name +
            "   Ammo: " + state.MagazineAmmo + " / " + state.ReserveAmmo);
        GUI.Label(new Rect(24, 65, 350, 25), lastShot);
        for (int i = 0; i < targets.Length; i++)
            GUI.Label(new Rect(24, 90 + i * 22, 350, 25), targets[i].name + ": " + targets[i].CurrentHealth);
        GUI.Label(new Rect(24, 158, 350, 25), "R reload  |  V FPS/TPS  |  1–7 weapons  |  T reset");
    }
}
