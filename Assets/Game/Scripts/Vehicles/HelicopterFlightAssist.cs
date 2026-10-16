using System;
using UnityEngine;

// Produces cyclic, pedal and collective inputs for the shared helicopter motor.
[Serializable]
public class HelicopterFlightAssist
{
    public bool assistEnabled = true;
    public float attitudeSpring = 60, attitudeDamping = 16, verticalResponse = 2.8f;
    public float positionHoldSpring = .6f, positionHoldDamping = 1.5f, maxHoldAcceleration = 18;
    public float reversalTilt = 38;
    public Vector2 reversalSpeedRange = new Vector2(4, 20);
    public float movementVelocityResponse = .8f, cyclicResponseTime = .1f;
    public Quaternion RequestedAttitude { get; private set; } = Quaternion.identity;
    Vector3 holdPosition, cyclicAcceleration;
    bool holdingPosition, wasActive, horizontalHoldCaptured, altitudeHoldCaptured;
    float heading;

    public VehicleInput ProvideInput(VehicleInput input, HelicopterMotor motor, bool powered)
    {
        var vehicle = motor.vehicle;
        var body = vehicle.Body;
        bool active = powered && !input.directFlight && (assistEnabled || (vehicle.Driver && !vehicle.Driver.IsLocal));
        if (!active)
        {
            wasActive = holdingPosition = false;
            RequestedAttitude = body.rotation;
            return input;
        }
        if (!wasActive)
        {
            holdingPosition = false;
            heading = body.rotation.eulerAngles.y;
            cyclicAcceleration = Vector3.zero;
        }
        wasActive = true;
        bool hold = vehicle.Driver && vehicle.Driver.IsLocal && input.move.sqrMagnitude < .001f
            && Mathf.Abs(input.lift) < .01f && !input.aim && !motor.Grounded;
        if (hold && !holdingPosition)
        {
            holdPosition = body.position;
            horizontalHoldCaptured = altitudeHoldCaptured = false;
        }
        if (hold)
        {
            if (!horizontalHoldCaptured)
            {
                holdPosition.x = body.position.x; holdPosition.z = body.position.z;
                horizontalHoldCaptured = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude < .3f;
            }
            if (!altitudeHoldCaptured)
            {
                holdPosition.y = body.position.y;
                altitudeHoldCaptured = Mathf.Abs(body.linearVelocity.y) < .15f;
            }
        }
        holdingPosition = hold;
        float gravity = Physics.gravity.magnitude * motor.flightForceMultiplier;
        float pitch, roll;
        if (input.directionalFlight)
        {
            float desiredHeading = !input.aim && input.move.sqrMagnitude > .001f ? input.movementHeading : input.heading;
            heading = Mathf.MoveTowardsAngle(heading, desiredHeading, motor.yawRate * Time.fixedDeltaTime);
            Vector3 velocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 desiredVelocity = Quaternion.Euler(0, input.movementHeading, 0)
                * new Vector3(input.move.x, 0, input.move.y) * motor.maxSpeed;
            Vector3 acceleration = hold
                ? Vector3.ProjectOnPlane(holdPosition - body.position, Vector3.up) * positionHoldSpring - velocity * positionHoldDamping
                : (desiredVelocity - velocity) * movementVelocityResponse;
            float opposingSpeed = Mathf.Max(0, -Vector3.Dot(velocity, desiredVelocity.normalized));
            float reversal = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(reversalSpeedRange.x, reversalSpeedRange.y, opposingSpeed));
            float tilt = Mathf.Lerp(motor.maxTilt, Mathf.Max(motor.maxTilt, reversalTilt), reversal);
            float tiltAcceleration = gravity * Mathf.Tan(tilt * Mathf.Deg2Rad);
            acceleration = Vector3.ClampMagnitude(acceleration, Mathf.Min(maxHoldAcceleration, tiltAcceleration));
            cyclicAcceleration = Vector3.MoveTowards(cyclicAcceleration, acceleration,
                tiltAcceleration * Time.fixedDeltaTime / Mathf.Max(.01f, cyclicResponseTime));
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, heading, 0)) * cyclicAcceleration;
            pitch = Mathf.Atan2(local.z, gravity) * Mathf.Rad2Deg;
            roll = -Mathf.Atan2(local.x, Mathf.Sqrt(gravity * gravity + local.z * local.z)) * Mathf.Rad2Deg;
            if (input.aim)
            {
                float weight = 1 - Mathf.Clamp01(input.move.magnitude);
                pitch = Mathf.Lerp(pitch, Mathf.Clamp(input.viewPitch, -motor.maxAimPitch, motor.maxAimPitch), weight);
                roll *= 1 - weight;
            }
        }
        else
        {
            heading += input.yaw * motor.yawRate * Time.fixedDeltaTime;
            pitch = input.move.y * motor.maxTilt; roll = -input.move.x * motor.maxTilt;
        }
        RequestedAttitude = Quaternion.Euler(pitch, heading, roll);
        if (motor.Grounded)
        {
            holdingPosition = false;
            heading = body.rotation.eulerAngles.y;
            RequestedAttitude = body.rotation;
        }
        Quaternion error = RequestedAttitude * Quaternion.Inverse(body.rotation);
        error.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180) angle -= 360;
        Vector3 rates = Quaternion.Inverse(body.rotation) * (axis.normalized * angle * Mathf.Deg2Rad)
            * (attitudeSpring / Mathf.Max(.01f, attitudeDamping));
        input.pitchRate = Mathf.Clamp(rates.x / (motor.manualPitchRate * Mathf.Deg2Rad), -1, 1);
        input.yaw = Mathf.Clamp(rates.y / (motor.yawRate * Mathf.Deg2Rad), -1, 1);
        float throttle = input.lift;
        input.move = new Vector2(-Mathf.Clamp(rates.z / (motor.manualRollRate * Mathf.Deg2Rad), -1, 1), 0);
        float desiredClimb = hold ? Mathf.Clamp((holdPosition.y - body.position.y) * positionHoldSpring,
            -motor.climbSpeed, motor.climbSpeed) : throttle * motor.climbSpeed;
        float thrust = (gravity + (desiredClimb - body.linearVelocity.y) * verticalResponse)
            / Mathf.Max(.5f, Vector3.Dot(body.rotation * Vector3.up, Vector3.up));
        // Collective uses the same neutral-hover mapping as manual flight; the motor clamps its limits.
        input.lift = (thrust / Mathf.Max(.01f, gravity) - 1) / .65f;
        return input;
    }
}
