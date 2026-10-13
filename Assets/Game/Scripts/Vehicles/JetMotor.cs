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
        lift = Mathf.Clamp(lift, -body.mass * Physics.gravity.magnitude * maxLoadFactor,
            body.mass * Physics.gravity.magnitude * maxLoadFactor);
        LoadFactor = lift / (body.mass * Mathf.Max(.01f, Physics.gravity.magnitude));
        Stalled = !Grounded && forwardSpeed > 5 && Mathf.Abs(wingAngle) > stallAngle;
        if (Airspeed > .1f)
        {
            Vector3 travel = body.linearVelocity / Airspeed;
            Vector3 liftDirection = Vector3.ProjectOnPlane(frame * Vector3.up, travel).normalized;
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
        body.AddForce(frame * Vector3.forward * maximumThrust * EnginePower);

        float authority = Mathf.Clamp01(forwardSpeed * forwardSpeed / (referenceControlSpeed * referenceControlSpeed));
        Vector3 localRates = Vector3.zero;
        bool assisted = assistEnabled || (vehicle.Driver && !vehicle.Driver.IsLocal);
        if (powered)
        {
            if (assisted && input.directionalFlight)
                localRates = AssistedRates(input, body, pressure);
            else
            {
                localRates = new Vector3(input.pitchRate * pitchRate, input.yaw * yawRate, -input.move.x * rollRate) * Mathf.Deg2Rad;
                RequestedAttitude = frame * Quaternion.Euler(localRates * Mathf.Rad2Deg * .4f);
            }
        }
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

    Vector3 AssistedRates(VehicleInput input, Rigidbody body, float pressure)
    {
        float heading = body.rotation.eulerAngles.y;
        float headingError = Mathf.DeltaAngle(heading, input.heading);
        float bank = Mathf.Clamp(headingError * headingToBank, -maximumBank, maximumBank);
        if (Grounded) bank = 0;
        float supportedWeight = body.mass * Physics.gravity.magnitude / Mathf.Max(.5f, Mathf.Cos(bank * Mathf.Deg2Rad));
        float trimCl = Mathf.Clamp(supportedWeight / Mathf.Max(1, pressure * wingArea), 0, maximumLiftCoefficient * .8f);
        float trimAngle = Mathf.Clamp((trimCl - liftBias) / liftSlope * Mathf.Rad2Deg - wingIncidence, -5, 12);
        float pitch = Mathf.Clamp(input.viewPitch - trimAngle, -75, 75);
        if (Grounded && Airspeed < rotationSpeed) pitch = body.rotation.eulerAngles.x;
        RequestedAttitude = Quaternion.Euler(pitch, heading, -bank);
        Quaternion error = Quaternion.Inverse(body.rotation) * RequestedAttitude;
        error.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180) angle -= 360;
        Vector3 rates = axis.normalized * angle * Mathf.Deg2Rad * controlResponse;
        // Yaw follows the turn produced by banked lift, with limited rudder assistance.
        float coordinatedYaw = Physics.gravity.magnitude * Mathf.Tan(bank * Mathf.Deg2Rad) / Mathf.Max(25, Airspeed);
        rates.y = 0;
        rates += Quaternion.Inverse(body.rotation) * Vector3.up * coordinatedYaw;
        rates.y += Mathf.Clamp(headingError * .12f, -yawRate * .4f, yawRate * .4f) * Mathf.Deg2Rad;
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
