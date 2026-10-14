using UnityEngine;

// An explicit test command supplies ordinary soldier input; it never moves a body directly.
public sealed class TransportTaskInput : SoldierInputSource
{
    public enum Task { Hold, BoardCargo, ExitCargo, MoveTo }
    public Task CurrentTask { get; private set; }
    public bool Completed { get; private set; }
    public Vehicle carrier;
    public Vector3 localGoal;
    Vector3 worldGoal;
    public void MoveTo(Vector3 goal){CurrentTask=Task.MoveTo;worldGoal=goal;Completed=false;}
    SoldierLife life;
    float nextJump;
    bool falling;
    Rigidbody departed;
    void Awake()=>life=GetComponent<SoldierLife>();
    public void Command(Task task,Vehicle vehicle,Vector3 goal){CurrentTask=task;carrier=vehicle;localGoal=goal;Completed=false;falling=false;departed=vehicle?vehicle.Body:null;nextJump=0;}
    public override SoldierInputFrame ReadInput()
    {
        var frame=new SoldierInputFrame();if(Completed||CurrentTask==Task.Hold||life.health.IsDead)return frame;
        if(CurrentTask==Task.MoveTo)
        {
            Vector3 travelDelta=Vector3.ProjectOnPlane(worldGoal-transform.position,Vector3.up);
            if(travelDelta.magnitude<.6f){Completed=true;CurrentTask=Task.Hold;return frame;}
            float heading=Mathf.Atan2(travelDelta.x,travelDelta.z)*Mathf.Rad2Deg;
            frame.lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(life.look.Yaw,heading),-120*Time.deltaTime,120*Time.deltaTime),0);
            Vector3 localMove=Quaternion.Inverse(Quaternion.Euler(0,life.look.Yaw,0))*travelDelta.normalized;
            frame.move=new Vector2(localMove.x,localMove.z);return frame;
        }
        if(CurrentTask==Task.ExitCargo&&(!carrier||carrier.IsDestroyed))falling=true;
        if(falling)
        {
            if(life.controller.isGrounded&&(!departed||life.controller.Support!=departed)){Completed=true;CurrentTask=Task.Hold;}
            return frame;
        }
        var local=carrier.transform.InverseTransformPoint(transform.position);
        bool supported=life.controller.Support==carrier.Body;
        if(CurrentTask==Task.BoardCargo&&supported&&Vector3.ProjectOnPlane(local-localGoal,Vector3.up).sqrMagnitude<2){Completed=true;return frame;}
        if(CurrentTask==Task.ExitCargo&&(local.z<localGoal.z||local.y<localGoal.y-4))
        {falling=true;return frame;}
        Vector3 delta=carrier.transform.TransformPoint(localGoal)-transform.position;
        float yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
        frame.lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(life.look.Yaw,yaw),-120*Time.deltaTime,120*Time.deltaTime),0);
        var move=Quaternion.Inverse(Quaternion.Euler(0,life.look.Yaw,0))*Vector3.ProjectOnPlane(delta,Vector3.up).normalized;
        frame.move=new Vector2(move.x,move.z);
        if(life.controller.isGrounded&&Time.time>=nextJump&&(CurrentTask==Task.BoardCargo||local.z< -10))
        {frame.jump=true;nextJump=Time.time+.8f;}
        return frame;
    }
}
