using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Button owns the action; this component animates only the selection treatment.
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaMenuButtonFeedback")]
public class MenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler, IMoveHandler
{
    public Button button;
    public Image focusEdge, glow;
    public TMP_Text label, context;
    public Material normalText, pressedText;
    [TextArea] public string description;
    public float responseTime = .16f;
    public float sweepDuration = .38f;
    public float markerHeight = 60f;
    bool hovered, selected, pressed, wasFocused;
    float focusAmount, revealTime, submitUntil;

    void OnEnable()
    {
        hovered = pressed = wasFocused = false;
        selected = false;
        focusAmount = revealTime = 0f;
        submitUntil = 0f;
        Refresh();
    }
    void Update() => Refresh();
    void Refresh()
    {
        // Pointer selection remains available for navigation, but only keyboard focus keeps the filament lit.
        selected &= EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject;
        bool focused = button.interactable && (hovered || selected);
        if (focused && !wasFocused) revealTime = 0f;
        wasFocused = focused;
        focusAmount = Mathf.MoveTowards(focusAmount, focused ? 1f : 0f,
            Time.unscaledDeltaTime / Mathf.Max(.01f, responseTime));
        float eased = Mathf.SmoothStep(0f, 1f, focusAmount);
        revealTime += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(revealTime / Mathf.Max(.01f, sweepDuration));
        float reveal = 1f - Mathf.Pow(1f - progress, 3f);
        float pulse = button.interactable && (pressed || Time.unscaledTime < submitUntil) ? 1f : 0f;
        var material = pulse > 0f ? pressedText : normalText;
        if (label.fontSharedMaterial != material)
        {
            label.fontSharedMaterial = material;
            label.UpdateMeshPadding();
        }
        label.color = pulse > 0f ? Color.white : Color.Lerp(new Color(.65f, .65f, .65f), new Color(.9f, .9f, .9f), eased);
        // Top anchors stay fixed while the marker and its diffuse spill grow downward.
        focusEdge.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, markerHeight * reveal);
        focusEdge.color = new Color(1f, 1f, 1f, eased * (1f + pulse * .1f));
        glow.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, markerHeight * reveal + 20f);
        glow.color = new Color(1f, 1f, 1f, eased * reveal * (.6f + pulse * .12f));
    }
    public void OnPointerEnter(PointerEventData data)
    { hovered = true; selected = false; if (context) context.text = description; }
    public void OnPointerExit(PointerEventData data) { hovered = selected = pressed = false; submitUntil = 0f; }
    public void OnPointerDown(PointerEventData data)
    { if (data.button == PointerEventData.InputButton.Left) { selected = false; pressed = true; submitUntil = Time.unscaledTime + .08f; } }
    public void OnPointerUp(PointerEventData data) { pressed = false; }
    public void OnSelect(BaseEventData data) { selected = !(data is PointerEventData); if (context) context.text = description; }
    public void OnDeselect(BaseEventData data) { selected = false; pressed = false; submitUntil = 0f; }
    public void OnSubmit(BaseEventData data) { selected = true; submitUntil = Time.unscaledTime + .08f; }
    public void OnMove(AxisEventData data) { selected = true; }
    void OnDisable()
    {
        hovered = selected = pressed = wasFocused = false;
        focusAmount = revealTime = submitUntil = 0f;
        focusEdge.color = glow.color = new Color(1f, 1f, 1f, 0f);
    }
}
