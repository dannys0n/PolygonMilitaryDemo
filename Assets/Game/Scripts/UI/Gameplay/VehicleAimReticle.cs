using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class VehicleAimReticle : MaskableGraphic
{
    public bool actualAim;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if(actualAim)
        {
            Line(mesh,new Vector2(0,9),new Vector2(9,0));Line(mesh,new Vector2(9,0),new Vector2(0,-9));
            Line(mesh,new Vector2(0,-9),new Vector2(-9,0));Line(mesh,new Vector2(-9,0),new Vector2(0,9));
        }
        else
        {
            Line(mesh,new Vector2(-15,0),new Vector2(-6,0));Line(mesh,new Vector2(6,0),new Vector2(15,0));
            Line(mesh,new Vector2(0,-15),new Vector2(0,-6));Line(mesh,new Vector2(0,6),new Vector2(0,15));
        }
    }
    void Line(VertexHelper mesh,Vector2 a,Vector2 b)
    {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*.9f;int start=mesh.currentVertCount;
        foreach(var p in new[]{a-n,a+n,b+n,b-n}){var v=UIVertex.simpleVert;v.position=p;v.color=color;mesh.AddVert(v);}
        mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
    }
}
