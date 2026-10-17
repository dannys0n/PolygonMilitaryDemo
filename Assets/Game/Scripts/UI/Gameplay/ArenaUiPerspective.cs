using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Project a UI panel's graphics as one tilted plane without tying it to the shaken camera.
public class ArenaUiPerspective : BaseMeshEffect
{
    RectTransform plane;
    Quaternion tilt;
    float distance;
    TMP_Text text;

    protected override void Awake() { base.Awake(); text = GetComponent<TMP_Text>(); }
    protected override void OnEnable()
    {
        base.OnEnable();
        if (!text) text = GetComponent<TMP_Text>();
        if (text) text.OnPreRenderText += WarpText;
    }
    protected override void OnDisable()
    {
        if (text) text.OnPreRenderText -= WarpText;
        base.OnDisable();
    }
    public void Configure(RectTransform panel, Vector2 angles, float perspectiveDistance)
    {
        Quaternion rotation = Quaternion.Euler(angles.x, angles.y, 0f);
        if (plane == panel && tilt == rotation && distance == perspectiveDistance) return;
        plane = panel; tilt = rotation; distance = Mathf.Max(100f, perspectiveDistance);
        graphic.SetVerticesDirty();
    }
    Vector3 Project(Vector3 vertex)
    {
        Vector3 local = plane.InverseTransformPoint(transform.TransformPoint(vertex));
        Vector2 center = plane.rect.center;
        Vector3 rotated = tilt * new Vector3(local.x - center.x, local.y - center.y, 0);
        float perspective = distance / Mathf.Max(distance * .25f, distance + rotated.z);
        return transform.InverseTransformPoint(plane.TransformPoint(new Vector3(center.x + rotated.x * perspective, center.y + rotated.y * perspective, local.z)));
    }
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || !plane || text) return;
        UIVertex vertex = default;
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i); vertex.position = Project(vertex.position); mesh.SetUIVertex(vertex, i);
        }
    }
    void WarpText(TMP_TextInfo info)
    {
        if (!IsActive() || !plane) return;
        // TMP keeps spare material entries when changing text/fallback glyphs. Only
        // the active meshes belong to this rebuild; spare entries can retain a
        // previous vertex count after their vertex buffer has been resized.
        int materials = Mathf.Min(info.materialCount, info.meshInfo.Length);
        for (int i = 0; i < materials; i++)
        {
            var mesh = info.meshInfo[i];
            var vertices = mesh.vertices;
            if (vertices == null) continue;
            int count = Mathf.Min(mesh.vertexCount, vertices.Length);
            for (int v = 0; v < count; v++) vertices[v] = Project(vertices[v]);
        }
    }
}
