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
    [Min(0)] public float airDrag=.5f;
    [Min(0)] public float trailFadeTime=2;
    float expires;
    bool burnedOut;
    ParticleSystem[] particles;
    void Awake()=>particles=GetComponentsInChildren<ParticleSystem>();
    public bool Alive => isActiveAndEnabled && Time.time < expires;
    void OnEnable(){ expires=Time.time+lifetime;burnedOut=false;Active.Add(this); }
    void OnDisable()=>Active.Remove(this);
    void FixedUpdate()
    {
        if(!Alive && !burnedOut)
        {
            burnedOut=true;
            // The infrared signature ends with the burn; smoke already emitted fades naturally.
            foreach(var particle in particles)
                particle.Stop(false,ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject,trailFadeTime);
        }
        velocity+=Physics.gravity*Time.fixedDeltaTime;
        velocity*=Mathf.Exp(-airDrag*Time.fixedDeltaTime);
        transform.position+=velocity*Time.fixedDeltaTime;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()=>Active.Clear();
}
