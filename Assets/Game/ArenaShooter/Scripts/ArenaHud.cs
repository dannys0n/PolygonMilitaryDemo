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
    public Material normalText, selectedText;
    public UnityEngine.UI.Image[] grenadeIcons, grenadeBackgrounds;
    public TMP_Text[] grenadeCounts;
    public UnityEngine.UI.Image[] launcherIcons=System.Array.Empty<UnityEngine.UI.Image>(), launcherBackgrounds=System.Array.Empty<UnityEngine.UI.Image>();
    public TMP_Text[] launcherCounts=System.Array.Empty<TMP_Text>();
    public UnityEngine.UI.Image flashOverlay;
    public TMP_Text[] killerNames, victimNames, equipmentKeys;
    public UnityEngine.UI.Image[] killWeapons, equipmentIcons, equipmentBackgrounds;
    public UnityEngine.UI.Image redProgress, blueProgress, healthProgress;
    public RectTransform crosshairRoot;
    public UnityEngine.UI.Graphic[] crosshairSegments;
    public UnityEngine.UI.Image[] spreadCrosshairSegments;
    public float crosshairSmoothing = 8f;
    public float minimumCrosshairScale = .15f;
    [Header("Crosshair shot bloom")]
    public float shotExpansion = 5f;
    public float maximumShotExpansion = 20f;
    public float shotRecoveryDelay = .055f;
    public float shotRecoveryTime = .16f;
    [Header("Hit and kill feedback")]
    public float hitMarkerDuration = .24f;
    public float killSkullDuration = 5f;
    public CanvasGroup[] killSkulls;
    public CanvasGroup crosshair, hitMarker, medalGroup, radar;
    public GameObject rifleIcon, pistolIcon;
    public RectTransform radarRoot;
    public RectTransform radarDotTemplate;
    public Color accent = new Color(1f, .686f, 0f);
    public float radarRange = 45f;
    readonly List<(string attacker, string victim, Sprite weapon, float expires)> kills = new List<(string,string,Sprite,float)>();
    readonly List<float> skullExpires = new List<float>();
    readonly List<RectTransform> radarDots = new List<RectTransform>();
    float crosshairScale = 1f, hitUntil, medalUntil, nextRefresh, lastKill = -100f;
    float shotSpread, recoverSpreadAt, killHitUntil;
    Vector2[] crosshairRestPositions, spreadRestPositions;
    int streak, multiKill;
    string accentHex;

    void Awake()
    {
        accentHex = ColorUtility.ToHtmlStringRGB(accent);
        crosshairRestPositions = crosshairSegments.Select(s => s.rectTransform.anchoredPosition).ToArray();
        spreadRestPositions = spreadCrosshairSegments.Select(s => s.rectTransform.anchoredPosition).ToArray();
    }
    void OnEnable()
    {
        match.KillRecorded += OnKill; match.MatchStarted += ResetFeedback;
        CombatEvents.DamageConfirmed += OnConfirmedDamage;
        SubscribePlayer();
    }
    void SubscribePlayer()
    {
        player.player.actions.FireStarted += OnFire;
        var utility=player.player.GetComponent<SoldierLaunchers>(); if(utility)utility.Fired+=OnFire;
        player.player.Respawned += OnRespawn;
    }
    void OnDisable()
    {
        match.KillRecorded -= OnKill; match.MatchStarted -= ResetFeedback;
        CombatEvents.DamageConfirmed -= OnConfirmedDamage;
        UnsubscribePlayer();
    }
    void UnsubscribePlayer()
    {
        player.player.actions.FireStarted -= OnFire;
        var utility=player.player.GetComponent<SoldierLaunchers>(); if(utility)utility.Fired-=OnFire;
        player.player.Respawned -= OnRespawn;
    }
    public void SetPlayer(ArenaParticipant character)
    {
        if (player == character) return;
        bool listening = isActiveAndEnabled;
        if (listening) UnsubscribePlayer();
        player = character;
        OnRespawn(); hitUntil = medalUntil = killHitUntil = 0f; lastKill = -100f; nextRefresh = 0f;
        if (listening) SubscribePlayer();
    }
    void OnRespawn() { streak = multiKill = 0; shotSpread = 0f; skullExpires.Clear(); medalUntil = 0f; }
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
        for (int i = 0; i < spreadCrosshairSegments.Length; i++)
            spreadCrosshairSegments[i].rectTransform.anchoredPosition = spreadRestPositions[i]
                + spreadRestPositions[i].normalized * shotSpread;
    }
    void ResetFeedback() { kills.Clear(); medalUntil = hitUntil = killHitUntil = 0f; lastKill = -100f; OnRespawn(); }
    void OnConfirmedDamage(CombatEvents.ConfirmedDamage hit)
    {
        if(hit.attacker!=player.gameObject || hit.target==player.gameObject)return;
        hitUntil = Time.time + hitMarkerDuration;
        if(hit.killed)killHitUntil=hitUntil;
        foreach (var image in hitMarker.GetComponentsInChildren<UnityEngine.UI.Image>()) image.color = Time.time<killHitUntil ? accent : Color.white;
    }
    void OnKill(ArenaParticipant attacker, ArenaParticipant victim)
    {
        Sprite weapon = attacker.player ? attacker.player.loadout.EquippedWeapon.definition.icon : rifleSprite;
        var utility=attacker.GetComponent<SoldierLaunchers>();
        // Projectiles can land after the shooter has returned to the rifle.
        if(utility && utility.LastShotTime>=attacker.LastShotTime-.01f && Time.time-utility.LastShotTime<8f)weapon=utility.LastShotIcon;
        string a = Escape(attacker.name), v = Escape(victim.name);
        kills.Insert(0, (a, v, weapon, Time.time + 7f));
        if (kills.Count > 5) kills.RemoveAt(kills.Count - 1);
        nextRefresh = 0f;
        if (attacker != player) return;
        skullExpires.RemoveAll(expiry => expiry <= Time.time);
        if (skullExpires.Count == killSkulls.Length) skullExpires.RemoveAt(0);
        skullExpires.Add(Time.time + killSkullDuration);
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
        var grenades=life.GetComponent<SoldierGrenades>();
        var flashColor=flashOverlay.color;flashColor.a=grenades.FlashAlpha;flashOverlay.color=flashColor;
        // Preserve the original ADS shrink; the renderer snaps strokes to screen pixels.
        bool visible = !player.health.IsDead && !life.view.WantsToAim
            && !life.movement.IsRunning && !life.loadout.IsSwitching;
        crosshairScale = Mathf.Lerp(crosshairScale, visible ? 1f : 0f, 1f - Mathf.Exp(-Time.deltaTime * crosshairSmoothing));
        crosshairRoot.localScale = Vector3.one * crosshairScale;
        if (life.view.WantsToAim) shotSpread = 0f;
        else if (Time.time >= recoverSpreadAt)
            shotSpread = Mathf.Lerp(shotSpread, 0f, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(.01f, shotRecoveryTime)));
        PositionCrosshair();
        bool spread = life.loadout.EquippedWeapon.definition.UsesSpread;
        foreach (var segment in crosshairSegments) segment.enabled = !spread && crosshairScale > minimumCrosshairScale;
        foreach (var segment in spreadCrosshairSegments) segment.enabled = spread && crosshairScale > minimumCrosshairScale;
        crosshair.alpha = 1f;
        hitMarker.alpha = Time.time < hitUntil ? 1f : 0f;
        medalGroup.alpha = 1f;
        medal.alpha = Mathf.Clamp01((medalUntil - Time.time) * 2f);
        skullExpires.RemoveAll(expiry => expiry <= Time.time);
        for (int i = 0; i < killSkulls.Length; i++)
        {
            killSkulls[i].alpha = i < skullExpires.Count
                ? Mathf.Clamp01((skullExpires[i] - Time.time) * 2f) : 0f;
            // Center each populated row, including a single kill or a partial last row.
            int row = i / 4;
            int columns = Mathf.Clamp(skullExpires.Count - row * 4, 1, 4);
            killSkulls[i].GetComponent<RectTransform>().anchoredPosition =
                new Vector2((i % 4 - (columns - 1) * .5f) * 34f, -50f - row * 34f);
        }
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
        mode.text = match.rules ? $"{match.DisplayName}  /  TEAM TICKETS" : $"{match.DisplayName}  /  FIRST TO {match.ScoreLimit}";
        redProgress.fillAmount = Mathf.Clamp01((float)match.RedScore / match.ScoreLimit);
        blueProgress.fillAmount = Mathf.Clamp01((float)match.BlueScore / match.ScoreLimit);
        var loadout = player.player.loadout; var ammo = loadout.EquippedState;
        weaponName.text = loadout.slots[loadout.selectedSlot].name.ToUpperInvariant();
        ammunition.text = $"{ammo.MagazineAmmo:00}<size=24><color=#B9C0C5> / {ammo.ReserveAmmo:000}</color></size>";
        fireMode.text = player.player.actions.IsReloading ? "RELOADING" : player.player.actions.fireMode == SoldierActions.FireMode.Automatic ? "AUTO" : "SEMI";
        if(loadout.EquippedWeapon.definition.infiniteAmmunition){ammunition.text="∞";fireMode.text="MELEE";}
        int ordinal = System.Array.IndexOf(loadout.AvailableSlots, loadout.selectedSlot);
        rifleIcon.SetActive(ordinal == 0); pistolIcon.SetActive(ordinal > 0);
        var equippedIcon = (ordinal == 0 ? rifleIcon : pistolIcon).GetComponent<UnityEngine.UI.Image>();
        equippedIcon.sprite = loadout.EquippedWeapon.definition.icon;
        equippedIcon.preserveAspect = true;
        var launchers=player.player.GetComponent<SoldierLaunchers>();
        if(launchers && launchers.IsEquipped)
        {
            weaponName.text=launchers.Equipped.displayName.ToUpperInvariant();
            int count=launchers.Count(launchers.Selected);
            ammunition.text=$"{(launchers.Ready&&count>0?1:0):00}<size=24><color=#B9C0C5> / {Mathf.Max(0,count-(launchers.Ready?1:0)):000}</color></size>";
            fireMode.text=count==0?"EMPTY":launchers.Ready?"SINGLE":"REARMING";
            equippedIcon.sprite=launchers.Equipped.icon; ordinal=-1;
        }
        for(int i=0;i<launcherIcons.Length;i++)
        {
            bool owned=launchers && i<launchers.types.Length;
            launcherIcons[i].transform.parent.gameObject.SetActive(owned);
            if(!owned)continue;
            bool selected=launchers.IsEquipped&&launchers.Selected==i;
            launcherIcons[i].sprite=launchers.types[i].icon;
            launcherIcons[i].color=new Color(1,1,1,launchers.Count(i)>0?1:.25f);
            launcherCounts[i].text=launchers.Count(i)+"  "+(i==0?"[Q]":"[X]");
            launcherBackgrounds[i].color=selected?new Color(accent.r,accent.g,accent.b,.12f):new Color(1,1,1,.035f);
            StyleEquipmentText(launcherCounts[i],selected);
        }
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
            equipmentIcons[i].sprite = loadout.slots[loadout.AvailableSlots[i]].definition.icon;
            equipmentIcons[i].color = i == ordinal ? Color.white : new Color(1,1,1,.6f);
            StyleEquipmentText(equipmentKeys[i],i == ordinal);
            equipmentBackgrounds[i].color = i == ordinal ? new Color(accent.r,accent.g,accent.b,.12f) : new Color(1,1,1,.035f);
        }
        var grenades=player.player.GetComponent<SoldierGrenades>();
        for(int i=0;i<grenadeIcons.Length;i++)
        {
            grenadeIcons[i].sprite=grenades.types[i].icon;
            grenadeIcons[i].color=new Color(1,1,1,grenades.Count(i)>0?1:.25f);
            grenadeCounts[i].text=grenades.Count(i)+"  "+(grenades.Selected==i?"[G]":"");
            grenadeBackgrounds[i].color=grenades.Selected==i?new Color(accent.r,accent.g,accent.b,.12f):new Color(1,1,1,.035f);
            StyleEquipmentText(grenadeCounts[i],grenades.Selected==i);
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
            killWeapons[i].sprite = kills[i].weapon;
        }
        deathStatus.text = !match.IsRunning ? $"{match.Result.ToUpperInvariant()}\nBACKSPACE TO RESTART"
            : player.IsRespawning ? $"RESPAWN IN {player.RespawnRemaining:F1}s" : "";
    }
    void StyleEquipmentText(TMP_Text label,bool selected)
    {
        label.color=Color.white;
        var material=selected?selectedText:normalText;
        if(label.fontSharedMaterial!=material){label.fontSharedMaterial=material;label.UpdateMeshPadding();}
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
