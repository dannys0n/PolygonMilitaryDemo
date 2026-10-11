using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Button owns the action; this component animates only the selection treatment.
public class ArenaMenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    public Button button;
    public Image focusEdge, glow;
    public TMP_Text label, context;
    [TextArea] public string description;
    public float responseTime = .16f;
    public float sweepDuration = .38f;
    public float markerHeight = 60f;
    bool hovered, selected, pressed, wasFocused;
    float focusAmount, revealTime, submitUntil;

    void OnEnable()
    {
        hovered = pressed = wasFocused = false;
        selected = EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject;
        focusAmount = revealTime = 0f;
        Refresh();
    }
    void Update() => Refresh();
    void Refresh()
    {
        bool focused = button.interactable && (hovered || selected);
        if (focused && !wasFocused) revealTime = 0f;
        wasFocused = focused;
        focusAmount = Mathf.MoveTowards(focusAmount, focused ? 1f : 0f,
            Time.unscaledDeltaTime / Mathf.Max(.01f, responseTime));
        float eased = Mathf.SmoothStep(0f, 1f, focusAmount);
        revealTime += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(revealTime / Mathf.Max(.01f, sweepDuration));
        float reveal = 1f - Mathf.Pow(1f - progress, 3f);
        float pulse = pressed || Time.unscaledTime < submitUntil ? 1f : 0f;
        label.color = Color.Lerp(new Color(.65f, .65f, .65f), Color.white, eased);
        // Top anchors stay fixed while the marker and its diffuse spill grow downward.
        focusEdge.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, markerHeight * reveal);
        focusEdge.color = new Color(1f, 1f, 1f, eased * (1f + pulse * .1f));
        glow.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, markerHeight * reveal + 20f);
        glow.color = new Color(1f, 1f, 1f, eased * reveal * (.6f + pulse * .12f));
    }
    public void OnPointerEnter(PointerEventData data)
    { hovered = true; EventSystem.current.SetSelectedGameObject(gameObject); }
    public void OnPointerExit(PointerEventData data) { hovered = false; pressed = false; }
    public void OnPointerDown(PointerEventData data)
    { if (data.button == PointerEventData.InputButton.Left) pressed = true; }
    public void OnPointerUp(PointerEventData data) { pressed = false; }
    public void OnSelect(BaseEventData data) { selected = true; context.text = description; }
    public void OnDeselect(BaseEventData data) { selected = false; pressed = false; }
    public void OnSubmit(BaseEventData data) { submitUntil = Time.unscaledTime + .08f; }
}
