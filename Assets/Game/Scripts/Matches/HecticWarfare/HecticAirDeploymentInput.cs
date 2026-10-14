using UnityEngine;

// Cargo-floor movement is physical input; navigation resumes only after landing.
public sealed class HecticAirDeploymentInput : MonoBehaviour
{
    public Vehicle Carrier { get; private set; }
    public bool IsDropping { get; private set; }
    public bool IsActive { get; private set; }
    SoldierLife life;
    ArenaAIInput infantry;
    Transform exit;
    float leaveAt,startedAt,nextJump, cargoHeight, exitLane;
    Rigidbody departedBody;
    Vector3 landingGoal;
    void Awake(){life=GetComponent<SoldierLife>();infantry=GetComponent<ArenaAIInput>();}
    void OnEnable()
    {
        if(!life)life=GetComponent<SoldierLife>();
        if(life){life.Respawned+=ResetDeployment;life.health.Died+=OnDied;}
    }
    void OnDisable()
    {
        if(life){life.Respawned-=ResetDeployment;life.health.Died-=OnDied;}
        ResetDeployment();
    }
    void OnDied(GameObject source)=>ResetDeployment();
    public void ResetDeployment(){Carrier=null;exit=null;departedBody=null;IsDropping=false;IsActive=false;}
    public void Begin(Vehicle carrier,Transform exitPoint,Vector3 goal)
    {
        IsActive=true;Carrier=carrier;exit=exitPoint;landingGoal=goal;leaveAt=Time.time+1;startedAt=Time.time;IsDropping=false;
        departedBody=carrier.Body;nextJump=0;
        var localPosition=carrier.transform.InverseTransformPoint(transform.position);
        cargoHeight=localPosition.y;exitLane=Mathf.Clamp(localPosition.x,-.7f,.7f);
        if(infantry)infantry.agent.enabled=false;
    }
    public bool TryRead(out SoldierInputFrame input)
    {
        input=default;
        if(!life||life.health.IsDead){ResetDeployment();return false;}
        if(!IsActive)return false;
        // A destroyed/deregistered aircraft can disappear before this input tick.
        if(!Carrier&&!IsDropping)IsDropping=true;
        if(Carrier&&Carrier.IsDestroyed){Carrier=null;IsDropping=true;}
        if(Carrier)
        {
            if(Time.time<leaveAt)return true;
            Vector3 exitLocal=exit?Carrier.transform.InverseTransformPoint(exit.position):new Vector3(0,cargoHeight,-22);
            exitLocal.x+=exitLane;
            Vector3 destination=Carrier.transform.TransformPoint(exitLocal);
            Vector3 delta=destination-transform.position;
            Vector3 localPosition=Carrier.transform.InverseTransformPoint(transform.position);
            // Render bounds exclude parts of the cargo floor and cannot define an exit.
            // Finish the walk only beyond the rear waypoint or after falling below the deck.
            if(localPosition.z<exitLocal.z || localPosition.y<cargoHeight-3)
            {Carrier=null;IsDropping=true;}
            else
            {
                float yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
                input.lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(life.look.Yaw,yaw),-120*Time.deltaTime,120*Time.deltaTime),0);
                Vector3 local=Quaternion.Inverse(Quaternion.Euler(0,life.look.Yaw,0))*Vector3.ProjectOnPlane(delta,Vector3.up).normalized;
                input.move=new Vector2(local.x,local.z);
                // The floor/ramp seam is raised; use the ordinary jump input to clear it.
                if(localPosition.z<-10 && life.controller.isGrounded && Time.time>=nextJump)
                {input.jump=true;nextJump=Time.time+.8f;}
                return true;
            }
        }
        if(!IsDropping)return false;
        if(life.controller.isGrounded && (!departedBody || life.controller.Support!=departedBody)){ResetDeployment();return false;}
        Vector3 direction=landingGoal-transform.position;
        float heading=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
        input.lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(life.look.Yaw,heading),-90*Time.deltaTime,90*Time.deltaTime),0);
        // Preserve aircraft momentum; air steering supplies only the normal bounded motor input.
        input.move=Vector2.up*.2f;
        if(Time.time-startedAt>35){life.health.ApplyDamage(life.health.maxHealth,null);ResetDeployment();}
        return true;
    }
}
