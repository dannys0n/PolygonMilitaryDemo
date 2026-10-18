using UnityEngine;

// One physical capsule owns locomotion contacts; bone rigidbodies remain ragdoll-only.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-15)]
[RequireComponent(typeof(Rigidbody),typeof(CapsuleCollider))]
public sealed class SoldierPhysicsBody : MonoBehaviour
{
    public Rigidbody body;
    public CapsuleCollider capsule;
    [Header("Safety")]
    [Tooltip("High emergency limit in metres per second. Does not add drag or alter normal walking/jumping.")]
    [Min(1)] public float maximumSpeed=150;
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
    bool stepContact;
    float lastGroundedTime=float.NegativeInfinity;
    readonly RaycastHit[] groundHits=new RaycastHit[16];
    const float GroundSkin=.025f;

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
        body.maxLinearVelocity=Mathf.Max(1,maximumSpeed);
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
            contactSupport=null;supportCollider=null;bestContactUp=-1;stepContact=false;
        }
        int count=collision.GetContacts(contacts);
        float minUp=Mathf.Cos(slopeLimit*Mathf.Deg2Rad);
        for(int i=0;i<count;i++)
        {
            var contact=contacts[i];
            // Only the motor capsule's lower contacts can ground the character.
            if(contact.thisCollider!=capsule||contact.point.y>Feet.y+radius+.05f)continue;
            if(contact.point.y<=Feet.y+stepOffset+GroundSkin&&contact.normal.y<minUp)stepContact=true;
            if(contact.normal.y<minUp)continue;
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
        contactSupport=null;supportCollider=null;stepContact=false;
    }
    // Step traversal and support adhesion are part of this motor, not an animation grace timer.
    // Only continuing grounded locomotion can use them; jumps, long falls and fast impulses cannot.
    public void TraverseGround(Vector3 direction,float maximumGroundSpeed)
    {
        bool continuing=Time.fixedTime-lastGroundedTime<=Time.fixedDeltaTime*1.5f;
        if(isGrounded)lastGroundedTime=Time.fixedTime;
        if(Time.fixedTime<ungroundUntil||(!isGrounded&&!continuing)||stepOffset<=0)return;
        Vector3 relative=velocity;
        if(relative.sqrMagnitude>Mathf.Pow(maximumGroundSpeed+2,2)||relative.y>maximumGroundSpeed)return;
        int mask=~(1<<gameObject.layer);
        Vector3 horizontal=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
        if(stepContact&&horizontal.sqrMagnitude>.1f)TryStepUp(horizontal,mask);
        // Flat, settled contacts need no extra query; the collision solver already supports us.
        if(isGrounded&&!stepContact&&GroundNormal.y>.999f&&relative.y<=.05f)return;
        // A supported front foot may cross a riser before the capsule center does.
        // Sample within its footprint, rather than pulling a valid step back to the old tread.
        Vector3 offset=horizontal*radius;
        Vector3 origin=Feet+offset+Vector3.up*(stepOffset+GroundSkin);
        float minUp=Mathf.Cos(slopeLimit*Mathf.Deg2Rad);
        float slopeClearance=radius*(1f/minUp-1f);
        if(!FindWalkable(origin,stepOffset*2+GroundSkin+slopeClearance,mask,out var floor))return;
        Vector3 normal=floor.normal;
        // Convert the forward hit's plane to the center of the rounded capsule foot.
        float gap=Feet.y-floor.point.y-Vector3.Dot(normal,offset)/normal.y
            -radius*(1f/normal.y-1f);
        if(gap < -GroundSkin || gap>stepOffset)return;
        bool physicalGrounded=isGrounded;
        GroundNormal=normal;Support=floor.rigidbody;supportCollider=floor.collider;
        relative=body.linearVelocity-GroundVelocity;
        float currentNormal=Vector3.Dot(relative,normal);
        if(!physicalGrounded||gap>GroundSkin)
        {
            // This velocity constraint also BRAKES descent as the gap closes. Merely
            // adding downward speed overshoots the next tread and penetrates its floor.
            float targetNormal=-Mathf.Min(Mathf.Max(0,gap)*normal.y/Time.fixedDeltaTime,maximumGroundSpeed)
                -Vector3.Dot(Physics.gravity,normal)*Time.fixedDeltaTime;
            body.AddForce(normal*(targetNormal-currentNormal),ForceMode.VelocityChange);
        }
        else if(currentNormal>0)body.AddForce(-normal*currentNormal,ForceMode.VelocityChange);
        isGrounded=true;lastGroundedTime=Time.fixedTime;
    }
    public void AccelerateTowards(Vector3 targetVelocity,Vector3 planeNormal,float acceleration,float deltaTime)
    {
        Vector3 error=Vector3.ProjectOnPlane(targetVelocity-body.linearVelocity,planeNormal);
        body.AddForce(Vector3.ClampMagnitude(error/deltaTime,acceleration),ForceMode.Acceleration);
    }
    bool FindWalkable(Vector3 origin,float distance,int mask,out RaycastHit best)
    {
        best=default;float nearest=float.PositiveInfinity;
        int count=Physics.RaycastNonAlloc(origin,Vector3.down,groundHits,distance,mask,QueryTriggerInteraction.Ignore);
        float minUp=Mathf.Cos(slopeLimit*Mathf.Deg2Rad);
        for(int i=0;i<count;i++)
        {
            var hit=groundHits[i];
            if(hit.distance>=nearest||hit.normal.y<minUp||hit.collider.transform.IsChildOf(transform)
                ||(hit.rigidbody&&hit.rigidbody.TryGetComponent<SoldierPhysicsBody>(out _)))continue;
            nearest=hit.distance;best=hit;
        }
        return nearest<float.PositiveInfinity;
    }
    void TryStepUp(Vector3 direction,int mask)
    {
        // Probe beyond the rounded capsule foot, then verify the complete raised volume.
        float reach=radius+Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude*Time.fixedDeltaTime+GroundSkin;
        Vector3 ahead=Feet+direction*reach;
        int count=Physics.RaycastNonAlloc(ahead+Vector3.up*(stepOffset+GroundSkin),Vector3.down,
            groundHits,stepOffset+GroundSkin*2,mask,QueryTriggerInteraction.Ignore);
        RaycastHit floor=default;float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var hit=groundHits[i];
            if(hit.distance>=nearest||hit.normal.y<Mathf.Cos(slopeLimit*Mathf.Deg2Rad)
                ||hit.collider.transform.IsChildOf(transform)||(hit.rigidbody&&hit.rigidbody.TryGetComponent<SoldierPhysicsBody>(out _)))continue;
            floor=hit;nearest=hit.distance;
        }
        if(nearest==float.PositiveInfinity)return;
        float rise=floor.point.y-Feet.y;
        if(rise<=GroundSkin||rise>stepOffset)return;
        // A continuous slope is handled by tangent locomotion; only an actual low riser steps.
        Vector3 bottom=Feet+Vector3.up*(radius+GroundSkin);
        Vector3 top=Feet+Vector3.up*(height-radius);
        int blocked=Physics.CapsuleCastNonAlloc(bottom,top,radius-GroundSkin,direction,groundHits,
            reach,mask,QueryTriggerInteraction.Ignore);
        bool riser=false;
        for(int i=0;i<blocked;i++)
            if(groundHits[i].normal.y<Mathf.Cos(slopeLimit*Mathf.Deg2Rad)&&!groundHits[i].collider.transform.IsChildOf(transform))riser=true;
        if(!riser)return;
        Vector3 lift=Vector3.up*(rise+GroundSkin);
        if(Physics.CheckCapsule(bottom+lift,top+lift,radius-GroundSkin,mask,QueryTriggerInteraction.Ignore))return;
        // Sweep the raised capsule horizontally as well; never step into an overhang or wall.
        blocked=Physics.CapsuleCastNonAlloc(bottom+lift,top+lift,radius-GroundSkin,direction,groundHits,
            reach,mask,QueryTriggerInteraction.Ignore);
        for(int i=0;i<blocked;i++)if(!groundHits[i].collider.transform.IsChildOf(transform))return;
        body.position+=lift;
    }
    public void BeginJump(){ungroundUntil=Time.fixedTime+.12f;lastGroundedTime=float.NegativeInfinity;isGrounded=false;Support=null;ClearGroundContacts();}
    public void ResetMotion()
    {
        ungroundUntil=0;lastGroundedTime=float.NegativeInfinity;isGrounded=false;Support=null;ClearGroundContacts();
        if(body&&!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
    }
}
