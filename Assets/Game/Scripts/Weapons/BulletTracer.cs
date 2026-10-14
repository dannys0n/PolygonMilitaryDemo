using UnityEngine;
using UnityEngine.Rendering;

// Shared world-only mesh presentation. Shots still resolve once through their existing damage path.
public sealed class BulletTracer : MonoBehaviour
{
    const int Capacity=512;
    struct Trail { public Vector3 origin, direction; public Quaternion rotation; public float distance, start, speed; public bool rendered; }
    readonly Trail[] trails=new Trail[Capacity];
    readonly Matrix4x4[] matrices=new Matrix4x4[Capacity];
    int next;
    Mesh mesh;
    Material material;
    static BulletTracer instance;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()=>instance=null;

    public static int VisualInterval(float rpm)=>rpm<600f?1:rpm<900f?2:rpm<1200f?3:4;
    public static bool ShowVisual(int shot,float rpm)=>shot%VisualInterval(rpm)==0;
    public static void Emit(Vector3 origin,Vector3 target,float speed=900f)
    {
        Vector3 delta=target-origin;
        if(delta.sqrMagnitude<.01f)return;
        if(!instance)
        {
            var prefab=Resources.Load<GameObject>("BulletTracer");
            if(!prefab)return;
            instance=new GameObject("World bullet tracers").AddComponent<BulletTracer>();
            instance.mesh=prefab.GetComponent<MeshFilter>().sharedMesh;
            instance.material=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        }
        int i=instance.next;instance.next=(instance.next+1)%Capacity;
        instance.trails[i]=new Trail {origin=origin,direction=delta.normalized,rotation=Quaternion.LookRotation(delta),
            distance=Mathf.Min(delta.magnitude,1500f),start=Time.time,speed=Mathf.Max(1,speed)};
    }
    void LateUpdate()
    {
        int count=0;
        for(int i=0;i<Capacity;i++)
        {
            var trail=trails[i];if(trail.distance<=0)continue;
            float head=(Time.time-trail.start)*trail.speed;
            // Keep the first segment visible for one rendered frame, even above 1 km/s.
            if(!trail.rendered){head=Mathf.Min(trail.distance,6f);trails[i].rendered=true;}
            head=Mathf.Max(.1f,head);
            float end=Mathf.Min(head,trail.distance);
            float tail=Mathf.Max(.05f,head-6f);
            if(tail>=trail.distance){trails[i].distance=0;continue;}
            float length=end-tail;if(length<=0)continue;
            matrices[count++]=Matrix4x4.TRS(trail.origin+trail.direction*tail,trail.rotation,new Vector3(.035f,.035f,length));
        }
        if(count>0)Graphics.RenderMeshInstanced(new RenderParams(material){shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,lightProbeUsage=LightProbeUsage.Off},mesh,0,matrices,count);
    }
}
