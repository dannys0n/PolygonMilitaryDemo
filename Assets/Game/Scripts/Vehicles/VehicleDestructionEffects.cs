using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Vehicle))]
public class VehicleDestructionEffects : MonoBehaviour
{
    public ParticleSystem blackSmoke, secondaryExplosion;
    [Header("Destroyed model")]
    public GameObject[] intactVisuals;
    public GameObject destroyedPrefab;
    public Vector3 destroyedLocalPosition, destroyedLocalRotation;
    public Vector3 destroyedLocalScale = Vector3.one;
    public GameObject WreckVisual { get; private set; }
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
            ParticleEffectPool.Stop(smoke);
            smoke=null;
        }
    }
    Vector3 Origin => transform.TransformPoint(localOrigin);
    void LateUpdate()
    {
        if(smoke)smoke.transform.SetPositionAndRotation(Origin,Quaternion.FromToRotation(Vector3.forward,Vector3.up));
    }
    void OnDestroyed(Vehicle wreck)
    {
        if (destroyedPrefab)
        {
            foreach (var visual in intactVisuals) if (visual) visual.SetActive(false);
            WreckVisual = Instantiate(destroyedPrefab, transform);
            WreckVisual.transform.SetLocalPositionAndRotation(destroyedLocalPosition, Quaternion.Euler(destroyedLocalRotation));
            WreckVisual.transform.localScale = destroyedLocalScale;
            // Existing chassis/landing colliders remain authoritative; a visual wreck cannot create a second body.
            foreach (var body in WreckVisual.GetComponentsInChildren<Rigidbody>()) Destroy(body);
            foreach (var collider in WreckVisual.GetComponentsInChildren<Collider>()) Destroy(collider);
        }
        StartCoroutine(Sequence());
    }
    IEnumerator Sequence()
    {
        smoke=ParticleEffectPool.Play(blackSmoke,Origin,Quaternion.FromToRotation(Vector3.forward,Vector3.up),Vector3.one*smokeScale,persistent:true);
        yield return new WaitForSeconds(Mathf.Max(0,firstExplosionTime));
        Explosion(secondaryExplosion,secondaryScale);
        yield return new WaitForSeconds(Mathf.Max(0,secondExplosionTime-firstExplosionTime));
        Explosion(secondaryExplosion,secondaryScale);
        yield return new WaitForSeconds(Mathf.Max(0,finalExplosionTime-secondExplosionTime));
        Explosion(vehicle.destructionEffect,finalScale);
    }
    void Explosion(ParticleSystem prefab,float scale)
    {
        ParticleEffectPool.Play(prefab,Origin,Quaternion.FromToRotation(Vector3.forward,Vector3.up),Vector3.one*scale);
    }
}
