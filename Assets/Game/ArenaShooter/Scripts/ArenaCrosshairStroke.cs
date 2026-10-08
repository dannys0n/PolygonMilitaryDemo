using UnityEngine;
using UnityEngine.UI;

// HellSlayer's double-ended arrow silhouette, drawn directly in screen pixels.
// No minified alpha texture: all four strokes have the same solid thickness.
public class ArenaCrosshairStroke : MaskableGraphic
{
    public bool horizontal;
    public float length = 14.65f;
    public float thickness = 1.5625f;
    public float tipLength = 2f;
    Matrix4x4 lastMatrix;
    Vector2Int lastResolution;

    void LateUpdate()
    {
        var matrix = rectTransform.localToWorldMatrix;
        var resolution = new Vector2Int(Screen.width, Screen.height);
        if (matrix != lastMatrix || resolution != lastResolution)
        {
            lastMatrix = matrix;
            lastResolution = resolution;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        var center = RectTransformUtility.WorldToScreenPoint(camera,
            rectTransform.TransformPoint(rectTransform.rect.center));
        var unit = RectTransformUtility.WorldToScreenPoint(camera,
            rectTransform.TransformPoint(rectTransform.rect.center + Vector2.right));
        float scale = Vector2.Distance(center, unit);
        int width = Mathf.Max(1, Mathf.RoundToInt(thickness * scale));
        int extent = Mathf.Max(3, Mathf.RoundToInt(length * scale));
        int tip = Mathf.Clamp(Mathf.RoundToInt(tipLength * scale), 1, extent / 2);
        float along = horizontal ? center.x : center.y;
        float across = horizontal ? center.y : center.x;
        float start = Mathf.Round(along - extent * .5f);
        float bottom = Mathf.Round(across - width * .5f);
        float middle = bottom + width * .5f;
        AddVertex(start, middle);
        AddVertex(start + tip, bottom);
        AddVertex(start + extent - tip, bottom);
        AddVertex(start + extent, middle);
        AddVertex(start + extent - tip, bottom + width);
        AddVertex(start + tip, bottom + width);
        for (int i = 1; i < 5; i++) mesh.AddTriangle(0, i, i + 1);

        void AddVertex(float x, float y)
        {
            var screenPoint = horizontal ? new Vector2(x, y) : new Vector2(y, x);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,
                screenPoint, camera, out var local);
            mesh.AddVert(local, color, Vector2.zero);
        }
    }
}
