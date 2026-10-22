using System;
using UnityEngine;

// Experimental AI pilot: reads telemetry and submits ordinary stick/pedal/collective inputs.
// The existing vehicle motors alone simulate surfaces, forces and collisions.
[Serializable]
public sealed class DirectPilot
{
    [Min(.1f)] public float angleResponse=3;
    [Range(0,1)] public float rateDamping=.3f;
    [Range(10,80)] public float maximumBank=65;
    [Min(.1f)] public float velocityResponse=.9f;
    [Min(.1f)] public float climbResponse=2.8f;
    float turnSide;
    public void Reset()=>turnSide=0;

    public VehicleInput ReadInput(Vehicle vehicle,VehicleInput intent)
    {
        if(intent.directFlight)return intent;
        if(!intent.directionalFlight)
        {
            intent.heading=intent.movementHeading=vehicle.Body.rotation.eulerAngles.y;
            intent.viewPitch=Mathf.DeltaAngle(0,vehicle.Body.rotation.eulerAngles.x);
        }
        var input=vehicle.motor is JetMotor jet?FlyJet(vehicle,jet,intent)
            :vehicle.motor is HelicopterMotor helicopter?FlyHelicopter(vehicle,helicopter,intent):intent;
        input.directFlight=true;input.directionalFlight=false;
        return input;
    }
    VehicleInput FlyJet(Vehicle vehicle,JetMotor motor,VehicleInput input)
    {
        var body=vehicle.Body;
        Vector3 desired=Quaternion.Euler(input.viewPitch,input.heading,0)*Vector3.forward;
        Vector3 local=Quaternion.Inverse(body.rotation)*desired;
        Vector3 angular=Quaternion.Inverse(body.rotation)*body.angularVelocity*Mathf.Rad2Deg;
        float headingError=Mathf.DeltaAngle(body.rotation.eulerAngles.y,input.heading);
        if(Mathf.Abs(headingError)>145)
        {
            if(turnSide==0)turnSide=Mathf.Sign(headingError);
            headingError=Mathf.Abs(headingError)*turnSide;
        }
        else turnSide=Mathf.Abs(headingError)>2?Mathf.Sign(headingError):0;
        Vector3 levelUp=Vector3.ProjectOnPlane(Vector3.up,vehicle.transform.forward);
        float roll=levelUp.sqrMagnitude>.001f?Vector3.SignedAngle(levelUp,vehicle.transform.up,vehicle.transform.forward):0;
        float bank=-Mathf.Clamp(headingError*1.3f,-maximumBank,maximumBank);
        // Once the target is ahead, unload the bank and center with the elevator/rudder.
        // Larger pursuit turns and collision avoidance retain normal bank authority.
        if(input.aim && Mathf.Abs(headingError)<25)bank*=.25f;
        float pitchError=-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
        float yawError=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
        if(Mathf.Abs(headingError)>145)yawError=Mathf.Abs(yawError)*turnSide;
        float response=input.aim?2.5f:1.5f;
        float damping=input.aim?.7f:rateDamping;
        input.pitchRate=Mathf.Clamp((pitchError*angleResponse*response-angular.x*damping)/Mathf.Max(1,motor.pitchRate),-1,1);
        input.yaw=Mathf.Clamp((yawError*angleResponse*response-angular.y*damping)/Mathf.Max(1,motor.Grounded?motor.groundSteeringRate:motor.yawRate),-1,1);
        input.move.x=motor.Grounded?0:-Axis(Mathf.DeltaAngle(roll,bank),angular.z,motor.rollRate);
        if(motor.Grounded && motor.Airspeed<motor.rotationSpeed)input.pitchRate=0;
        return input;
    }
    VehicleInput FlyHelicopter(Vehicle vehicle,HelicopterMotor motor,VehicleInput input)
    {
        var body=vehicle.Body;
        float gravity=Physics.gravity.magnitude*motor.flightForceMultiplier;
        Quaternion heading=Quaternion.Euler(0,input.movementHeading,0);
        Vector3 velocity=heading*new Vector3(input.move.x,0,input.move.y)*motor.maxSpeed;
        Vector3 acceleration=Vector3.ProjectOnPlane(velocity-body.linearVelocity,Vector3.up)*velocityResponse;
        Vector3 localVelocity=Quaternion.Inverse(body.rotation)*body.linearVelocity;
        // Account for the motor's drag when choosing cyclic; sustained forward flight
        // needs a small forward tilt as well as collective to support the tilted rotor.
        Vector3 drag=vehicle.transform.forward*localVelocity.z*(motor.forwardDrag+Mathf.Abs(localVelocity.z)*motor.quadraticDrag)
            +vehicle.transform.right*localVelocity.x*(motor.sidewaysDrag+Mathf.Abs(localVelocity.x)*motor.quadraticDrag);
        acceleration+=Vector3.ProjectOnPlane(drag,Vector3.up);
        acceleration=Vector3.ClampMagnitude(acceleration,gravity*Mathf.Tan(motor.maxTilt*Mathf.Deg2Rad));
        Vector3 local=Quaternion.Inverse(Quaternion.Euler(0,input.heading,0))*acceleration;
        float pitch=Mathf.Atan2(local.z,gravity)*Mathf.Rad2Deg;
        float roll=-Mathf.Atan2(local.x,gravity)*Mathf.Rad2Deg;
        if(input.aim)pitch=Mathf.Lerp(pitch,Mathf.Clamp(input.viewPitch,-motor.maxAimPitch,motor.maxAimPitch),1-Mathf.Clamp01(input.move.magnitude));
        Quaternion desired=Quaternion.Euler(pitch,input.heading,roll);
        if(motor.Grounded)desired=body.rotation;
        Quaternion error=desired*Quaternion.Inverse(body.rotation);
        error.ToAngleAxis(out float angle,out Vector3 axis);
        if(angle>180)angle-=360;
        Vector3 angles=Quaternion.Inverse(body.rotation)*(axis.normalized*angle);
        Vector3 angular=Quaternion.Inverse(body.rotation)*body.angularVelocity*Mathf.Rad2Deg;
        input.pitchRate=Axis(angles.x,angular.x,motor.manualPitchRate);
        input.yaw=Axis(angles.y,angular.y,motor.yawRate);
        float lift=input.lift;
        input.move=new Vector2(-Axis(angles.z,angular.z,motor.manualRollRate),0);
        float thrust=(gravity+(lift*motor.climbSpeed-body.linearVelocity.y)*climbResponse)
            /Mathf.Max(.5f,Vector3.Dot(body.rotation*Vector3.up,Vector3.up));
        input.lift=Mathf.Clamp((thrust/gravity-1)/.65f,-1,1);
        return input;
    }
    float Axis(float error,float rate,float limit)=>Mathf.Clamp((error*angleResponse-rate*rateDamping)/Mathf.Max(1,limit),-1,1);
}
