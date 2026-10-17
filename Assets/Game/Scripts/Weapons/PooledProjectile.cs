using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class PooledProjectile : MonoBehaviour
{
    GameObject prefab;
    Rigidbody body;
    Collider[] colliders;
    bool[] colliderEnabled;
    Renderer[] renderers;
    bool[] rendererEnabled;
    ParticleSystem[] particles;
    TrailRenderer[] trails;
    readonly List<(Collider first, Collider second)> ignored = new();
    bool returning;
    bool guidanceEnabled;

    public void Initialize(GameObject source)
    {
        prefab = source;
        body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        colliderEnabled = System.Array.ConvertAll(colliders, c => c.enabled);
        renderers = GetComponentsInChildren<Renderer>(true);
        rendererEnabled = System.Array.ConvertAll(renderers, r => r.enabled);
        particles = GetComponentsInChildren<ParticleSystem>(true);
        trails = GetComponentsInChildren<TrailRenderer>(true);
        if (TryGetComponent<VehicleMissileGuidance>(out var guidance)) guidanceEnabled = guidance.enabled;
    }

    public void TrackIgnore(Collider first, Collider second) => ignored.Add((first, second));

    public void Prepare(Vector3 position, Quaternion rotation)
    {
        returning = false;
        transform.SetPositionAndRotation(position, rotation);
        if (body)
        {
            body.isKinematic = false;
            body.position = position; body.rotation = rotation;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        }
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = colliderEnabled[i];
        for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = rendererEnabled[i];
        foreach (var particle in particles) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var trail in trails) trail.Clear();
        if (TryGetComponent<VehicleRocket>(out var rocket)) rocket.ResetForReuse();
        gameObject.SetActive(true);
        if (TryGetComponent<VehicleMissileGuidance>(out var guidance)) guidance.enabled = guidanceEnabled;
    }

    public void Release(float tailSeconds)
    {
        if (returning) return;
        returning = true;
        foreach (var pair in ignored)
            if (pair.first && pair.second) Physics.IgnoreCollision(pair.first, pair.second, false);
        ignored.Clear();
        if (TryGetComponent<VehicleMissileGuidance>(out var guidance)) guidance.enabled = false;
        foreach (var collider in colliders) collider.enabled = false;
        foreach (var renderer in renderers)
            if (!(renderer is ParticleSystemRenderer) && !(renderer is TrailRenderer)) renderer.enabled = false;
        if (body) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
        if (tailSeconds > 0) StartCoroutine(ReturnAfterTail(tailSeconds));
        else ProjectilePool.Store(this, prefab);
    }

    IEnumerator ReturnAfterTail(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        ProjectilePool.Store(this, prefab);
    }
}
