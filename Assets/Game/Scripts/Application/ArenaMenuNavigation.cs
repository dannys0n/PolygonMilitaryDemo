using System.Collections;
using UnityEngine;
using TMPro;

// Presentation-only navigation. Online services can replace the stub actions later.
[DefaultExecutionOrder(210)]
public class ArenaMenuNavigation : MonoBehaviour
{
    public enum Page { Main, Matchmaking, ServerBrowser, Settings, Extras }
    public SlimUI.ModernMenu.UIMenuManager slimMenu;
    public Animator cameraAnimator;
    public Transform[] cameraPoses;
    public GameObject[] pages;
    public TMP_Text serverStatus;
    public float transitionDuration = .4f;
    public Page CurrentPage { get; private set; }
    Coroutine transition;

    void Start()
    {
        // One owner of the camera transform for every destination.
        cameraAnimator.enabled = false;
        transform.SetPositionAndRotation(cameraPoses[0].position, cameraPoses[0].rotation);
        for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == 0);
    }
    void Update()
    {
        if (CurrentPage != Page.Main && Input.GetKeyDown(KeyCode.Escape)) ShowMain();
    }
    public void ShowMain() => Navigate(Page.Main);
    public void ShowMatchmaking() => Navigate(Page.Matchmaking);
    public void ShowServerBrowser() => Navigate(Page.ServerBrowser);
    public void ShowSettings() => Navigate(Page.Settings);
    public void ShowExtras() => Navigate(Page.Extras);
    void Navigate(Page page)
    {
        slimMenu.ReturnMenu();
        if (page == Page.Extras) slimMenu.extrasMenu.SetActive(true);
        slimMenu.PlaySwoosh();
        CurrentPage = page;
        if (transition != null) StopCoroutine(transition);
        pages[(int)page].SetActive(true);
        transition = StartCoroutine(MoveCamera((int)page));
    }
    IEnumerator MoveCamera(int index)
    {
        Vector3 start = transform.position;
        Quaternion rotation = transform.rotation;
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / Mathf.Max(.01f, transitionDuration)));
            transform.SetPositionAndRotation(Vector3.Lerp(start, cameraPoses[index].position, blend),
                Quaternion.Slerp(rotation, cameraPoses[index].rotation, blend));
            yield return null;
        }
        transform.SetPositionAndRotation(cameraPoses[index].position, cameraPoses[index].rotation);
        for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == index);
        transition = null;
    }
    public void RefreshServers() => serverStatus.text = "16 PLACEHOLDER SERVERS  /  VISUAL PREVIEW ONLY";
}

