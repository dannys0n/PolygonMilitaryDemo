using UnityEngine;

// CharacterControllers do not inherit a Rigidbody platform's motion.
// Carry through collision-tested moves, keeping the soldier upright and unparented.
public class SoldierVehiclePlatform : MonoBehaviour
{
    [Range(20,75)] public float maximumTilt = 50;
    public float crouchedHeight = 1.15f;
    public Vehicle Support { get; private set; }
    CharacterController controller;
    SoldierLook look;
    Vector3 localPoint, previousPoint, momentum, standingCenter, eyePosition;
    float standingHeight;
    readonly RaycastHit[] hits = new RaycastHit[24];

    void Awake()
    {
        controller=GetComponent<CharacterController>();
        // Wheel suspension must never use a soldier as a supporting road surface.
        int characterLayer=LayerMask.NameToLayer("Characters");
        if(characterLayer>=0)
            foreach(var collider in GetComponentsInChildren<Collider>(true))
                collider.gameObject.layer=characterLayer;
        look=GetComponent<SoldierLook>();
        standingHeight=controller.height;
        standingCenter=controller.center;
        if(look)eyePosition=look.playerCamera.localPosition;
    }

    public void BeginStep(Vector3 movement, float dt, bool jumping)
    {
        if(Support)
        {
            var body=Support.Body;
            bool detach=Support.IsDestroyed || !body || jumping || Vector3.Angle(Support.transform.up,Vector3.up)>maximumTilt;
            if(detach)
            {
                if(body)momentum=body.GetPointVelocity(transform.position);
                Support=null;
            }
            else
            {
                Vector3 current=Support.transform.TransformPoint(localPoint);
                controller.Move(current-previousPoint);
            }
        }
        if(!Support && momentum.sqrMagnitude>.001f)
        {
            // A separate horizontal Move can clear isGrounded; inspect it before carrying momentum.
            if(controller.isGrounded && !jumping)momentum=Vector3.zero;
            else
            {
                controller.Move(momentum*dt);
                momentum=Vector3.MoveTowards(momentum,Vector3.zero,dt*1.5f);
            }
        }
        AdjustCabinClearance(movement,dt);
    }

    void AdjustCabinClearance(Vector3 movement,float dt)
    {
        float bottom=standingCenter.y-standingHeight*.5f;
        Vector3 feet=transform.position+Vector3.up*bottom;
        // Check the floor beyond the threshold before lowering for a doorway.
        Vector3 ahead=feet+movement.normalized*(controller.radius+.2f);
        Vehicle cabin=Support ? Support : CabinFloorAhead(ahead);
        Vector3 origin=ahead+Vector3.up*.35f;
        int count=Physics.RaycastNonAlloc(origin,Vector3.up,hits,standingHeight,~0,QueryTriggerInteraction.Ignore);
        bool low=false;
        // Cabin crouching is only available on a vehicle floor, never from underneath a chassis.
        bool insideCabin=cabin && cabin.hasRenderedInterior && cabin.GetComponent<VehicleDoors>();
        for(int i=0;insideCabin && i<count;i++)
        {
            var hit=hits[i];
            if(hit.collider.transform.IsChildOf(transform))continue;
            if(hit.collider.GetComponentInParent<Vehicle>()==cabin && hit.distance+.35f<standingHeight+.05f){low=true;break;}
        }
        // Check directly overhead before allowing a full-height capsule again.
        count=Physics.RaycastNonAlloc(feet+Vector3.up*.35f,Vector3.up,hits,standingHeight,~0,QueryTriggerInteraction.Ignore);
        bool alreadyCrouched=controller.height<standingHeight-.01f;
        for(int i=0;alreadyCrouched && i<count;i++)
            if(!hits[i].collider.transform.IsChildOf(transform) && hits[i].distance+.35f<standingHeight+.05f)low=true;
        float height=low?Mathf.Max(controller.radius*2,crouchedHeight):standingHeight;
        controller.height=height;
        controller.center=new Vector3(standingCenter.x,bottom+height*.5f,standingCenter.z);
        if(look)look.playerCamera.localPosition=Vector3.Lerp(look.playerCamera.localPosition,
            eyePosition+Vector3.down*(standingHeight-height),1-Mathf.Exp(-12*dt));
    }

    Vehicle CabinFloorAhead(Vector3 feet)
    {
        float rise=controller.stepOffset+.15f;
        int count=Physics.RaycastNonAlloc(feet+Vector3.up*rise,Vector3.down,hits,
            rise+.3f,~0,QueryTriggerInteraction.Ignore);
        Vehicle floor=null;
        float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var hit=hits[i];
            if(hit.collider.transform.IsChildOf(transform) || hit.normal.y<.6f || hit.distance>=nearest)continue;
            nearest=hit.distance;
            floor=hit.collider is WheelCollider ? null : hit.collider.GetComponentInParent<Vehicle>();
        }
        return floor;
    }

    public void EndStep(bool jumping)
    {
        if(jumping){Support=null;return;}
        Vector3 feet=transform.TransformPoint(controller.center)-Vector3.up*(controller.height*.5f);
        int count=Physics.SphereCastNonAlloc(feet+Vector3.up*.22f,controller.radius*.7f,
            Vector3.down,hits,.5f,~0,QueryTriggerInteraction.Ignore);
        Vehicle next=null;
        float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var hit=hits[i];
            if(hit.collider.transform.IsChildOf(transform) || hit.normal.y<.6f)continue;
            var vehicle=hit.collider.GetComponentInParent<Vehicle>();
            if(hit.distance>=nearest)continue;
            nearest=hit.distance;
            // Ground nearer than a vehicle wins, rather than reattaching to a nearby chassis.
            next=vehicle && !vehicle.IsDestroyed && Vector3.Angle(vehicle.transform.up,Vector3.up)<=maximumTilt
                ? vehicle : null;
        }
        if(Support && !next && Support.Body)momentum=Support.Body.GetPointVelocity(transform.position);
        Support=next;
        if(!Support && controller.isGrounded)momentum=Vector3.zero;
        if(Support)
        {
            localPoint=Support.transform.InverseTransformPoint(transform.position);
            previousPoint=transform.position;
            momentum=Vector3.zero;
        }
    }

    public void ResetSupport()
    {
        Support=null;momentum=Vector3.zero;
        if(!controller)return;
        controller.height=standingHeight;controller.center=standingCenter;
        if(look)look.playerCamera.localPosition=eyePosition;
    }
}
