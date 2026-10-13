using UnityEngine;

// Garage-only cargo: authored placement, then ordinary independent Rigidbody simulation.
public sealed class VehicleLooseCargo : MonoBehaviour
{
    public Rigidbody[] cargo=System.Array.Empty<Rigidbody>();
    void Start()
    {
        var vehicle=GetComponent<Vehicle>();
        foreach(var body in cargo)
        {
            if(!body)continue;
            body.transform.SetParent(null,true);
            if(vehicle && vehicle.Body)body.linearVelocity=vehicle.Body.GetPointVelocity(body.position);
        }
    }
    void OnDestroy()
    {
        foreach(var body in cargo)if(body)Destroy(body.gameObject);
    }
}
