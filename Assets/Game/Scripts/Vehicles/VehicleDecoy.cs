using System.Collections.Generic;
using UnityEngine;

// Short-lived expendable signatures, independent of the dispensing vehicle's motion.
public sealed class VehicleDecoy : MonoBehaviour
{
    public enum Signature { Infrared, Radar }
    public static readonly List<VehicleDecoy> Active = new List<VehicleDecoy>();
    public Signature signature;
    public Vector3 velocity;
    public float lifetime = 5;
    float expires;
    public bool Alive => isActiveAndEnabled && Time.time < expires;
    void OnEnable(){ expires=Time.time+lifetime;Active.Add(this); }
    void OnDisable()=>Active.Remove(this);
    void FixedUpdate()
    {
        if(!Alive){Destroy(gameObject);return;}
        velocity+=Physics.gravity*Time.fixedDeltaTime;
        velocity*=Mathf.Exp(-.5f*Time.fixedDeltaTime);
        transform.position+=velocity*Time.fixedDeltaTime;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()=>Active.Clear();
}
