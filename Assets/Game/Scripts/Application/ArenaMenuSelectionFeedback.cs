using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Presentation only: each Selectable keeps its existing action and selected state.
public class ArenaMenuSelectionFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler, ISubmitHandler, IMoveHandler
{
    public UnityEngine.UI.Selectable control;
    [Tooltip("Optional vertical hover filament for menu navigation buttons.")]
    public UnityEngine.UI.Image highlightLine;
    public TMP_Text[] labels;
    public GameObject selectionIndicator;
    public Material normalText, selectedText;
    [Tooltip("Add a stronger white glow on press to the normal hover brightening.")]
    public bool brightenOnPress;
    public float responseTime = .16f;
    public float sweepDuration = .38f;
    [Tooltip("Gap from the filament core to the visible left edge of the label, in UI units.")]
    public float textGap = 24f;
    bool hovered, focused, pressed, wasHighlighted;
    float highlightAmount, textAmount, revealTime, submitUntil;
    // List owners can supply selection without fighting the hover presentation's materials.
    public bool Chosen { get; set; }
    Material appliedMaterial;
    Color appliedColor;
    bool textApplied;

    void OnEnable()
    {
        hovered = pressed = wasHighlighted = false;
        submitUntil = 0f;
        focused = false;
        highlightAmount = textAmount = revealTime = 0f;
        textApplied = false;
        if (highlightLine)
        {
            labels[0].ForceMeshUpdate(true);
            LayoutHighlightLine(0f);
        }
    }

    void Update()
    {
        bool chosen = Chosen || selectionIndicator && selectionIndicator.activeSelf;
        if (control is UnityEngine.UI.Toggle toggle) chosen |= toggle.isOn;
        chosen &= control.IsInteractable();
        // A mouse click selects the control in EventSystem without turning hover into persistent keyboard focus.
        focused &= EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject;
        bool highlighted = control.IsInteractable() && (hovered || focused);
        bool pressHighlight = brightenOnPress && control.IsInteractable()
            && (pressed || Time.unscaledTime < submitUntil);
        float step = Time.unscaledDeltaTime / Mathf.Max(.01f, responseTime);
        bool brightText = chosen || highlighted || pressHighlight;
        textAmount = pressHighlight ? 1f : Mathf.MoveTowards(textAmount, brightText ? 1f : 0f, step);
        var material = chosen || pressHighlight ? selectedText : normalText;
        var brightColor = chosen || pressHighlight ? Color.white : new Color(.9f, .9f, .9f);
        var textColor = Color.Lerp(new Color(.7f, .7f, .7f), brightColor, Mathf.SmoothStep(0, 1, textAmount));
        if (!textApplied || material != appliedMaterial || textColor != appliedColor)
        {
            foreach (var label in labels)
            {
                if (label.fontSharedMaterial != material)
                {
                    label.fontSharedMaterial = material;
                    label.UpdateMeshPadding();
                }
                label.color = textColor;
            }
            textApplied = true; appliedMaterial = material; appliedColor = textColor;
        }
        if (!highlightLine) return;
        if (highlighted && !wasHighlighted) revealTime = 0f;
        wasHighlighted = highlighted;
        highlightAmount = Mathf.MoveTowards(highlightAmount, highlighted ? 1f : 0f, step);
        revealTime += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(revealTime / Mathf.Max(.01f, sweepDuration));
        float reveal = 1f - Mathf.Pow(1f - progress, 3f);
        if (highlightAmount > 0f) LayoutHighlightLine(reveal);
        var lineColor = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, highlightAmount));
        if (highlightLine.color != lineColor) highlightLine.color = lineColor;
    }

    public void LayoutHighlightLine(float reveal)
    {
        var label = labels[0];
        var bounds = label.textBounds;
        // TMP can report uninitialized bounds before an inactive label builds its mesh.
        if (label.textInfo.characterCount == 0 || Mathf.Abs(bounds.min.x) > 100000f) return;
        var left = highlightLine.transform.parent.InverseTransformPoint(
            label.transform.TransformPoint(new Vector3(bounds.min.x, bounds.center.y, 0f)));
        var rect = (RectTransform)transform;
        float height = Mathf.Min(60f, rect.rect.height * .75f);
        var line = highlightLine.rectTransform;
        line.anchorMin = line.anchorMax = new Vector2(.5f, .5f);
        line.pivot = new Vector2(.5f, 1f);
        // Use the rendered label edge, since centered buttons have different empty margins.
        line.anchoredPosition = new Vector2(left.x - rect.rect.center.x - textGap,
            left.y - rect.rect.center.y + height * .5f);
        line.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height * reveal);
    }

    void OnDisable()
    {
        hovered = focused = pressed = false;
        if (highlightLine) highlightLine.color = new Color(1, 1, 1, 0);
    }

    public void OnPointerEnter(PointerEventData data) { hovered = true; focused = false; }
    public void OnPointerExit(PointerEventData data) { hovered = focused = pressed = false; submitUntil = 0f; }
    public void OnSelect(BaseEventData data) { focused = !(data is PointerEventData); }
    public void OnDeselect(BaseEventData data) { focused = pressed = false; submitUntil = 0f; }
    public void OnPointerDown(PointerEventData data)
    { if (data.button == PointerEventData.InputButton.Left) { focused = false; pressed = true; submitUntil = Time.unscaledTime + .08f; } }
    public void OnPointerUp(PointerEventData data) { pressed = false; }
    public void OnSubmit(BaseEventData data) { focused = true; submitUntil = Time.unscaledTime + .12f; }
    public void OnMove(AxisEventData data) { focused = true; }
}
