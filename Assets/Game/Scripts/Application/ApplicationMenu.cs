using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Demo1 presentation stays intact; this adds a temporary project-scene launcher.
[DefaultExecutionOrder(200)]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaApplicationMenu")]
public class ApplicationMenu : MonoBehaviour
{
    public SlimUI.ModernMenu.UIMenuManager slimMenu;
    public UnityEngine.UI.Button sceneButtonTemplate;
    public Transform sceneList;
    public UnityEngine.UI.Button previousPage, nextPage;
    [Min(1)] public int scenesPerPage = 5;
    public string[] ScenePaths { get; private set; }
    public int Page { get; private set; }
    readonly List<GameObject> sceneButtons = new List<GameObject>();

    void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
#if UNITY_EDITOR
        ScenePaths = UnityEditor.AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
            .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).ToArray();
#else
        ScenePaths = Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
            .Select(SceneUtility.GetScenePathByBuildIndex).ToArray();
#endif
        ScenePaths = ScenePaths.OrderBy(p => p.StartsWith("Assets/Game/", System.StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(Path.GetFileNameWithoutExtension, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p, System.StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string path in ScenePaths)
        {
            var button = Instantiate(sceneButtonTemplate, sceneList, false);
            button.name = "Scene: " + path;
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            string selectedPath = path;
            button.onClick.AddListener(() => LoadScene(selectedPath));
            string name = Path.GetFileNameWithoutExtension(path);
            bool duplicate = ScenePaths.Count(p => Path.GetFileNameWithoutExtension(p) == name) > 1;
            foreach (var label in button.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                label.text = duplicate ? name + "\n" + Path.GetDirectoryName(path).Replace("Assets/", "") : name;
                label.fontSize = duplicate || name.Length > 24 ? 240f : 350f;
            }
            sceneButtons.Add(button.gameObject);
        }
        sceneButtonTemplate.gameObject.SetActive(false);
        previousPage.onClick.AddListener(PreviousPage);
        nextPage.onClick.AddListener(NextPage);
        ShowPage(0);
    }

    public void PreviousPage() => ShowPage(Page - 1);
    public void NextPage() => ShowPage(Page + 1);
    void ShowPage(int page)
    {
        int size = Mathf.Max(1, scenesPerPage);
        int last = Mathf.Max(0, (sceneButtons.Count - 1) / size);
        Page = Mathf.Clamp(page, 0, last);
        for (int i = 0; i < sceneButtons.Count; i++) sceneButtons[i].SetActive(i / size == Page);
        previousPage.interactable = Page > 0; nextPage.interactable = Page < last;
    }

    public void LoadScene(string path)
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        slimMenu.LoadScene(path);
#endif
    }
}
