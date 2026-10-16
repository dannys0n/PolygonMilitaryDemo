using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Compact spectator presentation, separate from camera/input controls.
public class ArenaSpectatorHud : MonoBehaviour
{
    public ArenaAISpectator spectator;
    public CanvasGroup visibility;
    public GameObject scorePanel;
    public TMP_Text title, controls, redScore, blueScore, timer, mode;
    public UnityEngine.UI.Image redProgress, blueProgress;
    public Color accent = new Color(1,.686f,0);
    [Header("Alternate perspective monitor")]
    public GameObject monitor;
    public RawImage monitorImage;
    public TMP_Text monitorLabel;
    [Min(256)] public int monitorWidth = 640;
    RenderTexture monitorTexture;
    float nextRefresh;
    string accentHex;
    void Awake()
    {
        accentHex = ColorUtility.ToHtmlStringRGB(accent);
        monitorTexture = new RenderTexture(monitorWidth, monitorWidth, 24, RenderTextureFormat.ARGB32)
            { name = "Spectator alternate perspective", filterMode = FilterMode.Bilinear };
        monitorTexture.Create();
        monitorImage.texture = monitorTexture;
        spectator.SetInsetTexture(monitorTexture);
    }
    void Update()
    {
        bool visible = spectator.isActiveAndEnabled && spectator.IsSpectating;
        visibility.alpha = visible ? 1f : 0f;
        monitor.SetActive(visible && spectator.FollowedCharacter);
        if (!visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .1f;
        var target = spectator.FollowedCharacter;
        string view = !target ? "FREE CAMERA" : spectator.FirstPersonSpectating ? "FIRST PERSON" : "ORBIT";
        monitorLabel.text = spectator.FirstPersonSpectating ? "THIRD PERSON  /  C SWAP" : "FIRST PERSON  /  C SWAP";
        title.text = $"<color=#{accentHex}>{view}</color>"
            + (target ? "  /  " + target.name.Replace("<","").Replace(">","") : "  /  SPECTATOR");
        controls.text = !spectator.ShowControls ? "" : !target
            ? "RMB + WASD FLY   Q/E HEIGHT   SHIFT FAST   F FOLLOW   Z/X PLAYER   TAB SCOREBOARD   L HELP"
            : spectator.FirstPersonSpectating
                ? "Z/X PLAYER   C SWAP VIEWS   F FREE CAMERA   TAB SCOREBOARD   L HELP"
                : "RMB ORBIT   WHEEL DISTANCE   Z/X PLAYER   C SWAP VIEWS   F FREE CAMERA   TAB SCOREBOARD   L HELP";
        // The shared gameplay HUD already owns the score header while following.
        scorePanel.SetActive(!target && !spectator.localPlayer);
        var match = spectator.match;
        redScore.text = match.RedScore.ToString("000"); blueScore.text = match.BlueScore.ToString("000");
        timer.text = match.ClockText;
        mode.text = match.IsRunning ? $"{match.DisplayName} / {match.TeamSize(ArenaParticipant.Team.Red)} v {match.TeamSize(ArenaParticipant.Team.Blue)}" : match.Result.ToUpperInvariant();
        redProgress.fillAmount = Mathf.Clamp01((float)match.RedScore / match.ScoreLimit);
        blueProgress.fillAmount = Mathf.Clamp01((float)match.BlueScore / match.ScoreLimit);
    }

    void OnDestroy()
    {
        if (spectator) spectator.SetInsetTexture(null);
        monitorTexture.Release();
        Destroy(monitorTexture);
    }
}
