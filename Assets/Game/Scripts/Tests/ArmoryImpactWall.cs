using UnityEngine;

// One bounded mesh for dots and area footprints; no object/material allocation per hit.
public sealed class ArmoryImpactWall : MonoBehaviour, ISurfaceImpactReceiver, ISurfaceSpreadReceiver
{
    public Material dotMaterial;
    [Min(.001f)] public float dotRadius = .025f;
    [Min(0f)] public float holdDuration = 2f;
    [Min(.1f)] public float fadeDuration = 20f;
    [Range(1,1024)] public int maximumMarks = 512;
    const int Sides=48, VerticesPerMark=Sides+5, IndicesPerMark=Sides*3+6;
    Mesh marks;
    MeshRenderer markerRenderer;
    Vector3[] vertices;
    Color32[] colors;
    float[] created;
    bool[] areas;
    readonly Vector3[] perimeter=new Vector3[Sides];
    BoxCollider surface;
    int next, count;

    void OnEnable() => SurfaceImpact.Explosion+=ReceiveExplosion;
    void Awake() => EnsureMesh();
    void EnsureMesh()
    {
        if (marks) return;
        surface=GetComponent<BoxCollider>();
        int capacity = Mathf.Clamp(maximumMarks,1,1024);
        vertices=new Vector3[capacity*VerticesPerMark];colors=new Color32[vertices.Length];
        created=new float[capacity];areas=new bool[capacity];
        var uv=new Vector2[vertices.Length];var triangles=new int[capacity*IndicesPerMark];
        for(int i=0;i<capacity;i++)
        {
            int v=i*VerticesPerMark,t=i*IndicesPerMark;
            uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;
            uv[v+4]=Vector2.one*.5f;
            for(int j=0;j<Sides;j++)
            {
                float angle=j*Mathf.PI*2/Sides;
                uv[v+5+j]=Vector2.one*.5f+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.5f;
                triangles[t+j*3]=v+4;triangles[t+j*3+1]=v+5+j;triangles[t+j*3+2]=v+5+(j+1)%Sides;
            }
            // Draw the red center after its blue footprint.
            t+=Sides*3;
            triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;
            triangles[t+3]=v;triangles[t+4]=v+2;triangles[t+5]=v+3;
        }
        marks=new Mesh{name="Armory impact footprints"};marks.MarkDynamic();
        marks.vertices=vertices;marks.uv=uv;marks.colors32=colors;marks.triangles=triangles;
        var display=new GameObject("Impact dots",typeof(MeshFilter),typeof(MeshRenderer));
        display.transform.SetParent(transform,false);display.GetComponent<MeshFilter>().sharedMesh=marks;
        markerRenderer=display.GetComponent<MeshRenderer>();markerRenderer.sharedMaterial=dotMaterial;
        markerRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;markerRenderer.receiveShadows=false;
        if(surface)
        {
            var padding=new Vector3(.004f/Mathf.Abs(transform.lossyScale.x),.004f/Mathf.Abs(transform.lossyScale.y),.004f/Mathf.Abs(transform.lossyScale.z));
            var block=new MaterialPropertyBlock();
            block.SetVector("_ClipMin",surface.center-surface.size*.5f-padding);
            block.SetVector("_ClipMax",surface.center+surface.size*.5f+padding);
            markerRenderer.SetPropertyBlock(block);
        }
        markerRenderer.enabled=false;
    }
    public void ReceiveImpact(Vector3 point,Vector3 normal) => AddMark(point,normal,false);

