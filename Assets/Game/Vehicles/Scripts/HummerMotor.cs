using System;
using UnityEngine;

// PhysX handles tire slip, suspension and contact forces. Visuals follow GetWorldPose.
public class HummerMotor : VehicleMotor
{
    [Serializable]
    public class Wheel
    {
        public WheelCollider collider; public Transform mesh; public bool steer; public Quaternion rotationOffset;
    }
    public Wheel[] wheels;
    public Vector3 centerOfMass = new Vector3(0, .35f, 0);
    public float motorTorque = 2600f, brakeTorque = 8000f, maxSpeed = 25f;
    public float steeringResponse = 500f, boostMultiplier = 1.4f;
    public float engineBrakeTorque = 1000f;
    public float aerodynamicDrag = .8f, maxReverseSpeed = 10f;
    public float steeringFalloffSpeed = 30f, yawAssist = 8f;
    public float maxYawRate = 2.8f, maxCorneringAcceleration = 22f;
    float steeringAngle;
    public float lowSpeedSteering = 60f, highSpeedSteering = 60f, antiRoll = 6500f;
    public override bool CanDisembark => wheels[0].collider.isGrounded || wheels[1].collider.isGrounded;
    void Start()
    {
        vehicle.Body.centerOfMass = centerOfMass;
        foreach (var w in wheels)
            w.collider.ConfigureVehicleSubsteps(5f, 5, 8);
    }
    public override void Simulate(VehicleInput input, bool powered)
    {
        float speed = Vector3.Dot(vehicle.Body.linearVelocity, transform.forward);
        float steer = Mathf.Lerp(lowSpeedSteering, highSpeedSteering, Mathf.Clamp01(Mathf.Abs(speed) / steeringFalloffSpeed));
        steeringAngle = Mathf.MoveTowards(steeringAngle, input.move.x * steer, steeringResponse * Time.fixedDeltaTime);
        float speedLimit = input.move.y < 0 ? maxReverseSpeed : maxSpeed * (input.boost ? 1.2f : 1f);
        bool braking = !powered || input.brake || (Mathf.Abs(speed) > 1f && input.move.y * speed < -.1f);
        foreach (var w in wheels)
        {
            w.collider.steerAngle = w.steer ? steeringAngle : 0;
            w.collider.brakeTorque = braking || (input.handbrake && !w.steer) ? brakeTorque
                : Mathf.Abs(input.move.y) < .05f  ? engineBrakeTorque : 0;
            w.collider.motorTorque = powered && !braking && !input.handbrake && Mathf.Abs(speed) < speedLimit
                ? input.move.y * motorTorque * (input.boost ? boostMultiplier : 1f) : 0;
        }
        StabilizeAxle(wheels[0].collider, wheels[1].collider, antiRoll);
        StabilizeAxle(wheels[2].collider, wheels[3].collider, antiRoll);
        // Grounded steering assistance improves response without rotating the transform.
        if (powered && GroundedWheels() >= 2)
        {
            float wheelbase = Mathf.Abs(wheels[2].collider.transform.localPosition.z - wheels[0].collider.transform.localPosition.z);
            float yawLimit = Mathf.Min(maxYawRate, maxCorneringAcceleration / Mathf.Max(Mathf.Abs(speed), 1f));
            float desiredYaw = Mathf.Clamp(speed / wheelbase * Mathf.Tan(steeringAngle * Mathf.Deg2Rad), -yawLimit, yawLimit);
            float actualYaw = Vector3.Dot(vehicle.Body.angularVelocity, transform.up);
            vehicle.Body.AddTorque(transform.up * (desiredYaw - actualYaw) * yawAssist, ForceMode.Acceleration);
        }
        vehicle.Body.AddForce(-vehicle.Body.linearVelocity * vehicle.Body.linearVelocity.magnitude * aerodynamicDrag);
    }
    int GroundedWheels()
    {
        int count = 0; foreach (var wheel in wheels) if (wheel.collider.isGrounded) count++; return count;
    }
    void StabilizeAxle(WheelCollider left, WheelCollider right, float stiffness)
    {
        bool l = left.GetGroundHit(out var lh), r = right.GetGroundHit(out var rh);
        float lt = l ? (-left.transform.InverseTransformPoint(lh.point).y - left.radius) / left.suspensionDistance : 1;
        float rt = r ? (-right.transform.InverseTransformPoint(rh.point).y - right.radius) / right.suspensionDistance : 1;
        float force = (lt - rt) * stiffness;
        if (l)
            vehicle.Body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
        if (r)
            vehicle.Body.AddForceAtPosition(right.transform.up * force, right.transform.position);
    }
    void LateUpdate()
    {
        foreach (var w in wheels)
        {
            w.collider.GetWorldPose(out var pos, out var rot);
            w.mesh.SetPositionAndRotation(pos, rot * w.rotationOffset);
        }
    }
}
