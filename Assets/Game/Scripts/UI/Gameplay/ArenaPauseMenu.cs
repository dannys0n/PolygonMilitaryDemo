using UnityEngine;

[DefaultExecutionOrder(300)]
public class ArenaPauseMenu : MonoBehaviour
{
    public ArenaClassMenu classMenu;
    [Header("Pause and deployment presentation")]
    public TMPro.TMP_FontAsset menuFont;
    public Material normalText, selectedText;
    public UnityEngine.UI.Button deploymentButton;
    public GameObject panel;
    public ArenaSceneTransition transition;
    public bool IsPaused { get; private set; }
    public UnityEngine.UI.Button spectateButton;
    public ArenaSensitivitySlider sensitivitySlider;
    ArenaAISpectator spectator;
    TMPro.TMP_Text spectateLabel;
    bool previousSpectatorBlocked;
    ConquestDeploymentMenu deployment;
    SoldierLocalInput localInput;
    ArenaClassEditor classEditor;
    bool inputEnabled;
    CursorLockMode previousCursor;
    bool previousVisible;
    float previousTimeScale;

    void Awake()
    {
        localInput = FindAnyObjectByType<SoldierLocalInput>();
        deployment = FindAnyObjectByType<ConquestDeploymentMenu>();
        spectator = FindAnyObjectByType<ArenaAISpectator>();
        classEditor = FindAnyObjectByType<ArenaClassEditor>(FindObjectsInactive.Include);
        if (!classMenu && deploymentButton && deployment)
        {
            deploymentButton.gameObject.SetActive(localInput);
            deploymentButton.onClick.AddListener(deployment.OpenRedeployment);
        }
        if (spectateButton)
        {
            spectateButton.gameObject.SetActive(spectator && spectator.localPlayer);
            spectateButton.onClick.AddListener(ToggleSpectator);
            spectateLabel = spectateButton.GetComponentInChildren<TMPro.TMP_Text>();
        }
    }
    void Start() => panel.SetActive(false);
    public void ToggleSpectator()
    {
        Resume();
        spectator.SetManualSpectating(!spectator.ManualSpectating);
    }
    void Update()
    {
        if (transition.IsLoading) return;
        if(Input.GetKeyDown(KeyCode.Escape))
        {
            if(deployment && deployment.CloseTacticalMap())return;
            // The editor can be created lazily; only retry lookup on a real input event.
            if(!classEditor)classEditor=FindAnyObjectByType<ArenaClassEditor>(FindObjectsInactive.Include);
            if(classEditor && classEditor.IsVisible){classEditor.Close();return;}
            if(classMenu && classMenu.IsVisible)classMenu.Back();else if(IsPaused)Resume();else Pause();
        }
        if (IsPaused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true; previousTimeScale = Time.timeScale;
        if (spectator) { previousSpectatorBlocked = spectator.ControlsBlocked; spectator.ControlsBlocked = true; }
        if (spectateLabel && spectator) spectateLabel.text = spectator.ManualSpectating ? "RETURN TO BATTLE" : "SPECTATE";
        previousCursor = Cursor.lockState; previousVisible = Cursor.visible;
        if (localInput)
        {
            inputEnabled = localInput.enabled;
            // The local input adapter unlocks Escape before this menu processes it.
            if (inputEnabled) { previousCursor = CursorLockMode.Locked; previousVisible = false; }
            localInput.enabled = false;
        }
        Time.timeScale = 0f; panel.SetActive(true);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    public void Resume()
    {
        transition.loadingPanel.SetActive(false);
        if (!IsPaused) return;
        IsPaused = false; panel.SetActive(false); Time.timeScale = previousTimeScale;
        if (localInput) localInput.enabled = inputEnabled;
        if (spectator) spectator.ControlsBlocked = previousSpectatorBlocked;
        Cursor.lockState = previousCursor; Cursor.visible = previousVisible;
    }
    public void ReturnToMenu()
    {
        Resume(); transition.Load("Assets/Game/Scenes/Application/MainMenu.unity");
    }
    void OnDestroy() { if (IsPaused) Time.timeScale = previousTimeScale; }
}
