using UnityEngine;
using UnityEngine.UI;

// Small vector line drawings stay crisp at any HUD scale; no vehicle render textures.
public class VehicleDiagramGraphic : MaskableGraphic
{
    public enum Drawing { Horizon = 2, Tape = 3 }
    public Drawing drawing;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if(drawing == Drawing.Horizon)
        {
            for(int y=-80;y<=80;y+=40){Line(mesh,new Vector2(-130,y),new Vector2(-35,y));Line(mesh,new Vector2(35,y),new Vector2(130,y));}
            Line(mesh,new Vector2(-12,0),new Vector2(-3,0));Line(mesh,new Vector2(3,0),new Vector2(12,0));
        }
        else
        {
            Line(mesh,new Vector2(0,-75),new Vector2(0,75));for(int y=-75;y<=75;y+=15)Line(mesh,new Vector2(0,y),new Vector2(y%30==0?16:8,y));
        }
    }
    void Line(VertexHelper mesh,Vector2 a,Vector2 b)
    {
        Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized*.8f;int i=mesh.currentVertCount;
        foreach(var p in new[]{a-normal,a+normal,b+normal,b-normal}){var v=UIVertex.simpleVert;v.position=p;v.color=color;mesh.AddVert(v);}
        mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
    }
}
