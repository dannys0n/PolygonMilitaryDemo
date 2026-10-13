using UnityEngine;

// Articulates a ground launcher's modeled rack without changing the projectile or drive controls.
public class VehicleLauncherMount : MonoBehaviour
{
    public Vehicle vehicle;
    public Transform yawPivot, pitchPivot, muzzle;
    public float turnRate = 65f;
    public Vector2 pitchLimits = new Vector2(0f, 55f);
    public float yawLimit = 75f;
    Vector3 aimPoint;
    bool hasAim;

    public void AimAt(Ray view)
    {
        float distance = 300f;
        foreach (var hit in Physics.RaycastAll(view, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(vehicle.transform) && !(hit.collider is CharacterController))
                distance = Mathf.Min(distance, hit.distance);
        Aim(view.GetPoint(distance));
    }

    public void Aim(Vector3 worldPoint)
    {
        aimPoint = worldPoint;
        hasAim = true;
    }

    void LateUpdate()
    {
        if (!hasAim || vehicle.IsDestroyed) return;
        Vector3 direction = (aimPoint - muzzle.position).normalized;
        Vector3 local = yawPivot.parent.InverseTransformDirection(direction);
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
        yawPivot.localRotation = Quaternion.RotateTowards(yawPivot.localRotation, Quaternion.Euler(0, yaw, 0), turnRate * Time.deltaTime);
        local = yawPivot.InverseTransformDirection(direction);
        float pitch = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, pitchLimits.x, pitchLimits.y);
        pitchPivot.localRotation = Quaternion.RotateTowards(pitchPivot.localRotation, Quaternion.Euler(-pitch, 0, 0), turnRate * Time.deltaTime);
    }
}
