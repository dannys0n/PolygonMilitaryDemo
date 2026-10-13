using UnityEngine;

// Round instrument faces share geometry; all needles are driven by measured values.
public class VehicleDialGraphic : UnityEngine.UI.MaskableGraphic
{
    public bool attitude;
    public float minimum, maximum=100;
    float reading, pitch, roll;
    public void SetValue(float value){reading=value;SetVerticesDirty();}
    public void SetAttitude(float pitchDegrees,float rollDegrees){pitch=pitchDegrees;roll=rollDegrees;SetVerticesDirty();}
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();float radius=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.45f;
        for(int i=0;i<64;i++){float a=i*Mathf.PI/32,b=(i+1)*Mathf.PI/32;Line(mesh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,1,Color.white);}
        if(attitude)
        {
            Vector2 direction=Quaternion.Euler(0,0,-roll)*Vector2.right;
            Vector2 normal=new Vector2(-direction.y,direction.x);
            for(int mark=-3;mark<=3;mark++)
            {
                float offset=(mark*10-pitch)*.8f;if(Mathf.Abs(offset)>radius-4)continue;
                float half=Mathf.Sqrt(radius*radius-offset*offset)*(mark==0?.92f:.45f);
                Line(mesh,normal*offset-direction*half,normal*offset+direction*half,mark==0?2:1,new Color(1,1,1,.65f));
            }
            Line(mesh,new Vector2(-20,0),new Vector2(-6,0),2,new Color(1,.6f,.2f));Line(mesh,new Vector2(6,0),new Vector2(20,0),2,new Color(1,.6f,.2f));return;
        }
        for(int i=0;i<=20;i++)
        {
            float angle=Mathf.Lerp(225,-45,i/20f)*Mathf.Deg2Rad;Vector2 d=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            Line(mesh,d*(radius-(i%5==0?9:4)),d*(radius-2),1,new Color(1,1,1,.65f));
        }
        float needle=Mathf.Lerp(225,-45,Mathf.InverseLerp(minimum,maximum,reading))*Mathf.Deg2Rad;
        Line(mesh,Vector2.zero,new Vector2(Mathf.Cos(needle),Mathf.Sin(needle))*(radius-12),2,new Color(1,.6f,.2f));
    }
    static void Line(UnityEngine.UI.VertexHelper mesh,Vector2 a,Vector2 b,float width,Color tint)
    {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int first=mesh.currentVertCount;
        foreach(var p in new[]{a-n,a+n,b+n,b-n}){var vertex=UnityEngine.UIVertex.simpleVert;vertex.position=p;vertex.color=tint;mesh.AddVert(vertex);}
        mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
    }
}

