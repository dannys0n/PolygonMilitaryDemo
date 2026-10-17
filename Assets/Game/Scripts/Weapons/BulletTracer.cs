using UnityEngine;
using UnityEngine.Rendering;

// Shared world-only mesh presentation. Shots still resolve once through their existing damage path.
[DefaultExecutionOrder(1000)]
public sealed class BulletTracer : MonoBehaviour
{
    const int Capacity=512;
    struct Trail { public Vector3 origin, direction, target; public Transform muzzle; public Quaternion rotation; public float distance, start, speed, width, length; public bool rendered; }
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
    public static void Emit(Vector3 origin,Vector3 target,float speed=900f)=>Emit(origin,target,speed,null,.035f,6f);
    public static void Emit(Transform muzzle,Vector3 target,float speed=900f,float width=.035f,float length=6f)
    {
        if(muzzle)Emit(muzzle.position,target,speed,muzzle,width,length);
    }
    static void Emit(Vector3 origin,Vector3 target,float speed,Transform muzzle,float width,float length)
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
        instance.trails[i]=new Trail {origin=origin,target=target,muzzle=muzzle,direction=delta.normalized,rotation=Quaternion.LookRotation(delta),
            distance=Mathf.Min(delta.magnitude,1500f),start=Time.time,speed=Mathf.Max(1,speed),width=Mathf.Max(.001f,width),length=Mathf.Max(.1f,length)};
    }
    void LateUpdate()
    {
        int count=0;
        for(int i=0;i<Capacity;i++)
        {
            var trail=trails[i];if(trail.distance<=0)continue;
            if(!trail.rendered && trail.muzzle)
            {
                // Resolve once after animation, turret aiming and camera/vehicle presentation.
                // Subsequent frames remain in world space; bullets never follow the gun.
                trail.origin=trail.muzzle.position;
                Vector3 delta=trail.target-trail.origin;
                if(delta.sqrMagnitude<.01f){trails[i].distance=0;continue;}
                trail.direction=delta.normalized;trail.rotation=Quaternion.LookRotation(delta);
                trail.distance=Mathf.Min(delta.magnitude,1500f);trail.muzzle=null;
                trails[i]=trail;
            }
            float head=(Time.time-trail.start)*trail.speed;
            // Keep the first segment visible for one rendered frame, even above 1 km/s.
            if(!trail.rendered){head=Mathf.Min(trail.distance,trail.length);trails[i].rendered=true;}
            head=Mathf.Max(.1f,head);
            float end=Mathf.Min(head,trail.distance);
            float tail=Mathf.Max(.05f,head-trail.length);
            if(tail>=trail.distance){trails[i].distance=0;continue;}
            float length=end-tail;if(length<=0)continue;
            matrices[count++]=Matrix4x4.TRS(trail.origin+trail.direction*tail,trail.rotation,new Vector3(trail.width,trail.width,length));
        }
        if(count>0)Graphics.RenderMeshInstanced(new RenderParams(material){shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,lightProbeUsage=LightProbeUsage.Off},mesh,0,matrices,count);
    }
}
