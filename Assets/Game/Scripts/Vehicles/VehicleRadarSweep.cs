using UnityEngine;

public class VehicleRadarSweep : MonoBehaviour
{
    public Vehicle vehicle;
    public Transform radar;
    public float degreesPerSecond = 30f;
    void LateUpdate()
    {
        if (!vehicle.IsDestroyed) radar.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
