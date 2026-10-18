using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Event-driven binding shared by the settings screen and the direct pause-menu control.
[DefaultExecutionOrder(250)]
public sealed class ArenaSensitivitySlider : MonoBehaviour, IEndDragHandler, IPointerUpHandler
{
    public UnityEngine.UI.Slider slider;
    public TMP_Text label;

    bool started;

    void Start()
    {
        started = true;
        if (slider) slider.onValueChanged.AddListener(SetSensitivity);
        Refresh(ArenaMouseSensitivity.Value);
    }

    void OnEnable()
    {
        if (!slider) slider = GetComponent<UnityEngine.UI.Slider>();
        if (!slider) return;
        slider.minValue = ArenaMouseSensitivity.Minimum;
        slider.maxValue = ArenaMouseSensitivity.Maximum;
        slider.wholeNumbers = false;
        if (started) slider.onValueChanged.AddListener(SetSensitivity);
        ArenaMouseSensitivity.Changed += Refresh;
        Refresh(ArenaMouseSensitivity.Value);
    }

    public void Refresh(float value)
    {
        if (slider) slider.SetValueWithoutNotify(value);
        if (label) label.text = "MOUSE SENSITIVITY   " + value.ToString("0.00");
    }

    void SetSensitivity(float value) => ArenaMouseSensitivity.Value = value;
    public void OnEndDrag(PointerEventData data) => ArenaMouseSensitivity.Save();
    public void OnPointerUp(PointerEventData data) => ArenaMouseSensitivity.Save();
    void OnDisable()
    {
        if (slider) slider.onValueChanged.RemoveListener(SetSensitivity);
        ArenaMouseSensitivity.Changed -= Refresh;
        ArenaMouseSensitivity.Save();
    }
}
