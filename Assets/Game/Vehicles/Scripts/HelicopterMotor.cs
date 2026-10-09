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
    [Header("Stability assistance")]
    public float attitudeSpring = 12f, attitudeDamping = 6f, maxAngularAcceleration = 4f;
    public float verticalResponse = 2.8f, maxThrustMultiplier = 2.2f;
    public bool assistEnabled = true;
    public float positionHoldSpring = .8f, positionHoldDamping = 2.5f, maxHoldAcceleration = 18f;
    Vector3 holdPosition;
    bool holdingPosition;
    public float manualPitchRate = 70f, manualRollRate = 70f, manualResponse = 4f;
    public Quaternion RequestedAttitude { get; private set; }
    [Header("Fuselage drag")]
    public float forwardDrag = .07f, sidewaysDrag = .18f, quadraticDrag = .0035f;
    public float RotorPower { get; private set; }
    public bool Grounded { get; private set; }
    public override bool CanDisembark => Grounded;
    float heading, mainAngle, tailAngle, rotorDiscPitch;
    Quaternion mainRest, tailRest;

    void Start()
    {
        heading = transform.eulerAngles.y;
        RequestedAttitude = vehicle.Body.rotation;
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
        rotorDiscPitch = 0;
        Rigidbody body = vehicle.Body;
        Vector3 localVelocity = transform.InverseTransformDirection(body.linearVelocity);
        body.AddForce(-transform.forward * localVelocity.z * (forwardDrag + Mathf.Abs(localVelocity.z) * quadraticDrag)
            - transform.right * localVelocity.x * (sidewaysDrag + Mathf.Abs(localVelocity.x) * quadraticDrag), ForceMode.Acceleration);
        if (!powered)
        {
            holdingPosition = false;
            heading = transform.eulerAngles.y;
        RequestedAttitude = vehicle.Body.rotation;
            return;
        }

        bool localAssist = assistEnabled && vehicle.Driver && vehicle.Driver.IsLocal;
        bool noMovement = input.move.sqrMagnitude < .001f && Mathf.Abs(input.lift) < .01f;
        bool hold = localAssist && noMovement && !Grounded;
        if (hold && !holdingPosition) holdPosition = body.position;
        holdingPosition = hold;
        bool assisted = assistEnabled || (vehicle.Driver && !vehicle.Driver.IsLocal);
        if (!assisted)
        {
            Vector3 localRates = new Vector3(Mathf.Clamp(input.move.y + input.pitchRate, -1f, 1f) * manualPitchRate,
                input.yaw * yawRate, -input.move.x * manualRollRate) * Mathf.Deg2Rad;
            RequestedAttitude = body.rotation * Quaternion.Euler(localRates * Mathf.Rad2Deg * .25f);
            if (Grounded) localRates = Vector3.zero;
            Vector3 torque = (transform.TransformDirection(localRates) - body.angularVelocity) * manualResponse;
            body.AddTorque(Vector3.ClampMagnitude(torque, maxAngularAcceleration) * RotorPower, ForceMode.Acceleration);
            float collective = Mathf.Clamp(1f + input.lift * .65f, 0, maxThrustMultiplier);
            body.AddForce(transform.up * Physics.gravity.magnitude * collective * RotorPower, ForceMode.Acceleration);
            LimitSpeed(body);
            return;
        }
        rotorDiscPitch = input.directionalFlight ? input.move.y * maxTilt : 0;
        float pitch = input.directionalFlight ? 0 : input.move.y * maxTilt;
        float roll = -input.move.x * maxTilt;
        if (input.directionalFlight)
        {
            heading = Mathf.MoveTowardsAngle(heading, input.heading, yawRate * dt);
            pitch = Mathf.Clamp(pitch + input.viewPitch, -maxAimPitch, maxAimPitch);
        }
        else heading += input.yaw * yawRate * dt;

        RequestedAttitude = Quaternion.Euler(pitch, input.directionalFlight ? input.heading : heading, roll);
        // Do not force the aircraft to bank against its landing gear before takeoff.
        if (Grounded)
        {
            pitch = roll = rotorDiscPitch = 0;
            holdingPosition = false;
            heading = transform.eulerAngles.y;
        RequestedAttitude = vehicle.Body.rotation;
        }
        Quaternion desired = Quaternion.Euler(pitch, heading, roll);
        Quaternion error = desired * Quaternion.Inverse(body.rotation);
        error.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        Vector3 acceleration = axis.normalized * angle * Mathf.Deg2Rad * attitudeSpring
            - body.angularVelocity * attitudeDamping;
        body.AddTorque(Vector3.ClampMagnitude(acceleration, maxAngularAcceleration) * RotorPower, ForceMode.Acceleration);

        // Neutral collective damps climb/descent; tilt still creates genuine horizontal acceleration.
        float desiredClimb = holdingPosition
            ? Mathf.Clamp((holdPosition.y - body.position.y) * positionHoldSpring, -climbSpeed, climbSpeed)
            : input.lift * climbSpeed;
        float verticalLift = Physics.gravity.magnitude + (desiredClimb - body.linearVelocity.y) * verticalResponse;
        Vector3 thrustAxis = Quaternion.AngleAxis(rotorDiscPitch, transform.right) * transform.up;
        float upFraction = Mathf.Max(.5f, Vector3.Dot(thrustAxis, Vector3.up));
        float thrust = Mathf.Clamp(verticalLift / upFraction, 0, Physics.gravity.magnitude * maxThrustMultiplier);
        Vector3 rotorAcceleration = thrustAxis * thrust * RotorPower;
        body.AddForce(rotorAcceleration, ForceMode.Acceleration);
        if (holdingPosition)
        {
            Vector3 errorPosition = Vector3.ProjectOnPlane(holdPosition - body.position, Vector3.up);
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 braking = Vector3.ClampMagnitude(errorPosition * positionHoldSpring
                - horizontalVelocity * positionHoldDamping, maxHoldAcceleration);
            // Counter tilt-driven travel while holding aim; keep the camera/body orientation independent.
            body.AddForce(braking - Vector3.ProjectOnPlane(rotorAcceleration, Vector3.up), ForceMode.Acceleration);
        }

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
        mainRotor.localRotation = mainRest * Quaternion.AngleAxis(rotorDiscPitch, Vector3.right) * Quaternion.AngleAxis(mainAngle, Vector3.up);
        tailRotor.localRotation = tailRest * Quaternion.AngleAxis(tailAngle, Vector3.right);
    }
}
