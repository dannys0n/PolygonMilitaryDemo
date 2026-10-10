using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Vehicle))]
public class VehicleDestructionEffects : MonoBehaviour
{
    public ParticleSystem blackSmoke, secondaryExplosion;
    public Vector3 localOrigin = new Vector3(0,1,0);
    public float firstExplosionTime=.25f, secondExplosionTime=1f, finalExplosionTime=2.25f;
    public float secondaryScale=.45f, finalScale=1.5f, smokeScale=1f;
    Vehicle vehicle;
    ParticleSystem smoke;
    void Awake() => vehicle=GetComponent<Vehicle>();
    void OnEnable() => vehicle.Destroyed+=OnDestroyed;
    void OnDisable()
    {
        vehicle.Destroyed-=OnDestroyed;
        StopAllCoroutines();
        if(smoke)
        {
            smoke.Stop(true,ParticleSystemStopBehavior.StopEmitting);
            Destroy(smoke.gameObject,smoke.main.startLifetime.constantMax+1f);
            smoke=null;
        }
    }
    Vector3 Origin => transform.TransformPoint(localOrigin);
    void LateUpdate()
    {
        if(smoke)smoke.transform.SetPositionAndRotation(Origin,Quaternion.FromToRotation(Vector3.forward,Vector3.up));
    }
    void OnDestroyed(Vehicle wreck) => StartCoroutine(Sequence());
    IEnumerator Sequence()
    {
        smoke=Instantiate(blackSmoke,Origin,Quaternion.FromToRotation(Vector3.forward,Vector3.up));
        smoke.transform.localScale=Vector3.one*smokeScale;smoke.Play(true);
        yield return new WaitForSeconds(Mathf.Max(0,firstExplosionTime));
        Explosion(secondaryExplosion,secondaryScale);
        yield return new WaitForSeconds(Mathf.Max(0,secondExplosionTime-firstExplosionTime));
        Explosion(secondaryExplosion,secondaryScale);
        yield return new WaitForSeconds(Mathf.Max(0,finalExplosionTime-secondExplosionTime));
        Explosion(vehicle.destructionEffect,finalScale);
    }
    void Explosion(ParticleSystem prefab,float scale)
    {
        var effect=Instantiate(prefab,Origin,Quaternion.FromToRotation(Vector3.forward,Vector3.up));
        effect.transform.localScale=Vector3.one*scale;effect.Play(true);
        float duration=0;
        foreach(var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main=particle.main;
            duration=Mathf.Max(duration,main.startDelay.constantMax+main.duration+main.startLifetime.constantMax);
        }
        Destroy(effect.gameObject,duration+1f);
    }
}