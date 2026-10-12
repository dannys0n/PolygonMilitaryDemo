using UnityEngine;
using UnityEngine.UI;

// Dashboard ticks and needle use real vehicle speed rather than simulated engine RPM.
public class VehicleSpeedometerGraphic : MaskableGraphic
{
    float speed, maximum = 120f;
    public void SetSpeed(float kph, float maxKph)
    {
        if (Mathf.Abs(speed-kph)<.1f && Mathf.Abs(maximum-maxKph)<.1f) return;
        speed=kph; maximum=Mathf.Max(30f,maxKph); SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float radius=Mathf.Min(rectTransform.rect.width*.5f,rectTransform.rect.height)*.82f;
        Vector2 center=new Vector2(0,-rectTransform.rect.height*.3f);
        for (int i=0;i<=24;i++)
        {
            float a=Mathf.Lerp(210f,-30f,i/24f)*Mathf.Deg2Rad;
            Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
            Segment(mesh,center+d*(radius-(i%4==0?10f:5f)),center+d*radius,1f,new Color32(255,255,255,140));
        }
        float angle=Mathf.Lerp(210f,-30f,Mathf.Clamp01(speed/maximum))*Mathf.Deg2Rad;
        Segment(mesh,center,center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(radius-12f),2f,new Color32(255,150,55,255));
    }
    void Segment(VertexHelper mesh,Vector2 a,Vector2 b,float width,Color32 tint)
    {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f; int start=mesh.currentVertCount;
        foreach(var p in new[]{a-n,a+n,b+n,b-n}){var v=UIVertex.simpleVert;v.position=p;v.color=tint;mesh.AddVert(v);}
        mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
    }
}
