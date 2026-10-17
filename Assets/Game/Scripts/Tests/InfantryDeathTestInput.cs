using UnityEngine;

// Ordinary movement inputs for the death lab; no direct locomotion velocity writes.
public sealed class InfantryDeathTestInput : SoldierInputSource
{
    public bool walking;
    SoldierLife life;
    float heading;
    void Awake()=>life=GetComponent<SoldierLife>();
    public override SoldierInputFrame ReadInput()
    {
        if(!walking || life.health.IsDead)return default;
        if(transform.position.z>6)heading=180;
        if(transform.position.z< -6)heading=0;
        return new SoldierInputFrame {move=Vector2.up,
            lookDelta=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(life.look.Yaw,heading),-180*Time.deltaTime,180*Time.deltaTime),0)};
    }
}
