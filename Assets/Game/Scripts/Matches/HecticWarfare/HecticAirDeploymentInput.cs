using UnityEngine;

// Cargo-floor movement is physical input; navigation resumes only after landing.
public sealed class HecticAirDeploymentInput : MonoBehaviour
{
    public Vehicle Carrier { get; private set; }
    public bool IsDropping { get; private set; }
    SoldierLife life;
    ArenaAIInput infantry;
    Transform exit;
    float leaveAt,startedAt;
    Vector3 landingGoal;
    void Awake(){life=GetComponent<SoldierLife>();infantry=GetComponent<ArenaAIInput>();}
    public void Begin(Vehicle carrier,Transform exitPoint,Vector3 goal)
    {
        Carrier=carrier;exit=exitPoint;landingGoal=goal;leaveAt=Time.time+1;startedAt=Time.time;IsDropping=false;
        if(infantry)infantry.agent.enabled=false;
    }
    public bool TryRead(out SoldierInputFrame input)
    {
        input=default;
        if(!life||life.health.IsDead){Carrier=null;IsDropping=false;return false;}
        if(Carrier&&Carrier.IsDestroyed){Carrier=null;IsDropping=true;}
        if(Carrier)
        {
            if(Time.time<leaveAt)return true;
            Vector3 destination=exit?exit.position:Carrier.transform.TransformPoint(new Vector3(0,1,-8));
            Vector3 delta=destination-transform.position;
            bool supported=life.controller.Support==Carrier.Body;
            if(!supported&&!Carrier.NavigationBounds.Contains(Carrier.transform.InverseTransformPoint(transform.position)))
            {Carrier=null;IsDropping=true;}
            else
            {
                float yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
                input.lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(life.look.Yaw,yaw),-120*Time.deltaTime,120*Time.deltaTime),0);
                Vector3 local=Quaternion.Inverse(Quaternion.Euler(0,life.look.Yaw,0))*Vector3.ProjectOnPlane(delta,Vector3.up).normalized;
                input.move=new Vector2(local.x,local.z);
                return true;
            }
        }
        if(!IsDropping)return false;
        if(life.controller.isGrounded){IsDropping=false;return false;}
        Vector3 direction=landingGoal-transform.position;
        float heading=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
        input.lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(life.look.Yaw,heading),-90*Time.deltaTime,90*Time.deltaTime),0);
        // Preserve aircraft momentum; air steering supplies only the normal bounded motor input.
        input.move=Vector2.up*.2f;
        if(Time.time-startedAt>35){life.health.ApplyDamage(life.health.maxHealth,null);IsDropping=false;}
        return true;
    }
}
