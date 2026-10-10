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
    [Header("Stability assistance")]
    public float attitudeSpring = 60f, attitudeDamping = 16f, maxAngularAcceleration = 16f;
    public float verticalResponse = 2.8f, maxThrustMultiplier = 2.2f;
    public bool assistEnabled = true;
    public float positionHoldSpring = .6f, positionHoldDamping = 1.5f, maxHoldAcceleration = 18f;
    Vector3 holdPosition;
    bool holdingPosition, previouslyAssisted, horizontalHoldCaptured, altitudeHoldCaptured;
    [Header("Manual controls")]
    public float manualPitchRate = 70f, manualRollRate = 70f, manualResponse = 16f;
    public float manualMaxAngularAcceleration = 16f;
    public Quaternion RequestedAttitude { get; private set; }
    [Header("Fuselage drag")]
    public float forwardDrag = .025f, sidewaysDrag = .05f, quadraticDrag = .0015f;
    public float RotorPower { get; private set; }
    public bool Grounded { get; private set; }
    public override bool CanDisembark => Grounded;
    float heading, mainAngle, tailAngle;
    public float movementVelocityResponse = .8f;
    public float cyclicResponseTime = .10f;
    Vector3 cyclicAcceleration;
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

        Rigidbody body = vehicle.Body;
        // Extra effective weight balances boosted rotor lift; both modes share this arcade force scale.
        if (body.useGravity) body.AddForce(Physics.gravity * (flightForceMultiplier - 1f),ForceMode.Acceleration);
        Vector3 localVelocity = Quaternion.Inverse(body.rotation) * body.linearVelocity;
        body.AddForce(-(body.rotation * Vector3.forward) * localVelocity.z * (forwardDrag + Mathf.Abs(localVelocity.z) * quadraticDrag)
            - (body.rotation * Vector3.right) * localVelocity.x * (sidewaysDrag + Mathf.Abs(localVelocity.x) * quadraticDrag), ForceMode.Acceleration);
        if (!powered)
        {
            holdingPosition = false;
            heading = transform.eulerAngles.y;
        RequestedAttitude = vehicle.Body.rotation;
            return;
        }

        bool assisted = assistEnabled || (vehicle.Driver && !vehicle.Driver.IsLocal);
        if (assisted != previouslyAssisted)
        {
            holdingPosition = false;
            heading = body.rotation.eulerAngles.y;
            cyclicAcceleration = Vector3.zero;
            previouslyAssisted = assisted;
        }
        bool localAssist = assisted && vehicle.Driver && vehicle.Driver.IsLocal;
        bool noMovement = input.move.sqrMagnitude < .001f && Mathf.Abs(input.lift) < .01f;
        bool hold = localAssist && noMovement && !input.aim && !Grounded;
        if (hold && !holdingPosition)
        {
            holdPosition = body.position;
            horizontalHoldCaptured = altitudeHoldCaptured = false;
        }
        if (hold)
        {
            // First arrest travel, then hold the resulting stop point instead of pulling back to the release point.
            if (!horizontalHoldCaptured)
            {
                holdPosition.x = body.position.x; holdPosition.z = body.position.z;
                horizontalHoldCaptured = Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up).magnitude < .3f;
            }
            if (!altitudeHoldCaptured)
            {
                holdPosition.y = body.position.y;
                altitudeHoldCaptured = Mathf.Abs(body.linearVelocity.y) < .15f;
            }
        }
        holdingPosition = hold;
        float gravity = Physics.gravity.magnitude * flightForceMultiplier;
        Vector3 angularAcceleration;
        float thrust;
        Vector3 rotorUp = body.rotation * Vector3.up;
        if (!assisted)
        {
            Vector3 localRates = new Vector3(Mathf.Clamp(input.move.y + input.pitchRate, -1f, 1f) * manualPitchRate,
                input.yaw * yawRate, -input.move.x * manualRollRate) * Mathf.Deg2Rad;
            RequestedAttitude = body.rotation * Quaternion.Euler(localRates * Mathf.Rad2Deg * .25f);
            if (Grounded) localRates = Vector3.zero;
            angularAcceleration = Vector3.ClampMagnitude((body.rotation * localRates - body.angularVelocity) * manualResponse,
                manualMaxAngularAcceleration);
            thrust = gravity * Mathf.Clamp(1f + input.lift * .65f, 0, maxThrustMultiplier);
        }
        else
        {
            float pitch, roll;
            if (input.directionalFlight)
            {
                float requestedHeading = !input.aim && input.move.sqrMagnitude > .001f ? input.movementHeading : input.heading;
                heading = Mathf.MoveTowardsAngle(heading,requestedHeading,yawRate * dt);
                Quaternion movementFrame = Quaternion.Euler(0,input.movementHeading,0);
                Vector3 velocity = Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up);
                Vector3 desiredVelocity = movementFrame * new Vector3(input.move.x,0,input.move.y) * maxSpeed;
                Vector3 acceleration = holdingPosition
                    ? Vector3.ProjectOnPlane(holdPosition-body.position,Vector3.up) * positionHoldSpring - velocity * positionHoldDamping
                    : (desiredVelocity-velocity) * movementVelocityResponse;
                float tiltAcceleration = gravity * Mathf.Tan(maxTilt * Mathf.Deg2Rad);
                acceleration = Vector3.ClampMagnitude(acceleration,Mathf.Min(maxHoldAcceleration,tiltAcceleration));
                cyclicAcceleration = Vector3.MoveTowards(cyclicAcceleration,acceleration,tiltAcceleration * dt / Mathf.Max(.01f,cyclicResponseTime));
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0,heading,0)) * cyclicAcceleration;
                pitch = Mathf.Atan2(local.z,gravity) * Mathf.Rad2Deg;
                roll = -Mathf.Atan2(local.x,Mathf.Sqrt(gravity*gravity + local.z*local.z)) * Mathf.Rad2Deg;
                if(input.aim)
                {
                    // Movement keeps its cyclic authority; aim alignment resumes as movement input releases.
                    float aimWeight = 1f-Mathf.Clamp01(input.move.magnitude);
                    pitch = Mathf.Lerp(pitch,Mathf.Clamp(input.viewPitch,-maxAimPitch,maxAimPitch),aimWeight);
                    roll *= 1f-aimWeight;
                }
            }
            else
            {
                heading += input.yaw * yawRate * dt;
                pitch = input.move.y * maxTilt;
                roll = -input.move.x * maxTilt;
            }
            RequestedAttitude = Quaternion.Euler(pitch,heading,roll);
            if (Grounded)
            {
                holdingPosition = false;
                heading = body.rotation.eulerAngles.y;
                RequestedAttitude = body.rotation;
            }
            Quaternion error = RequestedAttitude * Quaternion.Inverse(body.rotation);
            error.ToAngleAxis(out float angle,out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            // Rate damping follows bounded cyclic/pedal demands instead of chasing attitude with an unlimited rate.
            Vector3 localError = Quaternion.Inverse(body.rotation) * (axis.normalized * angle * Mathf.Deg2Rad);
            Vector3 desiredRates = localError * (attitudeSpring / attitudeDamping);
            desiredRates.x = Mathf.Clamp(desiredRates.x,-manualPitchRate*Mathf.Deg2Rad,manualPitchRate*Mathf.Deg2Rad);
            desiredRates.y = Mathf.Clamp(desiredRates.y,-yawRate*Mathf.Deg2Rad,yawRate*Mathf.Deg2Rad);
            desiredRates.z = Mathf.Clamp(desiredRates.z,-manualRollRate*Mathf.Deg2Rad,manualRollRate*Mathf.Deg2Rad);
            angularAcceleration = Vector3.ClampMagnitude((body.rotation * desiredRates-body.angularVelocity) * attitudeDamping,maxAngularAcceleration);
            float desiredClimb = holdingPosition
                ? Mathf.Clamp((holdPosition.y-body.position.y) * positionHoldSpring,-climbSpeed,climbSpeed)
                : input.lift * climbSpeed;
            float verticalLift = gravity + (desiredClimb-body.linearVelocity.y) * verticalResponse;
            thrust = Mathf.Clamp(verticalLift / Mathf.Max(.5f,Vector3.Dot(rotorUp,Vector3.up)),0,gravity * maxThrustMultiplier);
        }
        // Both control modes feed the same Rigidbody torque and rotor-axis thrust.
        body.AddTorque(angularAcceleration * RotorPower,ForceMode.Acceleration);
        body.AddForce(rotorUp * thrust * RotorPower,ForceMode.Acceleration);
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
