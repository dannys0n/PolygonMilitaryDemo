using UnityEngine;

// Cosmetic casing. Socket +Z points out of the gun; the casing also inherits owner velocity.
[RequireComponent(typeof(Rigidbody))]
public class SoldierShellCasing : MonoBehaviour
{
    public Vector3 minimumVelocity = new Vector3(-.2f, -.15f, 2.5f);
    public Vector3 maximumVelocity = new Vector3(.2f, .25f, 3.5f);
    [Tooltip("Gap between the entire casing and the ejection port, in metres.")]
    public float spawnClearance = .003f;
    public float spinSpeed = 2500f;
    public float lifetime = 1f;
    Rigidbody body;

    float casingRadius;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        Bounds meshBounds = GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 scale = transform.lossyScale;
        // A sphere around the pivot covers every spin orientation, including an offset mesh pivot.
        casingRadius = Vector3.Scale(meshBounds.center, scale).magnitude
            + Vector3.Scale(meshBounds.extents, scale).magnitude;
    }

    public void Launch(Quaternion ejectionRotation, Vector3 ownerVelocity, float visualScale = 1f)
    {
        transform.localScale *= visualScale;
        casingRadius *= Mathf.Abs(visualScale);
        Vector3 velocity = new Vector3(Random.Range(minimumVelocity.x, maximumVelocity.x),
            Random.Range(minimumVelocity.y, maximumVelocity.y), Random.Range(minimumVelocity.z, maximumVelocity.z));
        // Avoid rendering a newborn shell behind the moving receiver.
        body.position += ejectionRotation * Vector3.forward * (casingRadius + spawnClearance);
        transform.position = body.position;
        body.interpolation = RigidbodyInterpolation.Extrapolate;
        body.linearVelocity = ownerVelocity + ejectionRotation * velocity;
        body.maxAngularVelocity = spinSpeed * Mathf.Deg2Rad;
        body.angularDamping = .1f;
        body.angularVelocity = Random.onUnitSphere * body.maxAngularVelocity;
        body.rotation = Random.rotation;
        Destroy(gameObject, lifetime);
    }
}
