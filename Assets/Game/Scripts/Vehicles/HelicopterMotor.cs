using UnityEngine;

// Tilt directs rotor thrust. A bounded attitude controller supplies game-friendly stability.
// Flight assistance background: Microsoft Flight Simulator SDK [HELICOPTER] / [FUSELAGE_AERODYNAMICS].
public class HelicopterMotor : VehicleMotor
{
    public Transform mainRotor, tailRotor, airframe;
    public SphereCollider[] landingGear;
    [Header("Rotor and flight limits")]
    public float rotorRpm = 420f, spoolTime = 2.5f;
    public float maxTilt = 24f, maxAimPitch = 60f, yawRate = 65f, climbSpeed = 5f, maxSpeed = 32f;
    [Min(1f)] public float flightForceMultiplier = 1.75f;
    public float maxThrustMultiplier = 2.2f;
    [Header("Manual controls")]
    public float manualPitchRate = 70f, manualRollRate = 70f, manualResponse = 16f;
    public float manualMaxAngularAcceleration = 16f;

    [Header("Fuselage drag")]
    public float forwardDrag = .025f, sidewaysDrag = .05f, quadraticDrag = .0015f;
    public float RotorPower { get; private set; }
    public bool Grounded { get; private set; }
    public override bool CanDisembark => Grounded;
    float mainAngle, tailAngle;

    Quaternion mainRest, tailRest;

    void Start()
    {

        mainRest = mainRotor.localRotation;
        tailRest = tailRotor.localRotation;
        // Visual banking belongs to the Rigidbody now, including collision geometry and weapons.
        airframe.localRotation = Quaternion.identity;
        vehicle.Body.centerOfMass = new Vector3(0, 1.25f, .7f);
    }

    public override void Simulate(VehicleInput input, bool powered)
    {
        float dt = Time.fixedDeltaTime;
        RotorPower = Mathf.MoveTowards(RotorPower, powered ? 1f : 0f, dt / spoolTime);
        Grounded = IsOnGround();

        Rigidbody body = vehicle.Body;
        // Extra effective weight balances boosted rotor lift; both modes share this arcade force scale.
        if (body.useGravity) body.AddForce(Physics.gravity * (flightForceMultiplier - 1f),ForceMode.Acceleration);
        Vector3 localVelocity = Quaternion.Inverse(body.rotation) * body.linearVelocity;
        body.AddForce(-(body.rotation * Vector3.forward) * localVelocity.z * (forwardDrag + Mathf.Abs(localVelocity.z) * quadraticDrag)
            - (body.rotation * Vector3.right) * localVelocity.x * (sidewaysDrag + Mathf.Abs(localVelocity.x) * quadraticDrag), ForceMode.Acceleration);
        if (!powered) return;
        float gravity = Physics.gravity.magnitude * flightForceMultiplier;
        Vector3 rotorUp = body.rotation * Vector3.up;
        Vector3 localRates = new Vector3(Mathf.Clamp(input.move.y + input.pitchRate, -1, 1) * manualPitchRate,
            Mathf.Clamp(input.yaw, -1, 1) * yawRate, -Mathf.Clamp(input.move.x, -1, 1) * manualRollRate) * Mathf.Deg2Rad;
        if (Grounded) localRates = Vector3.zero;
        Vector3 angularAcceleration = Vector3.ClampMagnitude((body.rotation * localRates - body.angularVelocity) * manualResponse,
            manualMaxAngularAcceleration);
        float thrust = gravity * Mathf.Clamp(1 + input.lift * .65f, 0, maxThrustMultiplier);
        body.AddTorque(angularAcceleration * RotorPower, ForceMode.Acceleration);
        body.AddForce(rotorUp * thrust * RotorPower, ForceMode.Acceleration);
        LimitSpeed(body);
    }

    void LimitSpeed(Rigidbody body)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (horizontal.magnitude > maxSpeed)
            body.AddForce(-horizontal.normalized * (horizontal.magnitude - maxSpeed) * 2f, ForceMode.Acceleration);
    }

    bool IsOnGround()
    {
        int contacts = 0;
        foreach (var gear in landingGear)
        {
            Vector3 origin = gear.transform.TransformPoint(gear.center);
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, gear.radius + .06f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<SoldierLife>()) continue;
                if (hit.normal.y > .6f) { contacts++; break; }
            }
        }
        return contacts >= 2;
    }

    void LateUpdate()
    {
        mainAngle = (mainAngle + rotorRpm * 6 * RotorPower * Time.deltaTime) % 360;
        tailAngle = (tailAngle + rotorRpm * 18 * RotorPower * Time.deltaTime) % 360;
        mainRotor.localRotation = mainRest * Quaternion.AngleAxis(mainAngle, Vector3.up);
        tailRotor.localRotation = tailRest * Quaternion.AngleAxis(tailAngle, Vector3.right);
    }
}
