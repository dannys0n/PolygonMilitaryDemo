using UnityEngine;

// Input adapters submit intent; this motor alone applies bounded locomotion forces.
[DefaultExecutionOrder(-10)]
public class SoldierMovement : MonoBehaviour
{
    public Animator animator;
    public SoldierPhysicsBody controller;
    public float moveSpeed=4f;
    [Min(0)] public float runSpeed=6f;
    [Min(.01f)] public float firstPersonSprintBlendDuration=.3f;
    [Min(0)] public float jumpHeight=1.2f;
    [Header("Physical movement")]
    [Min(1)] public float groundAcceleration=45,airAcceleration=8;
    [Min(.1f)] public float maximumStepSpeed=2.5f;
    public bool IsRunning { get; private set; }
    public Vector3 Velocity=>controller.Velocity;
    SoldierFirstPersonView view;
    SoldierLook look;
    Vector2 input;
    bool runRequested,jumpRequested,wasGrounded;
    Vector3 airReference;
    float sprintBlend,standingHeight;
    Vector3 standingCenter,eyePosition;
    readonly RaycastHit[] hits=new RaycastHit[24];
    void Awake()
    {
        view=GetComponent<SoldierFirstPersonView>();look=GetComponent<SoldierLook>();
        standingHeight=controller.height;standingCenter=controller.center;
        eyePosition=look.playerCamera.localPosition;
    }
    public void RequestJump(){if(enabled)jumpRequested=true;}
    public void SimulateMovement(Vector2 movement,float deltaTime,bool runRequested=false)
    {if(!enabled)return;input=Vector2.ClampMagnitude(movement,1);this.runRequested=runRequested;}
    public void ResetMovement()
    {
        input=Vector2.zero;runRequested=jumpRequested=wasGrounded=IsRunning=false;
        sprintBlend=0;airReference=Vector3.zero;controller.ResetMotion();
        controller.capsule.height=standingHeight;controller.capsule.center=standingCenter;
        animator.SetFloat("RunBlend",0);animator.SetFloat("MoveX",0);animator.SetFloat("MoveZ",0);animator.SetFloat("VerticalSpeed",0);
        view.armsAnimator.SetFloat("RunBlend",0);
    }
    void FixedUpdate()
    {
        if(!controller.enabled||controller.body.isKinematic)return;
        var body=controller.body;float dt=Time.fixedDeltaTime;
        Quaternion heading=Quaternion.Euler(0,look.Yaw,0);
        // Rotate the dynamic body through the solver so interpolation has physical poses to blend.
        float yawError=Mathf.DeltaAngle(body.rotation.eulerAngles.y,look.Yaw);
        body.angularVelocity=Vector3.up*(yawError*Mathf.Deg2Rad/dt);
        Vector3 direction=heading*new Vector3(input.x,0,input.y);
        AdjustClearance(direction);
        bool grounded=controller.isGrounded;
        IsRunning=runRequested&&input.y>.1f&&grounded&&!view.IsReloading&&!view.IsEquipping&&view.AimBlend<.01f&&!view.WantsToAim;
        float speed=IsRunning?runSpeed:moveSpeed;
        if(wasGrounded&&!grounded)airReference=Vector3.ProjectOnPlane(body.linearVelocity-direction*speed,Vector3.up);
        if(grounded)
        {
            Vector3 wanted=controller.GroundVelocity+Vector3.ProjectOnPlane(direction,controller.GroundNormal).normalized*direction.magnitude*speed;
            Vector3 error=Vector3.ProjectOnPlane(wanted-body.linearVelocity,controller.GroundNormal);
            body.AddForce(Vector3.ClampMagnitude(error/dt,groundAcceleration),ForceMode.Acceleration);
            if(direction.sqrMagnitude>.01f)StepUp(direction);
            if(jumpRequested)
            {
                airReference=Vector3.ProjectOnPlane(controller.GroundVelocity,Vector3.up);
                float target=controller.GroundVelocity.y+Mathf.Sqrt(2*-Physics.gravity.y*jumpHeight);
                body.AddForce(Vector3.up*Mathf.Max(0,target-body.linearVelocity.y),ForceMode.VelocityChange);
                controller.BeginJump();grounded=false;
            }
        }
        else if(input.sqrMagnitude>.001f)
        {
            Vector3 error=airReference+direction*speed-Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up);
            body.AddForce(Vector3.ClampMagnitude(error/dt,airAcceleration),ForceMode.Acceleration);
        }
        jumpRequested=false;wasGrounded=grounded;
        animator.SetFloat("RunBlend",IsRunning?1:0,.15f,dt);
        sprintBlend=Mathf.MoveTowards(sprintBlend,IsRunning?1:0,dt/firstPersonSprintBlendDuration);
        view.armsAnimator.SetFloat("RunBlend",Mathf.SmoothStep(0,1,sprintBlend));
        animator.SetFloat("MoveX",input.x,.05f,dt);animator.SetFloat("MoveZ",input.y,.05f,dt);
        animator.SetBool("Grounded",grounded);animator.SetFloat("VerticalSpeed",controller.velocity.y);
    }
    void StepUp(Vector3 direction)
    {
        Vector3 feet=controller.Feet;direction.Normalize();float reach=controller.radius+.16f;
        int mask=~(1<<gameObject.layer);
        if(!Physics.Raycast(feet+Vector3.up*.08f,direction,out _,reach,mask,QueryTriggerInteraction.Ignore))return;
        Vector3 above=feet+direction*reach+Vector3.up*(controller.stepOffset+.1f);
        if(!Physics.Raycast(above,Vector3.down,out var top,controller.stepOffset+.1f,mask,QueryTriggerInteraction.Ignore))return;
        float rise=top.point.y-feet.y;
        if(rise<.02f||rise>controller.stepOffset||Vector3.Angle(top.normal,Vector3.up)>controller.slopeLimit)return;
        Vector3 center=controller.body.position+controller.center+Vector3.up*(rise+.03f);
        float segment=controller.height*.5f-controller.radius;
        int count=Physics.CapsuleCastNonAlloc(center+Vector3.up*segment,center-Vector3.up*segment,
            controller.radius*.95f,direction,hits,reach,mask,QueryTriggerInteraction.Ignore);
        if(count>0)return;
        float target=controller.GroundVelocity.y+maximumStepSpeed;
        controller.body.AddForce(Vector3.up*Mathf.Clamp(target-controller.body.linearVelocity.y,0,groundAcceleration*Time.fixedDeltaTime),ForceMode.VelocityChange);
    }
    void AdjustClearance(Vector3 direction)
    {
        bool crouched=controller.height<standingHeight-.01f;
        Vector3 feet=controller.Feet, ahead=feet+direction.normalized*(controller.radius+.2f);
        int mask=~(1<<gameObject.layer);
        Vehicle cabin=controller.Support?controller.Support.GetComponent<Vehicle>():null;
        // Anticipate the doorway before the standing capsule can cross its threshold.
        if(!cabin)
        {
            float rise=controller.stepOffset+.15f;
            int count=Physics.RaycastNonAlloc(ahead+Vector3.up*rise,Vector3.down,hits,
                rise+.3f,mask,QueryTriggerInteraction.Ignore);
            float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];
                if(hit.normal.y<.6f||hit.distance>=nearest||hit.collider.transform.IsChildOf(transform))continue;
                nearest=hit.distance;
                cabin=hit.collider is WheelCollider?null:hit.collider.GetComponentInParent<Vehicle>();
            }
        }
        bool low=false;
        if(cabin&&cabin.hasRenderedInterior&&cabin.GetComponent<VehicleDoors>())
        {
            int count=Physics.RaycastNonAlloc(ahead+Vector3.up*.35f,Vector3.up,hits,
                standingHeight,mask,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
                if(hits[i].collider.GetComponentInParent<Vehicle>()==cabin
                    &&hits[i].distance+.35f<standingHeight+.05f){low=true;break;}
            // Also catch roof edges beside the center ray.
            low|=Physics.CheckSphere(ahead+Vector3.up*(standingHeight-controller.radius),
                controller.radius*.95f,mask,QueryTriggerInteraction.Ignore);
        }
        if(crouched)
        {
            // Test the whole proposed standing volume before growing into a roof edge.
            float margin=.02f;
            low|=Physics.CheckCapsule(feet+Vector3.up*(controller.radius+margin),
                feet+Vector3.up*(standingHeight-controller.radius-margin),
                Mathf.Max(.01f,controller.radius-margin),mask,QueryTriggerInteraction.Ignore);
        }
        float height=low?Mathf.Max(controller.radius*2,1.15f):standingHeight;
        if(!Mathf.Approximately(controller.height,height))
        {controller.capsule.height=height;controller.capsule.center=new Vector3(standingCenter.x,standingCenter.y-standingHeight*.5f+height*.5f,standingCenter.z);}
    }
    void LateUpdate()
    {
        look.playerCamera.localPosition=Vector3.Lerp(look.playerCamera.localPosition,
            eyePosition+Vector3.down*(standingHeight-controller.height),1-Mathf.Exp(-12*Time.deltaTime));
    }
}
