using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Event-driven binding shared by the settings screen and the direct pause-menu control.
[DefaultExecutionOrder(250)]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaSensitivitySlider")]
public sealed class SensitivitySlider : MonoBehaviour, IEndDragHandler, IPointerUpHandler
{
    public UnityEngine.UI.Slider slider;
    public TMP_Text label;

    bool started;

    void Start()
    {
        started = true;
        if (slider) slider.onValueChanged.AddListener(SetSensitivity);
        Refresh(MouseSensitivity.Value);
    }

    void OnEnable()
    {
        if (!slider) slider = GetComponent<UnityEngine.UI.Slider>();
        if (!slider) return;
        slider.minValue = MouseSensitivity.Minimum;
        slider.maxValue = MouseSensitivity.Maximum;
        slider.wholeNumbers = false;
        if (started) slider.onValueChanged.AddListener(SetSensitivity);
        MouseSensitivity.Changed += Refresh;
        Refresh(MouseSensitivity.Value);
    }

    public void Refresh(float value)
    {
        if (slider) slider.SetValueWithoutNotify(value);
        if (label) label.text = "MOUSE SENSITIVITY   " + value.ToString("0.00");
    }

    void SetSensitivity(float value) => MouseSensitivity.Value = value;
    public void OnEndDrag(PointerEventData data) => MouseSensitivity.Save();
    public void OnPointerUp(PointerEventData data) => MouseSensitivity.Save();
    void OnDisable()
    {
        if (slider) slider.onValueChanged.RemoveListener(SetSensitivity);
        MouseSensitivity.Changed -= Refresh;
        MouseSensitivity.Save();
    }
}
