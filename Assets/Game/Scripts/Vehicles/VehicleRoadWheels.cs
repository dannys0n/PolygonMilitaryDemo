using UnityEngine;

// Visual-only motion for road wheels inside a static track. The tracks themselves stay authored.
public class VehicleRoadWheels : MonoBehaviour
{
    public Vehicle vehicle;
    public Transform[] left, right;
    public float radius = .38f;

    void LateUpdate()
    {
        if (vehicle.IsDestroyed) return;
        Vector3 localVelocity = vehicle.transform.InverseTransformDirection(vehicle.Body.linearVelocity);
        float yawRate = Vector3.Dot(vehicle.Body.angularVelocity, vehicle.transform.up);
        float leftDistance = (localVelocity.z + yawRate * .8f) * Time.deltaTime;
        float rightDistance = (localVelocity.z - yawRate * .8f) * Time.deltaTime;
        Spin(left, leftDistance);
        Spin(right, rightDistance);
    }

    void Spin(Transform[] wheels, float distance)
    {
        float degrees = distance / Mathf.Max(.05f, radius) * Mathf.Rad2Deg;
        foreach (var wheel in wheels) wheel.Rotate(vehicle.transform.right, degrees, Space.World);
    }
}
