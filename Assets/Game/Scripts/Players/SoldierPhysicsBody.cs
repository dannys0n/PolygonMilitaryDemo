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
    readonly ContactPoint[] contacts=new ContactPoint[32];
    float contactTime=float.NegativeInfinity;
    Vector3 contactNormalSum;
    Rigidbody contactSupport;
    float bestContactUp;
    Collider supportCollider;
    float ungroundUntil;
    public static bool IsMotorCollider(Collider collider)=>collider && collider.TryGetComponent<SoldierPhysicsBody>(out var character) && collider==character.capsule;
    void Awake()
    {
        if(!body)body=GetComponent<Rigidbody>();if(!capsule)capsule=GetComponent<CapsuleCollider>();
        body.constraints=RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ;body.useGravity=true;
        body.linearDamping=0;body.angularDamping=0;body.maxDepenetrationVelocity=8;
        int layer=LayerMask.NameToLayer("Characters");
        // Locomotion and damage queries use separate authored layers. Do not move hitboxes
        // out of weapon masks when configuring the character motor.
        if(layer>=0)foreach(var collider in GetComponentsInChildren<Collider>(true))
            if(!collider.TryGetComponent<SoldierHitRegion>(out _))collider.gameObject.layer=layer;
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
        isGrounded=false;Support=null;ClearGroundContacts();
    }
    void FixedUpdate()=>ProbeGround();
    public void ProbeGround()
    {
        isGrounded=false;Support=null;GroundNormal=Vector3.up;
        if(!enabled || Time.fixedTime<ungroundUntil)return;
        // Consume contacts from the preceding solver step, not a nearby floor prediction.
        bool fresh=Time.fixedTime-contactTime<=Time.fixedDeltaTime*1.5f;
        bool resting=body.IsSleeping()&&supportCollider&&supportCollider.enabled;
        if((fresh||resting)&&contactNormalSum.sqrMagnitude>.001f)
        {
            GroundNormal=contactNormalSum.normalized;
            Support=contactSupport;
            isGrounded=true;
        }
    }
    void OnCollisionEnter(Collision collision)=>CollectGroundContacts(collision);
    void OnCollisionStay(Collision collision)=>CollectGroundContacts(collision);
    void CollectGroundContacts(Collision collision)
    {
        if(!enabled||Time.fixedTime<ungroundUntil)return;
        if(contactTime!=Time.fixedTime)
        {
            contactTime=Time.fixedTime;contactNormalSum=Vector3.zero;
            contactSupport=null;supportCollider=null;bestContactUp=-1;
        }
        int count=collision.GetContacts(contacts);
        float minUp=Mathf.Cos(slopeLimit*Mathf.Deg2Rad);
        for(int i=0;i<count;i++)
        {
            var contact=contacts[i];
            // Only the motor capsule's lower contacts can ground the character.
            if(contact.thisCollider!=capsule||contact.normal.y<minUp
                ||contact.point.y>Feet.y+radius+.05f)continue;
            contactNormalSum+=contact.normal;
            if(contact.normal.y>bestContactUp)
            {
                bestContactUp=contact.normal.y;
                contactSupport=contact.otherCollider.attachedRigidbody;
                supportCollider=contact.otherCollider;
            }
        }
    }
    void ClearGroundContacts()
    {
        contactTime=float.NegativeInfinity;contactNormalSum=Vector3.zero;
        contactSupport=null;supportCollider=null;
    }
    public void BeginJump(){ungroundUntil=Time.fixedTime+.12f;isGrounded=false;Support=null;ClearGroundContacts();}
    public void ResetMotion()
    {
        ungroundUntil=0;isGrounded=false;Support=null;ClearGroundContacts();
        if(body&&!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
    }
}
