using System.Collections;
using UnityEngine;

// Cosmetic ballistic casing, following the demo's ejection, spin, sound and short lifetime.
[RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
public class SoldierShellCasing : MonoBehaviour
{
    public Vector3 minimumVelocity = new Vector3(2f, .8f, -.96f);
    public Vector3 maximumVelocity = new Vector3(3.2f, 1.6f, .96f);
    public float spinSpeed = 2500f;
    public float lifetime = 1f;
    public AudioClip[] casingSounds;

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
        StartCoroutine(PlayCasingSound());
    }

    IEnumerator PlayCasingSound()
    {
        // The source uses a delayed landing sound rather than collision detection.
        yield return new WaitForSeconds(Random.Range(.25f, .85f));
        GetComponent<AudioSource>().PlayOneShot(casingSounds[Random.Range(0, casingSounds.Length)]);
    }
}
