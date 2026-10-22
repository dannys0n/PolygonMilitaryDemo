using UnityEngine;
using UnityEngine.UI;

// An open acquisition bracket and center pipper, distinct from shotgun arcs.
public sealed class LauncherCrosshair : MaskableGraphic
{
    public float radius = 26f;
    float previousRadius;
    void LateUpdate() { if (!Mathf.Approximately(radius, previousRadius)) { previousRadius = radius; SetVerticesDirty(); } }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        const float arm = 9f, thickness = 1.5f;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                float left = x > 0 ? radius - arm : -radius;
                float bottom = y > 0 ? radius - arm : -radius;
                Quad(left, y * radius - thickness * .5f, arm, thickness);
                Quad(x * radius - thickness * .5f, bottom, thickness, arm);
            }
        Quad(-1f, -1f, 2f, 2f);
        void Quad(float x, float y, float w, float h)
        {
            int n = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x,y),color,Vector2.zero);mesh.AddVert(new Vector3(x+w,y),color,Vector2.zero);
            mesh.AddVert(new Vector3(x+w,y+h),color,Vector2.zero);mesh.AddVert(new Vector3(x,y+h),color,Vector2.zero);
            mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
    }
}