    public void ReceiveSpreadImpact(Vector3 point,Vector3 normal,Ray aim,float coneDegrees,float range)
    {
        normal=normal.normalized;
        var forward=aim.direction.normalized;
        var right=Vector3.Cross(forward,Mathf.Abs(forward.y)>.9f?Vector3.right:Vector3.up).normalized;
        var up=Vector3.Cross(forward,right);
        float slope=Mathf.Tan(coneDegrees*Mathf.Deg2Rad),planeDistance=Vector3.Dot(point-aim.origin,normal);
        for(int i=0;i<Sides;i++)
        {
            float angle=i*Mathf.PI*2/Sides;
            var direction=forward+slope*(right*Mathf.Cos(angle)+up*Mathf.Sin(angle));
            float denominator=Vector3.Dot(direction,normal);
            float depth=Mathf.Abs(denominator)>.00001f?planeDistance/denominator:-1f;
            if(depth<=0 || depth>range){AddMark(point,normal,false);return;}
            perimeter[i]=aim.origin+direction*depth;
        }
        AddMark(point,normal,true);
    }
    void ReceiveExplosion(Vector3 center,float radius)
    {
        if(!isActiveAndEnabled || radius<=0)return;
        EnsureMesh();
        if(!surface || (surface.ClosestPoint(center)-center).sqrMagnitude>radius*radius)return;
        var scale=transform.lossyScale;
        var size=Vector3.Scale(surface.size,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
        int axis=size.x<size.y?(size.x<size.z?0:2):(size.y<size.z?1:2);
        var local=transform.InverseTransformPoint(center);
        float sign=local[axis]>=surface.center[axis]?1f:-1f;
        local[axis]=surface.center[axis]+surface.size[axis]*.5f*sign;
        var point=transform.TransformPoint(local);
        var direction=Vector3.zero;direction[axis]=sign;
        var normal=transform.TransformDirection(direction).normalized;
        float distance=Vector3.Dot(center-point,normal);
        float footprint=Mathf.Sqrt(Mathf.Max(0,radius*radius-distance*distance));
        if(footprint<=0)return;
        var tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.9f?Vector3.right:Vector3.up).normalized;
        var up=Vector3.Cross(normal,tangent);
        for(int i=0;i<Sides;i++)
        {
            float angle=i*Mathf.PI*2/Sides;
            perimeter[i]=point+footprint*(tangent*Mathf.Cos(angle)+up*Mathf.Sin(angle));
        }
        AddMark(point,normal,true);
    }
    void AddMark(Vector3 point,Vector3 normal,bool area)
    {
        if(!isActiveAndEnabled)return;
        EnsureMesh();
        normal=normal.sqrMagnitude>.00001f?normal.normalized:-transform.forward;
        var tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.9f?Vector3.right:Vector3.up).normalized*dotRadius;
        var up=Vector3.Cross(normal,tangent).normalized*dotRadius;
        int v=next*VerticesPerMark;
        var lifted=point+normal*.003f;
        vertices[v]=transform.InverseTransformPoint(lifted-tangent-up);vertices[v+1]=transform.InverseTransformPoint(lifted+tangent-up);
        vertices[v+2]=transform.InverseTransformPoint(lifted+tangent+up);vertices[v+3]=transform.InverseTransformPoint(lifted-tangent+up);
        vertices[v+4]=transform.InverseTransformPoint(point+normal*.002f);
        for(int j=0;j<Sides;j++)vertices[v+5+j]=area?transform.InverseTransformPoint(perimeter[j]+normal*.002f):vertices[v+4];
        for(int j=0;j<VerticesPerMark;j++)colors[v+j]=j<4?new Color32(255,0,0,255):new Color32(35,120,255,(byte)(area?255:0));
        areas[next]=area;created[next]=Time.time;next=(next+1)%created.Length;count=Mathf.Min(count+1,created.Length);
        marks.vertices=vertices;marks.colors32=colors;marks.RecalculateBounds();markerRenderer.enabled=true;
    }
    public void ClearMarks()
    {
        next=count=0;
        if(!marks)return;
        System.Array.Clear(colors,0,colors.Length);marks.colors32=colors;markerRenderer.enabled=false;
    }
    public float Opacity(float age) => Mathf.Clamp01(1f-Mathf.Max(0f,age-holdDuration)/Mathf.Max(.1f,fadeDuration));
    void Update()
    {
        if(!marks || !markerRenderer.enabled)return;
        bool visible=false;
        for(int i=0;i<count;i++)
        {
            byte alpha=(byte)Mathf.RoundToInt(Opacity(Time.time-created[i])*255f);visible|=alpha>0;
            for(int j=0;j<VerticesPerMark;j++)colors[i*VerticesPerMark+j].a=j<4||areas[i]?alpha:(byte)0;
        }
        marks.colors32=colors;markerRenderer.enabled=visible;
    }
    void OnDisable(){SurfaceImpact.Explosion-=ReceiveExplosion;if(markerRenderer)markerRenderer.enabled=false;}
    void OnDestroy()
    {
        if(!marks)return;
        if(Application.isPlaying)Destroy(marks);else DestroyImmediate(marks);
    }
}
