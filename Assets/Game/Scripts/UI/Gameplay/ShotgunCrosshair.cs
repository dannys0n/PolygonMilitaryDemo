using UnityEngine;
using UnityEngine.UI;

// Four open arcs with a fixed screen-space stroke, independent of spread radius.
public sealed class ShotgunCrosshair : MaskableGraphic
{
    public float radius;
    public float thickness=1f;
    float lastRadius,lastThickness;
    Matrix4x4 lastMatrix;
    Vector2Int lastResolution;

    void LateUpdate()
    {
        var matrix=rectTransform.localToWorldMatrix;
        var resolution=new Vector2Int(Screen.width,Screen.height);
        if(radius==lastRadius && thickness==lastThickness && matrix==lastMatrix && resolution==lastResolution)return;
        lastRadius=radius;lastThickness=thickness;lastMatrix=matrix;lastResolution=resolution;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var camera=canvas && canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        var center=RectTransformUtility.WorldToScreenPoint(camera,rectTransform.TransformPoint(rectTransform.rect.center));
        float inner=Mathf.Max(0,radius),outer=inner+Mathf.Max(1,Mathf.Round(thickness));
        const int steps=24;
        for(int arc=0;arc<4;arc++)
            for(int step=0;step<steps;step++)
            {
                float a=(arc*90f-30f+60f*step/steps)*Mathf.Deg2Rad;
                float b=(arc*90f-30f+60f*(step+1)/steps)*Mathf.Deg2Rad;
                var first=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                var second=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                int v=mesh.currentVertCount;
                Add(center+first*inner);Add(center+first*outer);
                Add(center+second*outer);Add(center+second*inner);
                mesh.AddTriangle(v,v+1,v+2);mesh.AddTriangle(v,v+2,v+3);
            }
        void Add(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screen,camera,out var local);
            mesh.AddVert(local,color,Vector2.zero);
        }
    }
}
