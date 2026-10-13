using TMPro;
using UnityEngine;

// Adapted flight-data grouping from BMS's F-15C and Eagle Dynamics' Su-27 simulator manuals.
// Air data is a constant-density, still-air approximation; ALT uses the authored world datum.
public class VehicleFixedWingReadout : MonoBehaviour
{
    [Tooltip("World Y position treated as sea level. The scene currently has no pressure-altimeter simulation.")]
    public float altitudeDatum;
    [Tooltip("Disable when the parent HUD already displays airspeed, altitude and heading.")]
    public bool showAirData = true;
    GameObject panel;
    TMP_Text airData, handling, state;
    float nextRefresh;
    Vehicle displayedVehicle;
    public bool Visible => panel && panel.activeSelf;

    public void Build(TMP_Text template)
    {
        panel = new GameObject("Fixed wing flight data", typeof(RectTransform));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
        rect.anchoredPosition = new Vector2(0, 330);
        rect.sizeDelta = new Vector2(650, 105);
        airData = Label(template, "Air data", new Vector2(0, 30), 17);
        handling = Label(template, "Control and aerodynamic data", Vector2.zero, 15);
        state = Label(template, "Flight warning", new Vector2(0, -30), 16);
        panel.SetActive(false);
    }

    TMP_Text Label(TMP_Text template, string name, Vector2 position, float size)
    {
        var label = Instantiate(template, panel.transform);
        label.name = name;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
        label.rectTransform.anchoredPosition = position;
        label.rectTransform.sizeDelta = new Vector2(650, 27);
        label.fontSize = size;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.text = "";
        label.gameObject.SetActive(true);
        return label;
    }

    public void Present(Vehicle vehicle, bool show)
    {
        bool visible = show && vehicle && vehicle.motor is JetMotor;
        panel.SetActive(visible);
        if (!visible) { displayedVehicle = null; return; }
        airData.gameObject.SetActive(showAirData);
        if (displayedVehicle == vehicle && Time.unscaledTime < nextRefresh) return;
        displayedVehicle = vehicle;
        nextRefresh = Time.unscaledTime + .1f;
        var motor = (JetMotor)vehicle.motor;
        bool metric = vehicle.instrumentReference == Vehicle.InstrumentReference.Su27;
        float speed = motor.Airspeed * Mathf.Sqrt(Mathf.Max(0, motor.airDensity) / 1.225f);
        float altitude = vehicle.Body.position.y - altitudeDatum;
        float climb = vehicle.Body.linearVelocity.y;
        int heading = Mathf.RoundToInt(Mathf.Repeat(vehicle.transform.eulerAngles.y, 360)) % 360;
        airData.text = "IAS " + (speed * (metric ? 3.6f : 1.943844f)).ToString("000") + (metric ? " KM/H" : " KT")
            + "     ALT " + (altitude * (metric ? 1 : 3.28084f)).ToString("0000") + (metric ? " M" : " FT")
            + "     HDG " + heading.ToString("000") + "°";
        handling.text = "AOA " + motor.AngleOfAttack.ToString("+0.0;-0.0;0.0") + "°"
            + "    LIFT G " + motor.LoadFactor.ToString("0.0")
            + "    V/S " + (climb * (metric ? 1 : 196.8504f)).ToString("+0;-0;0") + (metric ? " M/S" : " FPM")
            + "    THR " + Mathf.RoundToInt(motor.Throttle * 100) + "%";
        string warning = motor.Stalled ? "STALL — LOWER NOSE" : motor.Grounded ? "GROUND" : "FLIGHT";
        if (motor.EnginePower > 1.01f) warning += "    A/B";
        state.text = warning + "    " + (vehicle.FlightAssistEnabled ? "ASSIST" : "MANUAL");
        state.color = motor.Stalled ? new Color(1, .55f, .12f) : Color.white;
    }
}
