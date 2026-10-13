using UnityEngine;

// A single wing/fuselage model: NASA dynamic pressure and lift equation, MSFS drag polar,
// and FAA banked/ coordinated turns. Coefficients and rates are tuned for an arena prototype.
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
    public bool assistEnabled = true;
    public float pitchRate = 65, rollRate = 120, yawRate = 28;
    public float controlResponse = 5, maximumAngularAcceleration = 12, referenceControlSpeed = 65;
    public float aerodynamicStability = 1.4f, sideslipStability = 2;
    public float maximumBank = 60, headingToBank = 1.4f, maxLoadFactor = 7;
    [Min(.1f)] public float assistedClimbResponse = 2;
    [Min(1)] public float assistedBankRate = 140;
    [Header("Ground handling")]
    public float wheelBrakeAcceleration = 8, groundSteeringRate = 28, rotationSpeed = 60;
    public float Throttle { get; private set; }
    public float EnginePower { get; private set; }
    public float Airspeed { get; private set; }
    public float AngleOfAttack { get; private set; }
    public float LoadFactor { get; private set; }
    public bool Stalled { get; private set; }
    public bool Grounded { get; private set; }
    public Quaternion RequestedAttitude { get; private set; }
    public override bool CanDisembark => Grounded;
    readonly RaycastHit[] groundHits = new RaycastHit[16];
    float assistedTurnDirection, assistedBank;
    bool wasAssisted;

    void Start()
    {
        Throttle = startingThrottle;
        RequestedAttitude = vehicle.Body.rotation;
        vehicle.Body.maxAngularVelocity = 4;
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
        bool assisted = assistEnabled || (vehicle.Driver && !vehicle.Driver.IsLocal);
        Vector3 travelDirection = Airspeed > .1f ? body.linearVelocity / Airspeed : frame * Vector3.forward;
        Vector3 liftDirection = Vector3.ProjectOnPlane(frame * Vector3.up, travelDirection).normalized;
        if (powered && assisted && input.directionalFlight && !Grounded)
        {
            // Fly-by-wire load compensation acts through the banked wings, not a sideways steering force.
            // It remains bounded by dynamic pressure, stall loss and the airframe's load limit.
            float desiredClimb = -Mathf.Sin(input.viewPitch * Mathf.Deg2Rad) * forwardSpeed;
            float verticalAcceleration = (desiredClimb - body.linearVelocity.y) * assistedClimbResponse;
            float demand = body.mass * (Physics.gravity.magnitude + verticalAcceleration)
                / Mathf.Max(.2f, Vector3.Dot(liftDirection, Vector3.up));
            float stallLoss = Mathf.Lerp(1, .3f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(stallAngle, stallAngle + 22, Mathf.Abs(wingAngle))));
            float available = pressure * wingArea * maximumLiftCoefficient * stallLoss;
            lift = Mathf.Clamp(demand, -available, available);
            liftCoefficient = lift / Mathf.Max(1, pressure * wingArea);
        }
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
            // The vertical tail resists lateral flow; it does not replace banked wing lift with strafing.
            body.AddForce(-(frame * Vector3.right) * localVelocity.x * pressure * wingArea * .0015f);
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
        Vector3 localRates = Vector3.zero;
        if (powered)
        {
            if (assisted && input.directionalFlight)
            {
                if (!wasAssisted)
                {
                    assistedBank = -Mathf.DeltaAngle(0, frame.eulerAngles.z);
                    assistedTurnDirection = 0;
                }
                localRates = AssistedRates(input, body, pressure, liftDirection * (lift / body.mass));
            }
            else
            {
                localRates = new Vector3(input.pitchRate * pitchRate, input.yaw * yawRate, -input.move.x * rollRate) * Mathf.Deg2Rad;
                RequestedAttitude = frame * Quaternion.Euler(localRates * Mathf.Rad2Deg * .4f);
            }
        }
        wasAssisted = powered && assisted && input.directionalFlight;
        Vector3 localAngular = Quaternion.Inverse(frame) * body.angularVelocity;
        Vector3 acceleration = (localRates - localAngular) * controlResponse;
        // Conventional tail stability points the nose back toward the actual airflow.
        // At low speed aerodynamic surfaces have little authority; no hover thrust is applied.
        acceleration.x += AngleOfAttack * Mathf.Deg2Rad * aerodynamicStability;
        acceleration.y += Mathf.Atan2(localVelocity.x, Mathf.Max(1, forwardSpeed)) * sideslipStability;
        acceleration *= authority;
        if (Grounded)
        {
            float steering = powered && assisted && input.directionalFlight
                ? Mathf.Clamp(Mathf.DeltaAngle(body.rotation.eulerAngles.y, input.heading) / 30, -1, 1) : input.yaw;
            acceleration.y = (steering * groundSteeringRate * Mathf.Deg2Rad - localAngular.y) * 3;
            // Keep wheels level until there is enough airflow for takeoff rotation.
            if (forwardSpeed < rotationSpeed) { acceleration.x = -localAngular.x * 3; acceleration.z = -localAngular.z * 3; }
            if (input.brake || input.move.y < -.5f)
                body.AddForce(-Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).normalized * wheelBrakeAcceleration, ForceMode.Acceleration);
        }
        body.AddTorque(frame * Vector3.ClampMagnitude(acceleration, maximumAngularAcceleration), ForceMode.Acceleration);
    }

    Vector3 AssistedRates(VehicleInput input, Rigidbody body, float pressure, Vector3 liftAcceleration)
    {
        float heading = body.rotation.eulerAngles.y;
        float headingError = Mathf.DeltaAngle(heading, input.heading);
        // Directly behind, tiny input changes cross +/-180. Keep the chosen turn side until
        // the target leaves the rear sector, rather than commanding an opposite bank every frame.
        if (Mathf.Abs(headingError) > 150)
        {
            if (assistedTurnDirection == 0) assistedTurnDirection = Mathf.Sign(headingError);
            headingError = Mathf.Abs(headingError) * assistedTurnDirection;
        }
        else assistedTurnDirection = Mathf.Abs(headingError) > 2 ? Mathf.Sign(headingError) : 0;
        float desiredBank = Grounded ? 0 : Mathf.Clamp(headingError * headingToBank, -maximumBank, maximumBank);
        assistedBank = Mathf.MoveTowards(assistedBank, desiredBank, assistedBankRate * Time.fixedDeltaTime);
        float bank = assistedBank;
        float supportedWeight = body.mass * Physics.gravity.magnitude / Mathf.Max(.2f, Mathf.Cos(bank * Mathf.Deg2Rad));
        float trimCl = Mathf.Clamp(supportedWeight / Mathf.Max(1, pressure * wingArea), 0, maximumLiftCoefficient * .95f);
        float trimAngle = Mathf.Clamp((trimCl - liftBias) / liftSlope * Mathf.Rad2Deg - wingIncidence, -5, 12);
        float pitch = Mathf.Clamp(input.viewPitch - trimAngle, -75, 75);
        if (Grounded && Airspeed < rotationSpeed) pitch = body.rotation.eulerAngles.x;
        RequestedAttitude = Quaternion.Euler(pitch, heading, -bank);
        Quaternion error = Quaternion.Inverse(body.rotation) * RequestedAttitude;
        error.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180) angle -= 360;
        Vector3 rates = axis.normalized * angle * Mathf.Deg2Rad * controlResponse;
        // Coordinate the nose with the path curvature produced by the actual bank and lift.
        // Using the requested bank here would yaw the model before its trajectory could turn.
        Vector3 horizontalTravel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Vector3 turnRight = Vector3.Cross(Vector3.up, horizontalTravel.normalized);
        float coordinatedYaw = Grounded ? 0 : Vector3.Dot(liftAcceleration, turnRight) / Mathf.Max(25, horizontalTravel.magnitude);
        rates.y = 0;
        rates += Quaternion.Inverse(body.rotation) * Vector3.up * coordinatedYaw;
        rates.x = Mathf.Clamp(rates.x, -pitchRate * Mathf.Deg2Rad, pitchRate * Mathf.Deg2Rad);
        rates.y = Mathf.Clamp(rates.y, -yawRate * Mathf.Deg2Rad, yawRate * Mathf.Deg2Rad);
        rates.z = Mathf.Clamp(rates.z, -rollRate * Mathf.Deg2Rad, rollRate * Mathf.Deg2Rad);
        if (wingIncidence + AngleOfAttack > stallAngle - 2) rates.x = Mathf.Max(rates.x, .25f);
        return rates;
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
