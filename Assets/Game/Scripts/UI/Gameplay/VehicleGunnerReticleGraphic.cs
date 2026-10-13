using UnityEngine;
using UnityEngine.UI;

// Redrawn from GPS, EMES-15 and TPD-K1 sight plates; no raster overlays.
// TPD-K1 ammunition ladders are a static reference, not a ballistic solution.
public class VehicleGunnerReticleGraphic : MaskableGraphic
{
    public VehicleAttackSight.SightStyle style;
    public bool orientation, barrelMarker, aperture;
    float traverse;
    public void SetTraverse(float degrees) { traverse = degrees; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if(aperture)
        {
            for(int i=0;i<128;i++)
            {
                float a=i*Mathf.PI/64,b=(i+1)*Mathf.PI/64;
                Vector2 p=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),q=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                int first=mesh.currentVertCount;
                Add(mesh,p*285,Color.black);Add(mesh,p*3000,Color.black);
                Add(mesh,q*3000,Color.black);Add(mesh,q*285,Color.black);
                mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
            }
            return;
        }
        if (barrelMarker)
        {
            Vector2[] points = { new Vector2(0,9), new Vector2(9,0), new Vector2(0,-9), new Vector2(-9,0) };
            for (int i=0;i<4;i++) Stroke(mesh,points[i],points[(i+1)%4],new Color(1,.55f,.12f),1.5f);
            return;
        }
        if (orientation)
        {
            Line(mesh,-13,-22,-13,22); Line(mesh,13,-22,13,22);
            Line(mesh,-13,22,13,22); Line(mesh,-13,-22,13,-22);
            Vector2 direction = Quaternion.Euler(0,0,-traverse)*Vector2.up;
            Stroke(mesh,Vector2.zero,direction*39,Color.white,1.3f);
            Circle(mesh,Vector2.zero,42,Color.white); return;
        }
        switch (style)
        {
            case VehicleAttackSight.SightStyle.Abrams: Western(mesh,true); break;
            case VehicleAttackSight.SightStyle.Leopard: Western(mesh,false); break;
            case VehicleAttackSight.SightStyle.T72: Tpd(mesh); break;
            default:
                Line(mesh,-125,0,-12,0); Line(mesh,12,0,125,0);
                Line(mesh,0,-110,0,-12); Line(mesh,0,12,0,90); break;
        }
    }
    void Western(VertexHelper mesh,bool abrams)
    {
        // The central ring and four short arms are shared, not a continuous cross.
        Color tint = Color.white;
        Circle(mesh,Vector2.zero,abrams?8:4,tint);
        Mark(mesh,-18,0,-8,0,tint); Mark(mesh,8,0,18,0,tint);
        Mark(mesh,0,-18,0,-8,tint); Mark(mesh,0,8,0,18,tint);
        if(abrams) Circle(mesh,Vector2.zero,1.2f,tint);
        if(abrams)
        {
            Mark(mesh,-53,-139,-45,-139,tint);Mark(mesh,-53,-131,-45,-131,tint);
            Mark(mesh,-53,-139,-53,-131,tint);Mark(mesh,-45,-139,-45,-131,tint);
        }
        Mark(mesh,0,36,0,70,tint); Mark(mesh,0,-36,0,-70,tint);
        for(int side=-1;side<=1;side+=2)
        {
            Mark(mesh,side*40,0,side*75,0,tint);
            Mark(mesh,side*90,0,side*125,0,tint);
            Mark(mesh,side*40,30,side*(abrams?75:125),30,tint);
            Mark(mesh,side*40,-30,side*(abrams?75:125),-30,tint);
        }
    }
    void Tpd(VertexHelper mesh)
    {
        // Separate laser ring, pointed lead marks, vertical MG ladder and
        // right-side 2.7 m target-height range estimator on the photographed plate.
        const float x=-15, y=70;
        Circle(mesh,Vector2.zero,4,Color.white);
        Line(mesh,x-9,y-13,x,y); Line(mesh,x,y,x+9,y-13);
        for(int i=-8;i<=8;i++)
        {
            if(i==0)continue; float px=x+i*20;
            Line(mesh,px-3,y-5,px,y+3); Line(mesh,px,y+3,px+3,y-5);
            if(i<8) Line(mesh,px+9,y+3,px+9,y-1);
        }
        Line(mesh,x,y-24,x,-195);
        for(int i=0;i<9;i++)
        {
            float py=45-i*24; Line(mesh,x-4,py,x-1,py);
            Line(mesh,x+2,py,x+5,py+4);
        }
        // Width follows target angular height: h/r, not a decorative staircase.
        Line(mesh,110,-92,205,-92);
        for(int i=0;i<7;i++)
        {
            float px=110+i*15, r=4000-i*500;
            float top=-92+27000/r;
            Line(mesh,px,-92,px,top);
            if(i<6)Line(mesh,px,top,px+15,-92+27000/(r-500));
        }
    }
    static void Line(VertexHelper mesh,float ax,float ay,float bx,float by) => Mark(mesh,ax,ay,bx,by,Color.white);
    static void Mark(VertexHelper mesh,float ax,float ay,float bx,float by,Color tint) => Stroke(mesh,new Vector2(ax,ay),new Vector2(bx,by),tint,1.2f);
    static void Circle(VertexHelper mesh,Vector2 center,float radius,Color tint)
    {
        for(int i=0;i<48;i++)
        {
            float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;
            Stroke(mesh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,tint,1.2f);
        }
    }
    static void Stroke(VertexHelper mesh,Vector2 a,Vector2 b,Color tint,float width)
    {
        Segment(mesh,a,b,width+1.4f,new Color(0,0,0,.65f)); Segment(mesh,a,b,width,tint);
    }
    static void Segment(VertexHelper mesh,Vector2 a,Vector2 b,float width,Color tint)
    {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
        int first=mesh.currentVertCount;
        Add(mesh,a-n,tint); Add(mesh,a+n,tint); Add(mesh,b+n,tint); Add(mesh,b-n,tint);
        mesh.AddTriangle(first,first+1,first+2); mesh.AddTriangle(first,first+2,first+3);
    }
    static void Add(VertexHelper mesh,Vector2 p,Color tint)
    {
        var v=UIVertex.simpleVert; v.position=p; v.color=tint; mesh.AddVert(v);
    }
}
