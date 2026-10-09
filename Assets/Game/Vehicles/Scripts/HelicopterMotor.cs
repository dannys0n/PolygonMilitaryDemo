using UnityEngine;

// Assisted flight: lift and attitude torques act on the Rigidbody; no transform-driven flight.
public class HelicopterMotor : VehicleMotor
{
    public Transform mainRotor, tailRotor, airframe;
    public float travelSpeed = 18f, travelResponse = 2f;
    public float rotorRpm = 420f, spoolTime = 2.5f;
    public float maxTilt = 16f, yawRate = 55f, climbSpeed = 5f, maxSpeed = 24f;
    public float attitudeSpring = 10f, attitudeDamping = 5f, verticalResponse = 3f;
    public float RotorPower { get; private set; }
    public override bool CanDisembark => Grounded;
    public bool Grounded { get; private set; }
    float heading;
    Quaternion mainRest, tailRest;
    float mainAngle, tailAngle;
    void Start()
    {
        heading = transform.eulerAngles.y;
        mainRest = mainRotor.localRotation;
        tailRest = tailRotor.localRotation;
        vehicle.Body.centerOfMass = new Vector3(0, 1.25f, .7f);
    }
    public override void Simulate(VehicleInput input, bool powered)
    {
        RotorPower = Mathf.MoveTowards(RotorPower, powered ? 1 : 0, Time.fixedDeltaTime / spoolTime);
        Grounded = false;
        foreach (var hit in Physics.RaycastAll(transform.position + Vector3.up * .6f, Vector3.down, 1.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform))
            {
                Grounded = true;
                break;
            }
        if (!powered && RotorPower < .1f)
        {
            heading = transform.eulerAngles.y;
            return;
        }
        if (input.directionalFlight)
        {
            heading = input.heading;
            Quaternion wanted = Quaternion.Euler(input.viewPitch * .2f, heading, 0);
            Quaternion rotationError = wanted * Quaternion.Inverse(vehicle.Body.rotation);
            rotationError.ToAngleAxis(out float degrees, out Vector3 rotationAxis);
            if (degrees > 180) degrees -= 360;
            vehicle.Body.AddTorque((rotationAxis.normalized * degrees * Mathf.Deg2Rad * attitudeSpring
                - vehicle.Body.angularVelocity * attitudeDamping) * RotorPower, ForceMode.Acceleration);
            Vector3 targetVelocity = wanted * new Vector3(input.move.x * travelSpeed, input.lift * climbSpeed, input.move.y * travelSpeed);
            vehicle.Body.AddForce(((targetVelocity - vehicle.Body.linearVelocity) * travelResponse + Vector3.up * Physics.gravity.magnitude) * RotorPower, ForceMode.Acceleration);
            airframe.localRotation = Quaternion.Slerp(airframe.localRotation,
                Quaternion.Euler(20 * input.move.y, 0, -30 * input.move.x), 1 - Mathf.Exp(-2 * Time.fixedDeltaTime));
            return;
        }
        airframe.localRotation = Quaternion.Slerp(airframe.localRotation, Quaternion.identity, 1 - Mathf.Exp(-2 * Time.fixedDeltaTime));
        heading += input.yaw * yawRate * Time.fixedDeltaTime;
        Quaternion desired = Quaternion.Euler(input.move.y * maxTilt, heading, -input.move.x * maxTilt);
        Quaternion error = desired * Quaternion.Inverse(vehicle.Body.rotation);
        error.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180)
            angle -= 360;
        vehicle.Body.AddTorque((axis.normalized * angle * Mathf.Deg2Rad * attitudeSpring
            - vehicle.Body.angularVelocity * attitudeDamping) * RotorPower, ForceMode.Acceleration);
        float vertical = input.lift * climbSpeed;
        float lift = Mathf.Clamp(Physics.gravity.magnitude / Mathf.Max(.6f, Vector3.Dot(transform.up, Vector3.up))
            + (vertical - vehicle.Body.linearVelocity.y) * verticalResponse, 0, Physics.gravity.magnitude * 2f);
        vehicle.Body.AddForce(transform.up * lift * RotorPower, ForceMode.Acceleration);
        Vector3 horizontal = Vector3.ProjectOnPlane(vehicle.Body.linearVelocity, Vector3.up);
        vehicle.Body.AddForce(-horizontal * .22f, ForceMode.Acceleration);
        if (horizontal.magnitude > maxSpeed)
            vehicle.Body.AddForce(-horizontal.normalized * (horizontal.magnitude - maxSpeed) * 3f, ForceMode.Acceleration);
    }
    void LateUpdate()
    {
        mainAngle = (mainAngle + rotorRpm * 6 * RotorPower * Time.deltaTime) % 360;
        tailAngle = (tailAngle + rotorRpm * 18 * RotorPower * Time.deltaTime) % 360;
        mainRotor.localRotation = mainRest * Quaternion.AngleAxis(mainAngle, Vector3.up);
        tailRotor.localRotation = tailRest * Quaternion.AngleAxis(tailAngle, Vector3.right);
    }
}
