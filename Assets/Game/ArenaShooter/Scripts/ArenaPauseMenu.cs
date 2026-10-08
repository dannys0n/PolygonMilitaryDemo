using UnityEngine;

[DefaultExecutionOrder(300)]
public class ArenaPauseMenu : MonoBehaviour
{
    public GameObject panel;
    public ArenaSceneTransition transition;
    public bool IsPaused { get; private set; }
    SoldierLocalInput localInput;
    bool inputEnabled;
    CursorLockMode previousCursor;
    bool previousVisible;
    float previousTimeScale;

    void Awake() => localInput = FindAnyObjectByType<SoldierLocalInput>();
    void Start() => panel.SetActive(false);
    void Update()
    {
        if (transition.IsLoading) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { if (IsPaused) Resume(); else Pause(); }
        if (IsPaused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true; previousTimeScale = Time.timeScale;
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
        Cursor.lockState = previousCursor; Cursor.visible = previousVisible;
    }
    public void ReturnToMenu()
    {
        Resume(); transition.Load("Assets/Game/ArenaShooter/Scenes/MainMenu.unity");
    }
    void OnDestroy() { if (IsPaused) Time.timeScale = previousTimeScale; }
}
