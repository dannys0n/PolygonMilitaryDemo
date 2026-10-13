using UnityEngine;

// Game flight model: thrust, speed/AoA-dependent lift, directional drag, and rate-limited
// steering torque. Receives only pilot controls; aim guidance belongs to JetFlightAssist.
public class JetMotor : VehicleMotor
{
    public SphereCollider[] landingGear;
    [Header("Airframe (SI units)")]
    [Min(1)] public float wingArea = 72;
    public float airDensity = 1.225f, wingIncidence = 4, liftBias = .18f, liftSlope = 5.5f;
    public float stallAngle = 18, maximumLiftCoefficient = 1.65f;
    public float parasiteDrag = .035f, inducedDrag = .075f, sideslipDrag = .6f;
    [Header("Engine")]
    public float maximumThrust = 180000, afterburnerMultiplier = 1.6f, engineResponse = 1.5f;
    public float throttleChangeRate = .55f, startingThrottle = .65f, maxSpeed = 180;
    [Header("Flight controls")]

    public float pitchRate = 65, rollRate = 120, yawRate = 28;
    public float controlResponse = 5, maximumAngularAcceleration = 12, referenceControlSpeed = 65;
    [Min(0)] public float lateralFlowResponse = 5;
    public float maxLoadFactor = 7;

    public Vector3 FlightControlInput { get; private set; }
    [System.Serializable]
    public class ControlSurface
    {
        public Transform part;
        public Vector3 hingeAxis = Vector3.right;
        public float pitch, roll, yaw;
        [System.NonSerialized] public Quaternion restRotation;
        [System.NonSerialized] public float angle;
    }
    [Header("Animated flight controls")]
    public ControlSurface[] controlSurfaces = System.Array.Empty<ControlSurface>();
    public float aileronTravel = 22, elevatorTravel = 22, rudderTravel = 20, surfaceResponse = 120;
    public Vector3 ControlDeflection { get; private set; }
    [Header("Ground handling")]
    public float wheelBrakeAcceleration = 8, groundSteeringRate = 28, rotationSpeed = 60;
    public float Throttle { get; private set; }
    public float EnginePower { get; private set; }
    public float Airspeed { get; private set; }
    public float AngleOfAttack { get; private set; }
    public float LoadFactor { get; private set; }
    public bool Stalled { get; private set; }
    public bool Grounded { get; private set; }

    public override bool CanDisembark => Grounded;
    readonly RaycastHit[] groundHits = new RaycastHit[16];

    void Start()
    {
        Throttle = startingThrottle;

        vehicle.Body.maxAngularVelocity = 4;
        foreach (var surface in controlSurfaces)
            if (surface.part) surface.restRotation = surface.part.localRotation;
    }

    void LateUpdate()
    {
        // Render the actuator state simulated in FixedUpdate. No separate visual steering delay.
        foreach (var surface in controlSurfaces)
            if (surface.part) surface.part.localRotation = surface.restRotation * Quaternion.AngleAxis(surface.angle, surface.hingeAxis);
    }

    void SimulateControlSurfaces(Vector3 requestedDeflection, float dt)
    {
        // Animate the pilot's sustained pitch/roll/rudder command, not the rate controller's
        // diminishing correction torque after the requested turning rate is reached.
        Vector3 current = ControlDeflection;
        current.x = Mathf.MoveTowards(current.x, requestedDeflection.x, surfaceResponse * dt / Mathf.Max(1, elevatorTravel));
        current.y = Mathf.MoveTowards(current.y, requestedDeflection.y, surfaceResponse * dt / Mathf.Max(1, rudderTravel));
        current.z = Mathf.MoveTowards(current.z, requestedDeflection.z, surfaceResponse * dt / Mathf.Max(1, aileronTravel));
        ControlDeflection = Vector3.ClampMagnitude(current, 1);
        float roll = -ControlDeflection.z * aileronTravel;
        float pitch = -ControlDeflection.x * elevatorTravel;
        float yaw = ControlDeflection.y * rudderTravel;
        foreach (var surface in controlSurfaces)
        {
            if (!surface.part) continue;
            surface.angle = Mathf.Clamp(surface.pitch * pitch + surface.roll * roll + surface.yaw * yaw, -30, 30);
        }
    }

