using UnityEngine;

// Readable numbered callouts point back to their actual stations on the model silhouette.
public class VehicleSeatDiagramGraphic : UnityEngine.UI.MaskableGraphic
{
    Vehicle vehicle;
    int selectedSeat;
    public void SetVehicle(Vehicle value,int selected){vehicle=value;selectedSeat=selected;SetVerticesDirty();}
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();if(!vehicle)return;
        for(int seatIndex=0;seatIndex<vehicle.seats.Length;seatIndex++)
        {
            var seat=vehicle.seats[seatIndex];if(!seat.anchor)continue;Vector3 local=vehicle.transform.InverseTransformPoint(seat.anchor.position);
            Vector2 target=(new Vector2(local.x,local.z)-vehicle.seatDiagramCenter)*(80f/Mathf.Max(.01f,vehicle.seatDiagramHalfExtent));
            Vector2 marker=seat.diagramPosition;float distance=Vector2.Distance(marker,target);
            Color tint=seatIndex==selectedSeat?new Color(1,.55f,.12f,.7f):new Color(1,1,1,.4f);
            Vector3 facing=vehicle.transform.InverseTransformDirection(seat.anchor.forward);
            Vector2 forward=new Vector2(facing.x,facing.z).normalized;
            Vector2 side=new Vector2(-forward.y,forward.x);
            Line(mesh,target+forward*5,target-forward*2+side*3,tint);
            Line(mesh,target+forward*5,target-forward*2-side*3,tint);
            if(distance<15)continue;
            Vector2 start=marker+(target-marker).normalized*13;
            Line(mesh,start,target,tint);
            Line(mesh,target+Vector2.left*2,target+Vector2.right*2,tint);Line(mesh,target+Vector2.down*2,target+Vector2.up*2,tint);
        }
    }
    static void Line(UnityEngine.UI.VertexHelper mesh,Vector2 a,Vector2 b,Color tint)
    {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*.55f;int first=mesh.currentVertCount;
        foreach(var p in new[]{a-n,a+n,b+n,b-n}){var vertex=UnityEngine.UIVertex.simpleVert;vertex.position=p;vertex.color=tint;mesh.AddVert(vertex);}
        mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
    }
}
