using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;

// Read-only presentation of arena/player state; no combat rules live in the HUD.
public class ArenaHud : MonoBehaviour
{
    public ArenaMatch match;
    public ArenaParticipant player;
    public TMP_Text redScore, blueScore, timer, mode, weaponName, ammunition, fireMode, slots, health, teammates, feed, medal, deathStatus;
    public UnityEngine.UI.Image redProgress, blueProgress, healthProgress;
    public RectTransform[] crosshairLines;
    public CanvasGroup crosshair, hitMarker, medalGroup, radar;
    public GameObject rifleIcon, pistolIcon;
    public RectTransform radarRoot;
    public RectTransform radarDotTemplate;
    public Color accent = new Color(1f, .686f, 0f);
    public float radarRange = 45f;
    readonly List<(string text, float expires)> kills = new List<(string,float)>();
    readonly List<RectTransform> radarDots = new List<RectTransform>();
    float shotKick, hitUntil, medalUntil, nextRefresh, lastKill = -100f;
    int streak, multiKill;
    string accentHex;

    void Awake() => accentHex = ColorUtility.ToHtmlStringRGB(accent);
    void OnEnable()
    {
        match.KillRecorded += OnKill; match.MatchStarted += ResetFeedback;
        player.player.actions.shooting.ShotResolved += OnShot;
        player.player.actions.FireStarted += OnFire;
        player.player.Respawned += OnRespawn;
    }
    void OnDisable()
    {
        match.KillRecorded -= OnKill; match.MatchStarted -= ResetFeedback;
        player.player.actions.shooting.ShotResolved -= OnShot;
        player.player.actions.FireStarted -= OnFire;
        player.player.Respawned -= OnRespawn;
    }
    void OnRespawn() { streak = multiKill = 0; shotKick = 0f; }
    void ResetFeedback() { kills.Clear(); feed.text = ""; medalUntil = hitUntil = 0f; lastKill = -100f; OnRespawn(); }
    void OnFire() => shotKick = Mathf.Min(20f, shotKick + 7f);
    void OnShot(SoldierHitscan.ShotResult shot)
    {
        if (!shot.damaged) return;
        hitUntil = Time.time + .16f;
        foreach (var image in hitMarker.GetComponentsInChildren<UnityEngine.UI.Image>()) image.color = shot.killed ? accent : Color.white;
    }
    void OnKill(ArenaParticipant attacker, ArenaParticipant victim)
    {
        string weapon = attacker.player ? attacker.player.loadout.slots[attacker.player.loadout.selectedSlot].name : "--";
        string a = Escape(attacker.name), v = Escape(victim.name);
        kills.Insert(0, ($"{a}  <color=#{accentHex}>[{weapon}]</color>  {v}", Time.time + 7f));
        if (kills.Count > 5) kills.RemoveAt(kills.Count - 1);
        nextRefresh = 0f;
        if (attacker != player) return;
        streak++;
        multiKill = Time.time - lastKill < 3f ? multiKill + 1 : 1; lastKill = Time.time;
        string title = multiKill >= 3 ? "MULTI KILL" : multiKill == 2 ? "DOUBLE KILL" : "ENEMY ELIMINATED";
        medal.text = $"<color=#{accentHex}>{title}</color>\n{v}  /  {streak} KILL STREAK";
        medalUntil = Time.time + 2.5f;
    }
    static string Escape(string value) => value.Replace("<", "").Replace(">", "");
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Backspace)) match.BeginMatch();
        if (Input.GetKeyDown(KeyCode.K)) player.health.ApplyDamage(player.health.maxHealth, player.gameObject);
        shotKick = Mathf.MoveTowards(shotKick, 0f, Time.deltaTime * 25f);
        var life = player.player;
        float movement = new Vector2(life.controller.velocity.x, life.controller.velocity.z).magnitude;
        float spread = 7f + Mathf.Clamp(movement * 2f, 0f, 10f) + shotKick;
        crosshairLines[0].anchoredPosition = new Vector2(-spread, 0);
        crosshairLines[1].anchoredPosition = new Vector2(spread, 0);
        crosshairLines[2].anchoredPosition = new Vector2(0, spread);
        crosshairLines[3].anchoredPosition = new Vector2(0, -spread);
        crosshair.alpha = player.health.IsDead || life.movement.IsRunning ? 0f : 1f - life.view.AimBlend;
        hitMarker.alpha = Time.time < hitUntil ? 1f : 0f;
        medalGroup.alpha = Mathf.Clamp01((medalUntil - Time.time) * 2f);
        radar.alpha = player.health.IsDead ? .3f : 1f;
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + .1f;
        UpdateReadouts(); UpdateRadar();
    }
    void UpdateReadouts()
    {
        redScore.text = match.RedScore.ToString("000"); blueScore.text = match.BlueScore.ToString("000");
        int seconds = Mathf.CeilToInt(match.TimeRemaining);
        timer.text = $"{seconds / 60:00}:{seconds % 60:00}";
        mode.text = $"TEAM DEATHMATCH  /  FIRST TO {match.settings.scoreLimit}";
        redProgress.fillAmount = Mathf.Clamp01((float)match.RedScore / match.settings.scoreLimit);
        blueProgress.fillAmount = Mathf.Clamp01((float)match.BlueScore / match.settings.scoreLimit);
        var loadout = player.player.loadout; var ammo = loadout.EquippedState;
        weaponName.text = loadout.slots[loadout.selectedSlot].name.ToUpperInvariant();
        ammunition.text = $"{ammo.MagazineAmmo:00}<size=24><color=#B9C0C5> / {ammo.ReserveAmmo:000}</color></size>";
        fireMode.text = player.player.actions.IsReloading ? "RELOADING" : player.player.actions.fireMode == SoldierActions.FireMode.Automatic ? "AUTO" : "SEMI";
        int ordinal = System.Array.IndexOf(loadout.AvailableSlots, loadout.selectedSlot);
        rifleIcon.SetActive(ordinal == 0); pistolIcon.SetActive(ordinal > 0);
        var slotText = new StringBuilder();
        for (int i = 0; i < loadout.AvailableSlots.Length; i++)
        {
            if (i > 0) slotText.Append("    ");
            string label = $"[{i+1}] {loadout.slots[loadout.AvailableSlots[i]].name}";
            slotText.Append(i == ordinal ? $"<color=#{accentHex}>{label}</color>" : label);
        }
        slots.text = slotText.ToString();
        health.text = $"+ {Mathf.CeilToInt(player.health.CurrentHealth):000}";
        healthProgress.fillAmount = player.health.CurrentHealth / player.health.maxHealth;
        var squad = match.participants.Where(p => p.team == player.team && p != player)
            .OrderBy(p => (p.transform.position-player.transform.position).sqrMagnitude).Take(3);
        teammates.text = string.Join("\n", squad.Select((p,i) => $"{i+1:00}  {Escape(p.name)}  <color=#{accentHex}>{(p.health.IsDead ? "DOWN" : Mathf.CeilToInt(p.health.CurrentHealth).ToString())}</color>"));
        kills.RemoveAll(k => k.expires < Time.time);
        feed.text = string.Join("\n", kills.Select(k => k.text));
        deathStatus.text = !match.IsRunning ? $"{match.Result.ToUpperInvariant()}\nBACKSPACE TO RESTART"
            : player.IsRespawning ? $"RESPAWN IN {player.RespawnRemaining:F1}s" : "";
    }
    void UpdateRadar()
    {
        var allies = match.participants.Where(p => p != player && p.team == player.team && !p.health.IsDead && !p.IsAwaitingSpawn).ToArray();
        while (radarDots.Count < allies.Length)
        {
            var dot = Instantiate(radarDotTemplate, radarRoot, false); dot.gameObject.SetActive(true); radarDots.Add(dot);
        }
        float radius = radarRoot.rect.width * .43f;
        var orientation = Quaternion.Inverse(Quaternion.Euler(0, player.transform.eulerAngles.y, 0));
        for (int i = 0; i < radarDots.Count; i++)
        {
            bool visible = i < allies.Length;
            if (visible)
            {
                var delta = orientation * (allies[i].transform.position-player.transform.position);
                var offset = new Vector2(delta.x, delta.z) / radarRange * radius;
                visible = offset.magnitude <= radius;
                radarDots[i].anchoredPosition = offset;
            }
            radarDots[i].gameObject.SetActive(visible);
        }
    }
}
