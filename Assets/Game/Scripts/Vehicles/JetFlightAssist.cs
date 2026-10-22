using System;
using UnityEngine;

// Input guidance only. It reads flight telemetry and returns ordinary pilot controls.
// Vehicle dispatches the result to JetMotor; this class never applies forces or moves transforms.
[Serializable]
public class JetFlightAssist
{
    public bool assistEnabled = true;
    public float maximumBank = 75, headingToBank = 1.8f;
    [Min(1)] public float assistedBankRate = 100;
    [Min(.1f)] public float aimResponse = 2;
    public Quaternion RequestedAttitude { get; private set; } = Quaternion.identity;
    float turnDirection, bank;
    bool wasActive;

    public VehicleInput ProvideInput(VehicleInput input, JetMotor motor, bool powered)
    {
        var body = motor.vehicle.Body;
        bool active = powered && !input.directFlight && input.directionalFlight &&
            (assistEnabled || (motor.vehicle.Driver && motor.vehicle.Driver.IsAI));
        if (!active)
        {
            wasActive = false;
            RequestedAttitude = body.rotation;
            return input;
        }
        if (!wasActive)
        {
            bank = -Mathf.DeltaAngle(0, body.rotation.eulerAngles.z);
            turnDirection = 0;
        }
        wasActive = true;
        float headingError = Mathf.DeltaAngle(body.rotation.eulerAngles.y, input.heading);
        if (Mathf.Abs(headingError) > 150)
        {
            if (turnDirection == 0) turnDirection = Mathf.Sign(headingError);
            headingError = Mathf.Abs(headingError) * turnDirection;
        }
        else turnDirection = Mathf.Abs(headingError) > 2 ? Mathf.Sign(headingError) : 0;
        float desiredBank = motor.Grounded ? 0 : Mathf.Clamp(headingError * headingToBank, -maximumBank, maximumBank);
        bank = Mathf.MoveTowards(bank, desiredBank, assistedBankRate * Time.fixedDeltaTime);
        float pitch = Mathf.Clamp(input.viewPitch, -85, 85);
        if (motor.Grounded && motor.Airspeed < motor.rotationSpeed) pitch = body.rotation.eulerAngles.x;
        RequestedAttitude = Quaternion.Euler(pitch, input.heading, -bank);
        Vector3 aim = Quaternion.Inverse(body.rotation) * (RequestedAttitude * Vector3.forward);
        float yawError = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
        if (Mathf.Abs(headingError) > 150 && Mathf.Abs(yawError) > 150)
            yawError = Mathf.Abs(yawError) * turnDirection;
        float pitchError = -Mathf.Atan2(aim.y, Mathf.Sqrt(aim.x * aim.x + aim.z * aim.z)) * Mathf.Rad2Deg;
        Vector3 forward = body.rotation * Vector3.forward;
        Vector3 levelUp = Vector3.ProjectOnPlane(Vector3.up, forward);
        float currentRoll = levelUp.sqrMagnitude > .001f
            ? Vector3.SignedAngle(levelUp, body.rotation * Vector3.up, forward) : -bank;
        float rollError = Mathf.DeltaAngle(currentRoll, -bank);
        Vector3 angular = Quaternion.Inverse(body.rotation) * body.angularVelocity * Mathf.Rad2Deg;
        input.pitchRate = AimControl(pitchError, angular.x, motor.pitchRate, motor.elevatorTravel, motor);
        input.yaw = AimControl(yawError, angular.y, motor.yawRate, motor.rudderTravel, motor);
        input.move.x = -AimControl(rollError, angular.z, motor.rollRate, motor.aileronTravel, motor);
        return input;
    }

    float AimControl(float error, float angularRate, float rateLimit, float surfaceTravel, JetMotor motor)
    {
        float anticipation = surfaceTravel / Mathf.Max(1, motor.surfaceResponse);
        float remaining = error - angularRate * anticipation;
        if (Mathf.Abs(remaining) < .2f) remaining = 0;
        float brakingRate = Mathf.Sqrt(2 * motor.maximumAngularAcceleration * Mathf.Rad2Deg * Mathf.Abs(remaining));
        float response=aimResponse*(motor.vehicle.Driver && motor.vehicle.Driver.IsAI?1.5f:1f);
        float desiredRate = Mathf.Clamp(remaining * response, -brakingRate, brakingRate);
        return Mathf.Clamp(desiredRate / Mathf.Max(1, rateLimit), -1, 1);
    }
}
