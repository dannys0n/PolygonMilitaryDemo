using UnityEngine;

// One physical capsule owns locomotion contacts; bone rigidbodies remain ragdoll-only.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-15)]
[RequireComponent(typeof(Rigidbody),typeof(CapsuleCollider))]
public sealed class SoldierPhysicsBody : MonoBehaviour
{
    public Rigidbody body;
    public CapsuleCollider capsule;
    [Range(0,80)] public float slopeLimit=50;
    [Range(0,.6f)] public float stepOffset=.3f;
    public bool isGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; }=Vector3.up;
    public Rigidbody Support { get; private set; }
    public Vector3 GroundVelocity => Support ? Support.GetPointVelocity(body.position) : Vector3.zero;
    public Vector3 Velocity => body && !body.isKinematic ? body.linearVelocity : Vector3.zero;
    public Vector3 velocity => Velocity-GroundVelocity;
    public float radius => capsule.radius;
    public float height => capsule.height;
    public Vector3 center => capsule.center;
    public Vector3 Feet => body.position+Vector3.up*(capsule.center.y-capsule.height*.5f);
    readonly RaycastHit[] hits=new RaycastHit[24];
    float ungroundUntil;
    public static bool IsMotorCollider(Collider collider)=>collider && collider.TryGetComponent<SoldierPhysicsBody>(out var character) && collider==character.capsule;
    void Awake()
    {
        if(!body)body=GetComponent<Rigidbody>();if(!capsule)capsule=GetComponent<CapsuleCollider>();
        body.constraints=RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ;body.useGravity=true;
        body.linearDamping=0;body.angularDamping=0;body.maxDepenetrationVelocity=8;
        int layer=LayerMask.NameToLayer("Characters");
        if(layer>=0)foreach(var collider in GetComponentsInChildren<Collider>(true))collider.gameObject.layer=layer;
    }
    void OnEnable()
    {
        if(!body)body=GetComponent<Rigidbody>();if(!capsule)capsule=GetComponent<CapsuleCollider>();
        body.position=transform.position;body.rotation=transform.rotation;
        body.isKinematic=false;body.detectCollisions=true;body.interpolation=RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;capsule.enabled=true;
    }
    void OnDisable()
    {
        if(!body)return;
        if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
        body.collisionDetectionMode=CollisionDetectionMode.Discrete;body.interpolation=RigidbodyInterpolation.None;
        body.isKinematic=true;body.detectCollisions=false;
        if(capsule)capsule.enabled=false;
        isGrounded=false;Support=null;
    }
    void FixedUpdate()=>ProbeGround();
    public void ProbeGround()
    {
        isGrounded=false;Support=null;GroundNormal=Vector3.up;
        if(!enabled || Time.fixedTime<ungroundUntil)return;
        float r=radius*.9f;int mask=~(1<<gameObject.layer);
        int count=Physics.SphereCastNonAlloc(Feet+Vector3.up*(r+.15f),r,Vector3.down,hits,.24f,mask,QueryTriggerInteraction.Ignore);
        float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var hit=hits[i];
            if(hit.collider.transform.IsChildOf(transform)||IsMotorCollider(hit.collider)||hit.distance>=nearest
                ||Vector3.Angle(hit.normal,Vector3.up)>slopeLimit)continue;
            nearest=hit.distance;isGrounded=true;GroundNormal=hit.normal;Support=hit.rigidbody;
        }
    }
    public void BeginJump(){ungroundUntil=Time.fixedTime+.12f;isGrounded=false;Support=null;}
    public void ResetMotion()
    {
        ungroundUntil=0;isGrounded=false;Support=null;
        if(body&&!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
    }
}
