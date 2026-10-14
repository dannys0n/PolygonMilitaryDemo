using UnityEngine;

// Extra riders enter through the actual doorway using the ordinary movement and jump input.
// Failed/unreachable attempts expire; they never warp or acquire an invisible seat.
public class HecticPhysicalBoarding : MonoBehaviour
{
    public ArenaAIInput infantry;
    public HecticCompanySupervisor supervisor;
    public ArenaVehicleAI transport;
    HecticCompanySupervisor.BoardingOrder order;
    bool active,inside;
    float nextJump;
    public Vehicle Carrier=>inside&&order.vehicle&&!order.vehicle.IsDestroyed?order.vehicle:null;
    public void Begin(HecticCompanySupervisor.BoardingOrder value)
    {if(active&&order.vehicle==value.vehicle)return;order=value;active=true;inside=false;}
    public bool TryRead(out SoldierInputFrame input)
    {
        input=default;if(!active)return false;
        if(!order.vehicle||order.vehicle.IsDestroyed||infantry.life.health.IsDead||Time.time>=order.expires&&!inside)
        {Finish();return false;}
        var v=order.vehicle;
        Vector3 destination=v.transform.TransformPoint(order.localDestination);
        bool supported=infantry.life.controller.Support==v.Body;
        if(supported&&Vector3.ProjectOnPlane(transform.position-destination,Vector3.up).sqrMagnitude<4)
        {inside=true;supervisor.FinishBoarding(transport);}
        if(inside)
        {
            if(!supported&&(transform.position-destination).sqrMagnitude>100){Finish();return false;}
            if(v.Body.linearVelocity.magnitude<3&&supervisor.TryGoal(transport,out var goal)
                &&(Vector3.ProjectOnPlane(v.transform.position-goal,Vector3.up).sqrMagnitude<supervisor.unloadRadius*supervisor.unloadRadius||infantry.IsCombatAlert))
            {
                Vector3 outward=Vector3.ProjectOnPlane(order.entry.position-v.transform.TransformPoint(order.localDestination),Vector3.up).normalized;
                destination=order.entry.position+outward*3;
                if((transform.position-order.entry.position).sqrMagnitude>20){Finish();return false;}
            }
            else return true;
        }
        else if((transform.position-order.entry.position).sqrMagnitude>16)
        {
            infantry.BoardingTarget=order.entry;return false;
        }
        // Near the threshold NavMesh cannot describe the cargo floor. Intent steers physically.
        infantry.BoardingTarget=null;
        Vector3 delta=destination-transform.position;
        float yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
        input.lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(infantry.life.look.Yaw,yaw),-infantry.turnSpeed*Time.deltaTime,infantry.turnSpeed*Time.deltaTime),0);
        Vector3 move=Quaternion.Inverse(Quaternion.Euler(0,infantry.life.look.Yaw,0))*Vector3.ProjectOnPlane(delta,Vector3.up).normalized;
        input.move=new Vector2(move.x,move.z);
        if(!inside&&infantry.life.controller.isGrounded&&Time.time>=nextJump)
        {input.jump=true;nextJump=Time.time+.8f;}
        return true;
    }
    void Finish(){active=inside=false;infantry.BoardingTarget=null;if(supervisor&&transport)supervisor.FinishBoarding(transport);}
}
