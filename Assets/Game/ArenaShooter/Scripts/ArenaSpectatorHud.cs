using UnityEngine;
using TMPro;

// Compact spectator presentation, separate from camera/input controls.
public class ArenaSpectatorHud : MonoBehaviour
{
    public ArenaAISpectator spectator;
    public CanvasGroup visibility;
    public GameObject scorePanel;
    public TMP_Text title, controls, redScore, blueScore, timer, mode;
    public UnityEngine.UI.Image redProgress, blueProgress;
    public Color accent = new Color(1,.686f,0);
    float nextRefresh;
    string accentHex;
    void Awake() => accentHex = ColorUtility.ToHtmlStringRGB(accent);
    void Update()
    {
        bool visible = spectator.isActiveAndEnabled && spectator.IsSpectating;
        visibility.alpha = visible ? 1f : 0f;
        if (!visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .1f;
        var target = spectator.FollowedCharacter;
        string view = !target ? "FREE CAMERA" : spectator.FirstPersonSpectating ? "FIRST PERSON" : "ORBIT";
        title.text = $"<color=#{accentHex}>{view}</color>"
            + (target ? "  /  " + target.name.Replace("<","").Replace(">","") : "  /  SPECTATOR");
        controls.text = !spectator.ShowControls ? "" : !target
            ? "RMB + WASD FLY   Q/E HEIGHT   SHIFT FAST   F FOLLOW   Z/X PLAYER   TAB SCOREBOARD   L HELP"
            : spectator.FirstPersonSpectating
                ? "Z/X PLAYER   C ORBIT   F FREE CAMERA   TAB SCOREBOARD   L HELP"
                : "RMB ORBIT   WHEEL DISTANCE   Z/X PLAYER   C FIRST PERSON   F FREE CAMERA   TAB SCOREBOARD   L HELP";
        // The shared gameplay HUD already owns the score header while following.
        scorePanel.SetActive(!target && !spectator.localPlayer);
        var match = spectator.match;
        redScore.text = match.RedScore.ToString("000"); blueScore.text = match.BlueScore.ToString("000");
        int seconds = Mathf.CeilToInt(match.TimeRemaining);
        timer.text = $"{seconds/60:00}:{seconds%60:00}";
        mode.text = match.IsRunning ? $"TEAM DEATHMATCH / {match.TeamSize(ArenaParticipant.Team.Red)} v {match.TeamSize(ArenaParticipant.Team.Blue)}" : match.Result.ToUpperInvariant();
        redProgress.fillAmount = Mathf.Clamp01((float)match.RedScore / match.settings.scoreLimit);
        blueProgress.fillAmount = Mathf.Clamp01((float)match.BlueScore / match.settings.scoreLimit);
    }
}