    public override void Simulate(VehicleInput input, bool powered)
    {
        var body = vehicle.Body;
        float dt = Time.fixedDeltaTime;
        Grounded = IsOnGround();
        Airspeed = body.linearVelocity.magnitude;
        Quaternion frame = body.rotation;
        Vector3 localVelocity = Quaternion.Inverse(frame) * body.linearVelocity;
        float forwardSpeed = Mathf.Max(0, localVelocity.z);
        // Positive AoA means the nose is above the air-relative flight path.
        AngleOfAttack = Mathf.Atan2(-localVelocity.y, Mathf.Max(.1f, forwardSpeed)) * Mathf.Rad2Deg;
        float wingAngle = AngleOfAttack + wingIncidence;
        float pressure = .5f * airDensity * Airspeed * Airspeed;
        float liftCoefficient = LiftCoefficient(wingAngle);
        float lift = pressure * wingArea * liftCoefficient;

        Vector3 travelDirection = Airspeed > .1f ? body.linearVelocity / Airspeed : frame * Vector3.forward;
        Vector3 liftDirection = Vector3.ProjectOnPlane(frame * Vector3.up, travelDirection).normalized;
        lift = Mathf.Clamp(lift, -body.mass * Physics.gravity.magnitude * maxLoadFactor,
            body.mass * Physics.gravity.magnitude * maxLoadFactor);
        LoadFactor = lift / (body.mass * Mathf.Max(.01f, Physics.gravity.magnitude));
        Stalled = !Grounded && forwardSpeed > 5 && Mathf.Abs(wingAngle) > stallAngle;
        if (Airspeed > .1f)
        {
            Vector3 travel = body.linearVelocity / Airspeed;
            float slip = Mathf.Atan2(localVelocity.x, Mathf.Max(1, forwardSpeed));
            float cd = parasiteDrag + inducedDrag * liftCoefficient * liftCoefficient + sideslipDrag * slip * slip;
            cd += Mathf.Abs(wingAngle) > stallAngle ? .3f * Mathf.InverseLerp(stallAngle, 65, Mathf.Abs(wingAngle)) : 0;
            if (localVelocity.z < 0) cd += .8f;
            body.AddForce(liftDirection * lift - travel * (pressure * wingArea * cd));
            // Directional drag resists sideways flow and brings travel into alignment with the nose.
            // Apply a bounded force; never assign velocity or rotate the Rigidbody directly.
            float flowAuthority = Mathf.Clamp01(Airspeed / Mathf.Max(1, referenceControlSpeed));
            body.AddForce(-(frame * Vector3.right) * localVelocity.x * lateralFlowResponse * flowAuthority, ForceMode.Acceleration);
            if (Airspeed > maxSpeed)
                body.AddForce(-travel * (Airspeed - maxSpeed) * 1.5f, ForceMode.Acceleration);
        }

        if (powered)
            Throttle = Mathf.Clamp01(Throttle + input.move.y * throttleChangeRate * dt);
        float engineTarget = powered ? Throttle * (input.boost ? afterburnerMultiplier : 1) : 0;
        EnginePower = Mathf.MoveTowards(EnginePower, engineTarget, engineResponse * dt);
        // Fade thrust near the speed limit instead of fighting a large overspeed braking force.
        float speedHeadroom = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(maxSpeed * .85f, maxSpeed, forwardSpeed));
        body.AddForce(frame * Vector3.forward * maximumThrust * EnginePower * speedHeadroom);

        float authority = Mathf.Clamp01(forwardSpeed * forwardSpeed / (referenceControlSpeed * referenceControlSpeed));
        FlightControlInput = powered ? new Vector3(Mathf.Clamp(input.pitchRate, -1, 1),
            Mathf.Clamp(input.yaw, -1, 1), -Mathf.Clamp(input.move.x, -1, 1)) : Vector3.zero;
        SimulateControlSurfaces(FlightControlInput, dt);
        Vector3 localRates = Vector3.Scale(ControlDeflection, new Vector3(pitchRate, yawRate, rollRate)) * Mathf.Deg2Rad;
        Vector3 localAngular = Quaternion.Inverse(frame) * body.angularVelocity;
        float controlLimit = Mathf.Max(.01f, maximumAngularAcceleration);
        Vector3 requestedControl = Vector3.ClampMagnitude((localRates - localAngular) * controlResponse, controlLimit);
        // Airflow determines how much authority a deflected surface has. Visible travel can
        // still occur on the runway, but cannot roll or pitch a stationary aircraft into flight.
        Vector3 acceleration = powered ? requestedControl : Vector3.zero;
        acceleration *= authority;
        if (Grounded)
        {
            float steering = FlightControlInput.y;
            acceleration.y = (steering * groundSteeringRate * Mathf.Deg2Rad - localAngular.y) * 3;
            // Keep wheels level until there is enough airflow for takeoff rotation.
            if (forwardSpeed < rotationSpeed) { acceleration.x = -localAngular.x * 3; acceleration.z = -localAngular.z * 3; }
            if (input.brake || input.move.y < -.5f)
                body.AddForce(-Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).normalized * wheelBrakeAcceleration, ForceMode.Acceleration);
        }
        Vector3 appliedControl = Vector3.ClampMagnitude(acceleration, maximumAngularAcceleration);
        body.AddTorque(frame * appliedControl, ForceMode.Acceleration);
    }

    float LiftCoefficient(float degrees)
    {
        float attached = Mathf.Clamp(liftBias + liftSlope * degrees * Mathf.Deg2Rad, -maximumLiftCoefficient, maximumLiftCoefficient);
        float stalled = Mathf.Sin(2 * degrees * Mathf.Deg2Rad) * .8f;
        return Mathf.Lerp(attached, stalled, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(stallAngle, stallAngle + 22, Mathf.Abs(degrees))));
    }

    bool IsOnGround()
    {
        foreach (var gear in landingGear)
        {
            int count = Physics.RaycastNonAlloc(gear.transform.TransformPoint(gear.center), Vector3.down, groundHits,
                gear.radius * Mathf.Max(gear.transform.lossyScale.x, gear.transform.lossyScale.y, gear.transform.lossyScale.z) + .15f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!groundHits[i].transform.IsChildOf(transform) && !groundHits[i].collider.GetComponentInParent<SoldierLife>()
                    && groundHits[i].normal.y > .35f) return true;
        }
        return false;
    }
}
