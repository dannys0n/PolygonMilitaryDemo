using UnityEngine;

public class VehicleGunnerReticleGraphic : UnityEngine.UI.MaskableGraphic
{
    public VehicleAttackSight.SightStyle style;
    public bool orientation,barrelMarker;
    float traverse;
    public void SetTraverse(float degrees){traverse=degrees;SetVerticesDirty();}
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        if(barrelMarker){Vector2[] points={new Vector2(0,9),new Vector2(9,0),new Vector2(0,-9),new Vector2(-9,0)};for(int i=0;i<4;i++){Segment(mesh,points[i],points[(i+1)%4],3,new Color(0,0,0,.5f));Segment(mesh,points[i],points[(i+1)%4],1.5f,new Color(1,.55f,.12f));}return;}
        if(orientation)
        {
            Line(mesh,new Vector2(-13,-22),new Vector2(-13,22));Line(mesh,new Vector2(13,-22),new Vector2(13,22));Line(mesh,new Vector2(-13,22),new Vector2(13,22));Line(mesh,new Vector2(-13,-22),new Vector2(13,-22));
            Vector2 direction=Quaternion.Euler(0,0,-traverse)*Vector2.up;Line(mesh,Vector2.zero,direction*39);
            for(int i=0;i<32;i++){float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;Line(mesh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*42,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*42);}return;
        }
        if(style==VehicleAttackSight.SightStyle.T72)
        {
            Line(mesh,new Vector2(-9,-8),new Vector2(0,5));Line(mesh,new Vector2(0,5),new Vector2(9,-8));
            for(int side=-1;side<=1;side+=2){Line(mesh,new Vector2(side*20,0),new Vector2(side*125,0));for(int x=40;x<=120;x+=20)Line(mesh,new Vector2(side*x,-4),new Vector2(side*x,4));}
            for(int i=0;i<5;i++){float y=-55-i*14;Line(mesh,new Vector2(-110,y),new Vector2(-50+i*10,y));}return;
        }
        float gap=style==VehicleAttackSight.SightStyle.Leopard?5:12;
        Line(mesh,new Vector2(-125,0),new Vector2(-gap,0));Line(mesh,new Vector2(gap,0),new Vector2(125,0));
        Line(mesh,new Vector2(0,-110),new Vector2(0,-gap));Line(mesh,new Vector2(0,gap),new Vector2(0,90));
        for(int i=-4;i<=4;i++){if(i==0)continue;float x=i*24;Line(mesh,new Vector2(x,-4),new Vector2(x,4));}
        if(style==VehicleAttackSight.SightStyle.Abrams)for(int i=0;i<24;i++){float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;Line(mesh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*4,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*4);}
        if(style==VehicleAttackSight.SightStyle.RocketDirector){Line(mesh,new Vector2(-35,35),new Vector2(35,35));Line(mesh,new Vector2(-35,-35),new Vector2(35,-35));}
    }
    static void Line(UnityEngine.UI.VertexHelper mesh,Vector2 a,Vector2 b)
    {
        Segment(mesh,a,b,3,new Color(0,0,0,.5f));Segment(mesh,a,b,1.3f,Color.white);
    }
    static void Segment(UnityEngine.UI.VertexHelper mesh,Vector2 a,Vector2 b,float width,Color tint)
    {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int first=mesh.currentVertCount;
        foreach(var p in new[]{a-n,a+n,b+n,b-n}){var v=UnityEngine.UIVertex.simpleVert;v.position=p;v.color=tint;mesh.AddVert(v);}mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
    }
}
