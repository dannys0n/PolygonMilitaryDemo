using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Local scene loading only. Backend/session ownership will be added separately.
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaSceneTransition")]
public class SceneTransition : MonoBehaviour
{
    public GameObject loadingPanel;
    public UnityEngine.UI.Slider progressBar;
    public TMPro.TMP_Text status;
    public UnityEngine.UI.Button backButton;
    public bool IsLoading { get; private set; }

    public void Load(string scene)
    {
        if (IsLoading) return;
        loadingPanel.SetActive(true); backButton.gameObject.SetActive(false);
        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            status.text = "This arena is unavailable. Return to the menu and try again.";
            backButton.gameObject.SetActive(true); return;
        }
        StartCoroutine(LoadScene(scene));
    }

    IEnumerator LoadScene(string scene)
    {
        IsLoading = true;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        status.text = "Loading arena..."; progressBar.value = 0f;
        var operation = SceneManager.LoadSceneAsync(scene);
        while (!operation.isDone)
        {
            progressBar.value = Mathf.Clamp01(operation.progress / .9f);
            yield return null;
        }
    }
}
