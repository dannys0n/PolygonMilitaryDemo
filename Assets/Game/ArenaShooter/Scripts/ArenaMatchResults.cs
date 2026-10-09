using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Match rules stay in ArenaMatch; this component presents their final result.
[DefaultExecutionOrder(600)]
public class ArenaMatchResults : MonoBehaviour
{
    public ArenaScoreboard scoreboard;
    public GameObject panel;
    public TMP_Text outcome;
    public bool IsVisible { get; private set; }
    SoldierLocalInput localInput;
    ArenaAISpectator spectator;
    ArenaPauseMenu pauseMenu;
    bool previousInputBlocked, previousSpectatorBlocked, previousPauseEnabled;
    CursorLockMode previousCursor;
    bool previousCursorVisible;
    readonly System.Collections.Generic.List<(Canvas canvas, bool enabled)> hiddenHud = new System.Collections.Generic.List<(Canvas, bool)>();

    void Awake()
    {
        localInput = FindAnyObjectByType<SoldierLocalInput>();
        spectator = FindAnyObjectByType<ArenaAISpectator>();
        pauseMenu = FindAnyObjectByType<ArenaPauseMenu>();
        panel.SetActive(false);
    }
    void OnEnable()
    {
        scoreboard.match.MatchEnded += Show;
        scoreboard.match.MatchStarted += Hide;
    }
    void OnDisable()
    {
        scoreboard.match.MatchEnded -= Show;
        scoreboard.match.MatchStarted -= Hide;
        Hide();
    }
    void LateUpdate()
    {
        if (!IsVisible) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    void Show()
    {
        if (IsVisible) return;
        if (pauseMenu && pauseMenu.IsPaused) pauseMenu.Resume();
        previousCursor = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        if (localInput) { previousInputBlocked = localInput.InputBlocked; localInput.InputBlocked = true; }
        if (spectator) { previousSpectatorBlocked = spectator.ControlsBlocked; spectator.ControlsBlocked = true; }
        if (pauseMenu) { previousPauseEnabled = pauseMenu.enabled; pauseMenu.enabled = false; }
        hiddenHud.Clear();
        foreach (var hud in FindObjectsByType<ArenaHud>()) HideHud(hud.GetComponent<Canvas>());
        foreach (var hud in FindObjectsByType<ArenaSpectatorHud>()) HideHud(hud.GetComponent<Canvas>());
        var match = scoreboard.match;
        bool draw = match.RedScore == match.BlueScore;
        bool redWon = match.RedScore > match.BlueScore;
        string title = draw ? "DRAW" : scoreboard.localPlayer
            ? ((scoreboard.localPlayer.team == ArenaParticipant.Team.Red) == redWon ? "VICTORY" : "DEFEAT")
            : (redWon ? "RED TEAM WINS" : "BLUE TEAM WINS");
        outcome.text = $"{title}  <size=28><color=#FFFFFF>RED {match.RedScore:000}  /  BLUE {match.BlueScore:000}</color></size>";
        IsVisible = true;
        panel.SetActive(true);
        scoreboard.SetResultsVisible(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        panel.SetActive(false);
        scoreboard.SetResultsVisible(false);
        foreach (var hud in hiddenHud) if (hud.canvas) hud.canvas.enabled = hud.enabled;
        hiddenHud.Clear();
        if (localInput) localInput.InputBlocked = previousInputBlocked;
        if (spectator) spectator.ControlsBlocked = previousSpectatorBlocked;
        if (pauseMenu) pauseMenu.enabled = previousPauseEnabled;
        Cursor.lockState = previousCursor;
        Cursor.visible = previousCursorVisible;
    }
    void HideHud(Canvas canvas)
    {
        hiddenHud.Add((canvas, canvas.enabled));
        canvas.enabled = false;
    }
    public void PlayAgain() => scoreboard.match.BeginMatch();
    public void MainMenu()
    {
        Time.timeScale = 1f;
        // Keep the results input lock until the new scene replaces this one.
        SceneManager.LoadSceneAsync("Assets/Game/MainMenu.unity");
    }
}
