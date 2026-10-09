using UnityEngine;

// Cosmetic ballistic casing, following the demo's ejection, spin and short lifetime.
[RequireComponent(typeof(Rigidbody))]
public class SoldierShellCasing : MonoBehaviour
{
    public Vector3 minimumVelocity = new Vector3(2f, .8f, -.96f);
    public Vector3 maximumVelocity = new Vector3(3.2f, 1.6f, .96f);
    public float spinSpeed = 2500f;
    public float lifetime = 1f;

    void Start()
    {
        var body = GetComponent<Rigidbody>();
        Vector3 velocity = new Vector3(Random.Range(minimumVelocity.x, maximumVelocity.x),
            Random.Range(minimumVelocity.y, maximumVelocity.y), Random.Range(minimumVelocity.z, maximumVelocity.z));
        body.linearVelocity = transform.TransformDirection(velocity);
        body.maxAngularVelocity = spinSpeed * Mathf.Deg2Rad;
        body.angularDamping = .1f;
        body.angularVelocity = Random.onUnitSphere * body.maxAngularVelocity;
        body.rotation = Random.rotation;
        Destroy(gameObject, lifetime);
    }

}
