using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasRenderer))]
public class VehicleDiagramGraphic : MaskableGraphic
{
    public enum Drawing { Horizon = 2, Tape = 3, Heading = 4, AircraftReference = 5 }
    public Drawing drawing;
    public TMP_Text[] tickLabels;
    float reading;
    public void SetReading(float value)
    {
        reading = value;
        SetVerticesDirty();
        float step = drawing == Drawing.Heading ? 30 : 20;
        float scale = drawing == Drawing.Heading ? 4 : 2;
        int center = Mathf.RoundToInt(value / step);
        for (int i=0;i<tickLabels.Length;i++)
        {
            float tick = (center+i-tickLabels.Length/2)*step;
            float offset = (tick-value)*scale;
            bool heading = drawing == Drawing.Heading;
            tickLabels[i].text = (heading ? Mathf.Repeat(tick,360) : tick).ToString("0");
            tickLabels[i].rectTransform.anchoredPosition = heading ? new Vector2(offset,-18) : new Vector2(28,offset);
            tickLabels[i].gameObject.SetActive(heading ? Mathf.Abs(offset)<rectTransform.rect.width*.5f-16 : tick>=0 && Mathf.Abs(offset)<75);
        }
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (drawing == Drawing.Horizon)
        {
            for(int y=-160;y<=160;y+=20)
            {
                float span=y==0?130:70;
                Line(mesh,new Vector2(-span,y),new Vector2(-35,y));Line(mesh,new Vector2(35,y),new Vector2(span,y));
            }
        }
        else if (drawing == Drawing.AircraftReference)
        {
            Line(mesh,new Vector2(-28,0),new Vector2(-8,0));Line(mesh,new Vector2(8,0),new Vector2(28,0));
            Line(mesh,new Vector2(-8,0),new Vector2(0,-6));Line(mesh,new Vector2(0,-6),new Vector2(8,0));
        }
        else if (drawing == Drawing.Heading)
        {
            float half = rectTransform.rect.width*.5f;
            Line(mesh,new Vector2(-half,0),new Vector2(half,0));
            int center=Mathf.FloorToInt(reading/10);
            for(int i=center-10;i<=center+10;i++)
            {
                float x=(i*10-reading)*4;
                if(Mathf.Abs(x)>half)continue;
                Line(mesh,new Vector2(x,0),new Vector2(x,i%3==0?-9:-5));
            }
            Line(mesh,new Vector2(-5,9),Vector2.zero);Line(mesh,Vector2.zero,new Vector2(5,9));
        }
        else
        {
            Line(mesh,new Vector2(0,-75),new Vector2(0,75));
            int center=Mathf.FloorToInt(reading/5);
            for(int i=center-8;i<=center+8;i++)
            {
                float y=(i*5-reading)*2;
                if(i<0||Mathf.Abs(y)>75)continue;
                Line(mesh,new Vector2(0,y),new Vector2(i%4==0?16:8,y));
            }
            Line(mesh,new Vector2(-10,-4),new Vector2(-4,0));Line(mesh,new Vector2(-4,0),new Vector2(-10,4));
        }
    }
    void Line(VertexHelper mesh,Vector2 a,Vector2 b)
    {
        Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized*.8f;int i=mesh.currentVertCount;
        foreach(var p in new[]{a-normal,a+normal,b+normal,b-normal}){var v=UIVertex.simpleVert;v.position=p;v.color=color;mesh.AddVert(v);}
        mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
    }
}