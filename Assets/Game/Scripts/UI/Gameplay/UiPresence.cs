using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(500)]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaUiPresence")]
public class UiPresence : MonoBehaviour
{
    [Header("Inward panel perspective")]
    public float yawDegrees = 6f;
    public float pitchDegrees = 3f;
    public float perspectiveDistance = 900f;
    [Header("HUD look lag (ignores animated sway and recoil)")]
    public bool motionEnabled = true;
    public float maximumLag = 8f;
    public float pixelsPerDegreePerSecond = .035f;
    public float lagSmoothing = 12f;
    readonly List<RectTransform> panels = new List<RectTransform>();
    readonly List<Vector2> restPositions = new List<Vector2>();
    GameplayHud hud;
    AISpectator spectator;
    MatchParticipant previousActor;
    Vector2 previousLook, lag;
    bool sampled;
    float nextScan;
    RectTransform root;

    void OnEnable() { sampled = false; lag = Vector2.zero; }
    void Start()
    {
        root = (RectTransform)transform;
        hud = GetComponent<GameplayHud>(); spectator = FindAnyObjectByType<AISpectator>();
        var scoreboard = GetComponent<Scoreboard>();
        var pause = GetComponent<PauseMenu>();
        if (hud)
        {
            // Gameplay aiming feedback stays exact and does not drift with decorative UI.
            foreach (Transform child in transform)
                if (child is RectTransform rect && child.name != "Crosshair" && child.name != "Hit Marker" && child.name != "Match Status" && child.name != "Friendly Markers") AddPanel(rect);
        }
        else if (scoreboard)
        {
            AddPanel((RectTransform)scoreboard.redContent.parent.parent);
            AddPanel((RectTransform)scoreboard.blueContent.parent.parent);
            AddPanel((RectTransform)scoreboard.matchInfo.transform);
            var title = scoreboard.panel.transform.Find("Title"); if (title) AddPanel((RectTransform)title);
        }
        else if (pause)
        {
            var buttons = pause.panel.transform.Find("Buttons"); if (buttons) AddPanel((RectTransform)buttons);
            var title = pause.panel.transform.Find("Title"); if (title) AddPanel((RectTransform)title);
        }
        else if (GetComponent<SpectatorHud>())
        {
            foreach (Transform child in transform) if (child is RectTransform rect) AddPanel(rect);
        }
        RefreshGraphics();
    }
    void AddPanel(RectTransform panel)
    {
        // Full-screen atmosphere/damage overlays belong to the display, not a tilted HUD panel.
        if(panel.GetComponent<MenuAtmosphere>())return;
        panels.Add(panel); restPositions.Add(panel.anchoredPosition);
    }
    public void RefreshGraphics()
    {
        if (!root) return;
        foreach (var graphic in GetComponentsInChildren<Graphic>(true))
        {
            if(graphic.GetComponent<MenuAtmosphere>())
            {
                var oldPerspective=graphic.GetComponent<UiPerspective>();
                if(oldPerspective)oldPerspective.enabled=false;
                continue;
            }
            foreach (var panel in panels)
            {
                if (!graphic.transform.IsChildOf(panel)) continue;
                Vector3 center = root.InverseTransformPoint(panel.TransformPoint(panel.rect.center));
                center -= (Vector3)root.rect.center;
                float yaw = Mathf.Abs(center.x) < 50 ? 0 : Mathf.Sign(center.x) * yawDegrees;
                float pitch = Mathf.Abs(center.y) < 60 ? -pitchDegrees * .5f : -Mathf.Sign(center.y) * pitchDegrees;
                var effect = graphic.GetComponent<UiPerspective>();
                if (!effect) effect = graphic.gameObject.AddComponent<UiPerspective>();
                effect.Configure(panel, new Vector2(pitch, yaw), perspectiveDistance);
                break;
            }
        }
    }
    void LateUpdate()
    {
        if (!root) return;
        if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + .5f; RefreshGraphics(); }
        if (!motionEnabled || !hud) return;
        var actor = hud.player;
        Vector2 look = new Vector2(actor.transform.eulerAngles.y, actor.player.look.Pitch);
        if (spectator && spectator.FollowedCharacter == actor && !spectator.FirstPersonSpectating)
            look = new Vector2(spectator.transform.eulerAngles.y, Mathf.DeltaAngle(0, spectator.transform.eulerAngles.x));
        Vector2 delta = new Vector2(Mathf.DeltaAngle(previousLook.x, look.x), Mathf.DeltaAngle(previousLook.y, look.y));
        float dt = Time.unscaledDeltaTime;
        Vector2 target = Vector2.zero;
        if (sampled && previousActor == actor && dt > 0 && dt < .15f && delta.magnitude < 45f && !actor.health.IsDead)
            target = Vector2.ClampMagnitude(new Vector2(-delta.x, delta.y) / dt * pixelsPerDegreePerSecond, maximumLag);
        else lag = Vector2.zero;
        previousActor = actor; previousLook = look; sampled = true;
        lag = Vector2.Lerp(lag, target, 1f - Mathf.Exp(-dt * lagSmoothing));
        for (int i = 0; i < panels.Count; i++) panels[i].anchoredPosition = restPositions[i] + lag;
    }
    void OnDisable()
    {
        for (int i = 0; i < panels.Count; i++) if (panels[i]) panels[i].anchoredPosition = restPositions[i];
    }
}
