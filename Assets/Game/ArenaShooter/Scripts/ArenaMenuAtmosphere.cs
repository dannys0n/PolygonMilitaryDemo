using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Static full-screen dust, exposed by the actual visible button filaments.
[RequireComponent(typeof(Image))]
public class ArenaMenuAtmosphere : MonoBehaviour
{
    public ArenaMenuPresentation menu;
    public Image[] localFilaments = System.Array.Empty<Image>();
    Image image;
    Material source, instance;
    Camera menuCamera;
    readonly List<Image> pageLines = new List<Image>();
    readonly Vector4[] filaments = new Vector4[6];
    readonly float[] strengths = new float[6];
    static readonly int Filaments = Shader.PropertyToID("_Filaments");
    static readonly int Strengths = Shader.PropertyToID("_FilamentStrengths");

    void OnEnable()
    {
        image = GetComponent<Image>();
        source = image.material;
        instance = new Material(source);
        image.material = instance;
        menuCamera = image.canvas.worldCamera;
        pageLines.Clear();
        if (menu) foreach (var page in FindAnyObjectByType<ArenaMenuNavigation>().pages)
            foreach (var feedback in page.GetComponentsInChildren<ArenaMenuSelectionFeedback>(true))
                if (feedback.highlightLine) pageLines.Add(feedback.highlightLine);
    }

    void LateUpdate()
    {
        // Reuse the same six light slots on whichever menu is visible.
        System.Array.Clear(strengths, 0, strengths.Length);
        int count = 0;
        if (menu) foreach (var button in menu.mainButtons)
        {
            var feedback = button.GetComponent<ArenaMenuButtonFeedback>();
            var marker = feedback.focusEdge;
            if (marker.gameObject.activeInHierarchy && marker.color.a > .001f)
                AddLight(marker, marker.color.a * Mathf.Clamp01(marker.rectTransform.rect.height / feedback.markerHeight), ref count);
        }
        foreach (var marker in pageLines)
            if (marker.gameObject.activeInHierarchy && marker.color.a > .001f)
                AddLight(marker, marker.color.a, ref count);
        foreach (var marker in localFilaments)
            if (marker.gameObject.activeInHierarchy && marker.color.a > .001f)
                AddLight(marker, marker.color.a, ref count);
        instance.SetVectorArray(Filaments, filaments);
        instance.SetFloatArray(Strengths, strengths);
    }

    void AddLight(Image marker, float strength, ref int count)
    {
        if (count == filaments.Length) return;
        var rect = marker.rectTransform;
        Vector3 start = ViewportPoint(rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMax, 0f)));
        Vector3 end = ViewportPoint(rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin, 0f)));
        filaments[count] = new Vector4(start.x, start.y, end.x, end.y);
        strengths[count++] = strength;
    }

    Vector3 ViewportPoint(Vector3 world)
    {
        if (menuCamera) return menuCamera.WorldToViewportPoint(world);
        var screen = RectTransformUtility.WorldToScreenPoint(null, world);
        return new Vector3(screen.x / Screen.width, screen.y / Screen.height, 0f);
    }

    void OnDisable()
    {
        image.material = source;
        Destroy(instance);
    }
}
