using UnityEngine;

// One bounded mesh for all marks; no object/material allocation per hit.
public sealed class ArmoryImpactWall : MonoBehaviour, ISurfaceImpactReceiver
{
    public Material dotMaterial;
    [Min(.001f)] public float dotRadius = .025f;
    [Min(0f)] public float holdDuration = 2f;
    [Min(.1f)] public float fadeDuration = 20f;
    [Range(1,1024)] public int maximumMarks = 512;
    Mesh marks;
    MeshRenderer markerRenderer;
    Vector3[] vertices;
    Color32[] colors;
    float[] created;
    int next, count;

    void Awake() => EnsureMesh();
    void EnsureMesh()
    {
        if (marks) return;
        int capacity = Mathf.Clamp(maximumMarks,1,1024);
        vertices = new Vector3[capacity*4]; colors = new Color32[capacity*4]; created = new float[capacity];
        var uv = new Vector2[capacity*4]; var triangles = new int[capacity*6];
        for (int i=0;i<capacity;i++)
        {
            int v=i*4,t=i*6;
            uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;
            triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v;triangles[t+4]=v+2;triangles[t+5]=v+3;
        }
        marks = new Mesh {name="Armory impact dots"};marks.MarkDynamic();
        marks.vertices=vertices;marks.uv=uv;marks.colors32=colors;marks.triangles=triangles;
        var display = new GameObject("Impact dots",typeof(MeshFilter),typeof(MeshRenderer));
        display.transform.SetParent(transform,false);display.GetComponent<MeshFilter>().sharedMesh=marks;
        markerRenderer=display.GetComponent<MeshRenderer>();markerRenderer.sharedMaterial=dotMaterial;
        markerRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;markerRenderer.receiveShadows=false;
        markerRenderer.enabled=false;
    }
    public void ReceiveImpact(Vector3 point,Vector3 normal)
    {
        if (!isActiveAndEnabled) return;
        EnsureMesh();
        normal=normal.sqrMagnitude>.00001f?normal.normalized:-transform.forward;
        Vector3 tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.9f?Vector3.right:Vector3.up).normalized*dotRadius;
        Vector3 up=Vector3.Cross(normal,tangent).normalized*dotRadius;
        point+=normal*.003f;
        int v=next*4;
        vertices[v]=transform.InverseTransformPoint(point-tangent-up);vertices[v+1]=transform.InverseTransformPoint(point+tangent-up);
        vertices[v+2]=transform.InverseTransformPoint(point+tangent+up);vertices[v+3]=transform.InverseTransformPoint(point-tangent+up);
        for(int i=0;i<4;i++)colors[v+i]=new Color32(255,0,0,255);
        created[next]=Time.time;next=(next+1)%created.Length;count=Mathf.Min(count+1,created.Length);
        marks.vertices=vertices;marks.colors32=colors;marks.RecalculateBounds();markerRenderer.enabled=true;
    }
    public void ClearMarks()
    {
        next = count = 0;
        if (!marks) return;
        System.Array.Clear(colors, 0, colors.Length);
        marks.colors32 = colors;
        markerRenderer.enabled = false;
    }
    public float Opacity(float age) => Mathf.Clamp01(1f-Mathf.Max(0f,age-holdDuration)/Mathf.Max(.1f,fadeDuration));
    void Update()
    {
        if (!marks || !markerRenderer.enabled) return;
        bool visible=false;
        for(int i=0;i<count;i++)
        {
            byte alpha=(byte)Mathf.RoundToInt(Opacity(Time.time-created[i])*255f);visible|=alpha>0;
            for(int j=0;j<4;j++)colors[i*4+j].a=alpha;
        }
        marks.colors32=colors;markerRenderer.enabled=visible;
    }
    void OnDisable() { if(markerRenderer)markerRenderer.enabled=false; }
    void OnDestroy()
    {
        if(!marks)return;
        if(Application.isPlaying)Destroy(marks);else DestroyImmediate(marks);
    }
}
