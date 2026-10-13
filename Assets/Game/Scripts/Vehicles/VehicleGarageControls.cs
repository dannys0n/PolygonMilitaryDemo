using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Scene-only convenience for crossing the garage; vehicle speed remains unchanged.
public class VehicleGarageControls : MonoBehaviour
{
    public SoldierMovement player;
    public TMP_FontAsset font;
    float walkingSpeed, runningSpeed;
    bool fastMovement = true;
    TMP_Text label;

    void Start()
    {
        walkingSpeed = player.moveSpeed;
        runningSpeed = player.runSpeed;
        var canvas = new GameObject("Garage controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.transform.SetParent(transform, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.GetComponent<Canvas>().sortingOrder = 100;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var button = new GameObject("Fast movement", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        button.transform.SetParent(canvas.transform, false);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-24, -18);
        rect.sizeDelta = new Vector2(260, 38);
        button.GetComponent<UnityEngine.UI.Image>().color = new Color(.08f, .08f, .08f, .8f);
        button.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(Toggle);
        var text = new GameObject("Speed label", typeof(RectTransform), typeof(TextMeshProUGUI));
        text.transform.SetParent(button.transform, false);
        label = text.GetComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = 19;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        ApplyMovementSpeed();
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) && player.enabled) Toggle();
    }
    void Toggle()
    {
        fastMovement = !fastMovement;
        ApplyMovementSpeed();
    }
    void ApplyMovementSpeed()
    {
        player.moveSpeed = walkingSpeed * (fastMovement ? 3 : 1);
        player.runSpeed = runningSpeed * (fastMovement ? 3 : 1);
        Refresh();
    }
    void Refresh()
    {
        label.text = "[F9]  MOVE SPEED  " + (fastMovement ? "3x" : "1x");
        label.color = fastMovement ? new Color(1, .55f, .12f) : Color.white;
    }
    void OnDisable()
    {
        if (walkingSpeed > 0 && player) { player.moveSpeed = walkingSpeed; player.runSpeed = runningSpeed; }
    }
}
