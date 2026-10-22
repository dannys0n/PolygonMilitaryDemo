using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Keeps home-page focus and contextual help consistent across the rotating menu.
[DefaultExecutionOrder(230)]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaMenuPresentation")]
public class MenuPresentation : MonoBehaviour
{
    public MenuNavigation navigation;
    public Button[] mainButtons;
    public GameObject homeHeading;
    GameObject lastSelection;
    bool wasHome;
    void Start() { lastSelection=mainButtons[0].gameObject; }
    void Update()
    {
        var menu=navigation.slimMenu;
        bool home=navigation.CurrentPage==MenuNavigation.Page.Main
            && !menu.playMenu.activeSelf && !menu.exitMenu.activeSelf;
        if(Input.GetKeyDown(KeyCode.Escape) && navigation.CurrentPage==MenuNavigation.Page.Main
            && (menu.playMenu.activeSelf || menu.exitMenu.activeSelf)) { menu.ReturnMenu();home=true; }
        homeHeading.SetActive(home);
        if(home && !wasHome) EventSystem.current.SetSelectedGameObject(lastSelection);
        var selected=EventSystem.current.currentSelectedGameObject;
        foreach(var button in mainButtons) if(selected==button.gameObject)lastSelection=selected;
        wasHome=home;
    }
}
