using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasRenderer))]
public class VehicleDiagramGraphic : MaskableGraphic
{
    public enum Drawing { Horizon = 2, Tape = 3, Heading = 4, AircraftReference = 5, Speed = 6, PitchLadder = 7, Bank = 8, VerticalSpeed = 9, FlightPath = 10, HullLevel = 11, HoverCue = 12, LevelHorizon = 13, PitchScale = 14 }
    public Drawing drawing;
    public TMP_Text[] tickLabels = System.Array.Empty<TMP_Text>();
    public float tickStep=20, pixelsPerUnit=2;
    float reading;
    Vector2 hoverVelocity;
    int labelCenter=int.MinValue;
    float labelStep=float.NaN;
    Drawing labelDrawing, geometryDrawing;
    float geometryStep=float.NaN, geometryPixelsPerUnit=float.NaN;
    static readonly float[] Sides={-1f,1f};
    static readonly float[] BankDegrees={-60f,-45f,-30f,-20f,-10f,0f,10f,20f,30f,45f,60f};
    public void SetHoverVelocity(Vector2 velocity){var value=Vector2.ClampMagnitude(velocity*4,45);if(hoverVelocity==value)return;hoverVelocity=value;SetVerticesDirty();}
    public void SetReading(float value)
    {
        if(reading!=value||geometryDrawing!=drawing||geometryStep!=tickStep||geometryPixelsPerUnit!=pixelsPerUnit)
        {reading=value;geometryDrawing=drawing;geometryStep=tickStep;geometryPixelsPerUnit=pixelsPerUnit;SetVerticesDirty();}
        float step = drawing == Drawing.Heading ? 30 : tickStep;
        float scale = drawing == Drawing.Heading ? 4 : pixelsPerUnit;
        int center = Mathf.RoundToInt(value / step);
        bool updateLabels=labelCenter!=center||labelStep!=step||labelDrawing!=drawing;
        labelCenter=center;labelStep=step;labelDrawing=drawing;
        for (int i=0;i<tickLabels.Length;i++)
        {
            float tick = (center+i-tickLabels.Length/2)*step;
            float offset = (tick-value)*scale;
            bool heading = drawing == Drawing.Heading;
            if(updateLabels)tickLabels[i].text = (heading ? Mathf.Repeat(tick,360) : tick).ToString("0");
            tickLabels[i].rectTransform.anchoredPosition = heading ? new Vector2(offset,-18) : new Vector2(drawing==Drawing.Speed?-40:35,offset);
            tickLabels[i].gameObject.SetActive(heading ? Mathf.Abs(offset)<rectTransform.rect.width*.5f-16 : (drawing==Drawing.PitchScale || tick>=0) && Mathf.Abs(offset)<75);
        }
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if(drawing==Drawing.LevelHorizon)
        {
            Line(mesh,new Vector2(-130,0),new Vector2(-35,0));Line(mesh,new Vector2(35,0),new Vector2(130,0));
        }
        else if(drawing==Drawing.PitchScale)
        {
            Line(mesh,new Vector2(0,-75),new Vector2(0,75));
            for(int degree=-90;degree<=90;degree+=5){float y=(degree-reading)*2;if(Mathf.Abs(y)>75)continue;Line(mesh,new Vector2(0,y),new Vector2(degree%10==0?12:6,y));}
            Line(mesh,new Vector2(-8,-4),Vector2.zero);Line(mesh,Vector2.zero,new Vector2(-8,4));
        }
        else if(drawing==Drawing.HoverCue)
        {
            foreach(float side in Sides){Line(mesh,new Vector2(side*45,-45),new Vector2(side*45,45));Line(mesh,new Vector2(-45,side*45),new Vector2(45,side*45));}
            Line(mesh,new Vector2(-6,0),new Vector2(6,0));Line(mesh,new Vector2(0,-6),new Vector2(0,6));
            Line(mesh,Vector2.zero,hoverVelocity);
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8,b=(i+1)*Mathf.PI/8;Line(mesh,hoverVelocity+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*4,hoverVelocity+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*4);}
        }
        else if(drawing==Drawing.HullLevel)
        {
            Line(mesh,new Vector2(-48,0),new Vector2(-32,0));Line(mesh,new Vector2(32,0),new Vector2(48,0));
            Vector2 axis=Quaternion.Euler(0,0,-reading)*Vector2.right;
            Line(mesh,-axis*27,axis*27);Line(mesh,new Vector2(0,-22),new Vector2(0,-15));
        }
        else if(drawing==Drawing.Bank)
        {
            float radius=120;foreach(float degree in BankDegrees){float a=(90-degree)*Mathf.Deg2Rad;Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(mesh,d*(radius-8),d*radius);}
            float angle=(90-reading)*Mathf.Deg2Rad;Vector2 marker=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(radius+12);Line(mesh,marker,marker.normalized*(radius+3));
        }
        else if(drawing==Drawing.VerticalSpeed)
        {Line(mesh,new Vector2(0,-75),new Vector2(0,75));for(int i=-2;i<=2;i++)Line(mesh,new Vector2(0,i*35),new Vector2(8,i*35));float y=Mathf.Clamp(reading/2000,-1,1)*70;Line(mesh,new Vector2(-10,y),new Vector2(0,y));}
        else if(drawing==Drawing.FlightPath)
        {for(int i=0;i<24;i++){float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;Line(mesh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*7,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*7);}Line(mesh,new Vector2(-16,0),new Vector2(-7,0));Line(mesh,new Vector2(7,0),new Vector2(16,0));Line(mesh,new Vector2(0,7),new Vector2(0,15));}
        else if(drawing==Drawing.PitchLadder)
        {
            // Positive pitch is solid; negative pitch is dashed, as on aviation HUDs.
            for(int y=-160;y<=160;y+=20)
            {
                float span=y==0?130:70;
                if(y<0){for(float x=35;x<span;x+=12){float end=Mathf.Min(x+7,span);Line(mesh,new Vector2(x,y),new Vector2(end,y));Line(mesh,new Vector2(-x,y),new Vector2(-end,y));}}
                else{Line(mesh,new Vector2(-span,y),new Vector2(-35,y));Line(mesh,new Vector2(35,y),new Vector2(span,y));}
                if(y!=0){float hook=y>0?-5:5;Line(mesh,new Vector2(-span,y),new Vector2(-span,y+hook));Line(mesh,new Vector2(span,y),new Vector2(span,y+hook));}
            }
        }
        else if (drawing == Drawing.Horizon)
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
            float minor=tickStep/4;
            int center=Mathf.FloorToInt(reading/minor);
            for(int i=center-8;i<=center+8;i++)
            {
                float y=(i*minor-reading)*pixelsPerUnit;
                if(i<0||Mathf.Abs(y)>75)continue;
                Line(mesh,new Vector2(0,y),new Vector2(i%4==0?16:8,y));
            }
            Line(mesh,new Vector2(-10,-4),new Vector2(-4,0));Line(mesh,new Vector2(-4,0),new Vector2(-10,4));
        }
    }
    void Line(VertexHelper mesh,Vector2 a,Vector2 b)
    {
        Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized*.8f;int i=mesh.currentVertCount;
        var v=UnityEngine.UIVertex.simpleVert;v.color=color;
        v.position=a-normal;mesh.AddVert(v);v.position=a+normal;mesh.AddVert(v);
        v.position=b+normal;mesh.AddVert(v);v.position=b-normal;mesh.AddVert(v);
        mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
    }
}
