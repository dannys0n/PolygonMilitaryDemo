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
    public bool allowLocalShortcuts = true;
    public TMP_Text redScore, blueScore, timer, mode, weaponName, ammunition, fireMode, slots, health, teammates, medal, deathStatus;
    public Sprite rifleSprite, pistolSprite;
    public TMP_Text[] killerNames, victimNames, equipmentKeys;
    public UnityEngine.UI.Image[] killWeapons, equipmentIcons, equipmentBackgrounds;
    public UnityEngine.UI.Image redProgress, blueProgress, healthProgress;
    public RectTransform crosshairRoot;
    public UnityEngine.UI.RawImage[] crosshairSegments;
    public float crosshairSmoothing = 8f;
    public float minimumCrosshairScale = .15f;
    [Header("Crosshair shot bloom")]
    public float shotExpansion = 5f;
    public float maximumShotExpansion = 20f;
    public float shotRecoveryDelay = .055f;
    public float shotRecoveryTime = .16f;
    public CanvasGroup crosshair, hitMarker, medalGroup, radar;
    public GameObject rifleIcon, pistolIcon;
    public RectTransform radarRoot;
    public RectTransform radarDotTemplate;
    public Color accent = new Color(1f, .686f, 0f);
    public float radarRange = 45f;
    readonly List<(string attacker, string victim, bool pistol, float expires)> kills = new List<(string,string,bool,float)>();
    readonly List<RectTransform> radarDots = new List<RectTransform>();
    float crosshairScale = 1f, hitUntil, medalUntil, nextRefresh, lastKill = -100f;
    float shotSpread, recoverSpreadAt;
    Vector2[] crosshairRestPositions;
    int streak, multiKill;
    string accentHex;

    void Awake()
    {
        accentHex = ColorUtility.ToHtmlStringRGB(accent);
        crosshairRestPositions = crosshairSegments.Select(s => s.rectTransform.anchoredPosition).ToArray();
    }
    void OnEnable()
    {
        match.KillRecorded += OnKill; match.MatchStarted += ResetFeedback;
        SubscribePlayer();
    }
    void SubscribePlayer()
    {
        player.player.actions.shooting.ShotResolved += OnShot;
        player.player.actions.FireStarted += OnFire;
        player.player.Respawned += OnRespawn;
    }
    void OnDisable()
    {
        match.KillRecorded -= OnKill; match.MatchStarted -= ResetFeedback;
        UnsubscribePlayer();
    }
    void UnsubscribePlayer()
    {
        player.player.actions.shooting.ShotResolved -= OnShot;
        player.player.actions.FireStarted -= OnFire;
        player.player.Respawned -= OnRespawn;
    }
    public void SetPlayer(ArenaParticipant character)
    {
        if (player == character) return;
        bool listening = isActiveAndEnabled;
        if (listening) UnsubscribePlayer();
        player = character;
        OnRespawn(); hitUntil = medalUntil = 0f; lastKill = -100f; nextRefresh = 0f;
        if (listening) SubscribePlayer();
    }
    void OnRespawn() { streak = multiKill = 0; shotSpread = 0f; }
    void OnFire()
    {
        if (player.player.view.WantsToAim) return;
        shotSpread = Mathf.Min(maximumShotExpansion, shotSpread + shotExpansion);
        recoverSpreadAt = Time.time + shotRecoveryDelay;
        PositionCrosshair();
    }
    void PositionCrosshair()
    {
        for (int i = 0; i < crosshairSegments.Length; i++)
            crosshairSegments[i].rectTransform.anchoredPosition = crosshairRestPositions[i]
                + crosshairRestPositions[i].normalized * shotSpread;
    }
    void ResetFeedback() { kills.Clear(); medalUntil = hitUntil = 0f; lastKill = -100f; OnRespawn(); }
    void OnShot(SoldierHitscan.ShotResult shot)
    {
        if (!shot.damaged) return;
        hitUntil = Time.time + .16f;
        foreach (var image in hitMarker.GetComponentsInChildren<UnityEngine.UI.Image>()) image.color = shot.killed ? accent : Color.white;
    }
    void OnKill(ArenaParticipant attacker, ArenaParticipant victim)
    {
        bool sidearm = attacker.player && System.Array.IndexOf(attacker.player.loadout.AvailableSlots, attacker.player.loadout.selectedSlot) > 0;
        string a = Escape(attacker.name), v = Escape(victim.name);
        kills.Insert(0, (a, v, sidearm, Time.time + 7f));
        if (kills.Count > 5) kills.RemoveAt(kills.Count - 1);
        nextRefresh = 0f;
        if (attacker != player) return;
        streak++;
        multiKill = Time.time - lastKill < 3f ? multiKill + 1 : 1; lastKill = Time.time;
        string title = multiKill >= 3 ? "MULTI KILL" : multiKill == 2 ? "DOUBLE KILL" : "KILL";
        medal.text = $"{title}  <size=30>+100</size>\n<color=#{accentHex}>{v}</color>  <size=16>KILL x{streak}</size>";
        medalUntil = Time.time + 2.5f;
    }
    static string Escape(string value) => value.Replace("<", "").Replace(">", "");
    void Update()
    {
        if (allowLocalShortcuts && Input.GetKeyDown(KeyCode.Backspace)) match.BeginMatch();
        if (allowLocalShortcuts && player.GetComponent<SoldierLocalInput>() && Input.GetKeyDown(KeyCode.K))
            player.health.ApplyDamage(player.health.maxHealth, player.gameObject);
        var life = player.player;
        // Keep AtlasNet's centered, smoothed visibility; shot bloom moves the arms independently.
        bool visible = !player.health.IsDead && !life.view.WantsToAim
            && !life.movement.IsRunning && !life.loadout.IsSwitching;
        crosshairScale = Mathf.Lerp(crosshairScale, visible ? 1f : 0f, Time.deltaTime * crosshairSmoothing);
        crosshairRoot.localScale = Vector3.one * crosshairScale;
        if (life.view.WantsToAim) shotSpread = 0f;
        else if (Time.time >= recoverSpreadAt)
            shotSpread = Mathf.Lerp(shotSpread, 0f, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(.01f, shotRecoveryTime)));
        PositionCrosshair();
        foreach (var segment in crosshairSegments) segment.enabled = crosshairScale > minimumCrosshairScale;
        crosshair.alpha = 1f;
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
        for (int i = 0; i < equipmentIcons.Length; i++)
        {
            bool owned = i < loadout.AvailableSlots.Length;
            equipmentIcons[i].transform.parent.gameObject.SetActive(owned);
            if (!owned) continue;
            equipmentIcons[i].sprite = i == 0 ? rifleSprite : pistolSprite;
            equipmentIcons[i].color = i == ordinal ? Color.white : new Color(1,1,1,.6f);
            equipmentKeys[i].color = i == ordinal ? accent : Color.white;
            equipmentBackgrounds[i].color = i == ordinal ? new Color(accent.r,accent.g,accent.b,.12f) : new Color(1,1,1,.035f);
        }
        health.text = $"+ {Mathf.CeilToInt(player.health.CurrentHealth):000}";
        healthProgress.fillAmount = player.health.CurrentHealth / player.health.maxHealth;
        var squad = match.participants.Where(p => p.team == player.team && p != player)
            .OrderBy(p => (p.transform.position-player.transform.position).sqrMagnitude).Take(3);
        teammates.text = string.Join("\n", squad.Select((p,i) => $"{i+1:00}  {Escape(p.name)}  <color=#{accentHex}>{(p.health.IsDead ? "DOWN" : Mathf.CeilToInt(p.health.CurrentHealth).ToString())}</color>"));
        kills.RemoveAll(k => k.expires < Time.time);
        for (int i = 0; i < killWeapons.Length; i++)
        {
            bool visible = i < kills.Count;
            killWeapons[i].transform.parent.gameObject.SetActive(visible);
            if (!visible) continue;
            killerNames[i].text = kills[i].attacker; victimNames[i].text = kills[i].victim;
            killWeapons[i].sprite = kills[i].pistol ? pistolSprite : rifleSprite;
        }
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
