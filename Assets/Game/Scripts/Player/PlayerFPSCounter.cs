using UnityEngine;

// The simple Flooded_Grounds FPSDisplay, kept with our game instead of the imported map.
public class PlayerFPSCounter : MonoBehaviour
{
    float deltaTime;
    GUIStyle style;

    void Update() => deltaTime += (Time.unscaledDeltaTime - deltaTime) * .1f;

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (style == null)
        {
            style = new GUIStyle { alignment = TextAnchor.UpperLeft };
            style.normal.textColor = Color.white;
        }
        style.fontSize = Mathf.Max(12, Screen.height * 2 / 100);
        float fps = deltaTime > 0f ? 1f / deltaTime : 0f;
        GUI.Label(new Rect(0, 0, Screen.width, style.fontSize + 4),
            string.Format("{0:0.0} ms ({1:0.} fps)", deltaTime * 1000f, fps), style);
    }
}
