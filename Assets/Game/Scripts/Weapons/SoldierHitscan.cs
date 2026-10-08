using UnityEngine;

public class SoldierHitscan : MonoBehaviour
{
    public Camera aimCamera;
    public LayerMask hitLayers = Physics.DefaultRaycastLayers;
    [Min(.001f)] public float muzzleClearance = .01f;
    public bool drawDebug;
    public event System.Action<ShotResult> ShotResolved;

    public struct ShotResult
    {
        public Vector3 origin;
        public Vector3 point;
        public Collider collider;
        public bool damaged;
        public bool killed;
    }

    public ShotResult Fire(SoldierWeaponDefinition weapon, Transform muzzle)
    {
        Ray cameraRay = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        Vector3 point = cameraRay.GetPoint(weapon.range);
        if (ClosestHit(cameraRay, weapon.range, out RaycastHit aimHit)) point = aimHit.point;

        Vector3 origin = muzzle.position;
        var result = new ShotResult { origin = origin, point = point };
        // A muzzle inside cover must not shoot out through its back face.
        foreach (Collider collider in Physics.OverlapSphere(origin, muzzleClearance,
            hitLayers, QueryTriggerInteraction.Collide))
        {
            if (!IsShotCollider(collider)) continue;
            result.collider = collider;
            result.point = collider.ClosestPoint(origin);
            return Finish(result, weapon);
        }

        // Also catch cover between the player and a protruding muzzle.
        Vector3 firingOrigin = transform.position + Vector3.up *
            GetComponent<CharacterController>().height * .75f;
        Vector3 toMuzzle = origin - firingOrigin;
        if (ClosestHit(new Ray(firingOrigin, toMuzzle), toMuzzle.magnitude, out RaycastHit coverHit))
        {
            result.collider = coverHit.collider;
            result.point = coverHit.point;
            return Finish(result, weapon);
        }

        Vector3 direction = point - origin;
        float distance = Mathf.Min(weapon.range, direction.magnitude + .01f);
        result.point = origin + direction.normalized * distance;
        if (ClosestHit(new Ray(origin, direction), distance, out RaycastHit hit))
        {
            result.collider = hit.collider;
            result.point = hit.point;
        }
        return Finish(result, weapon);
    }

    ShotResult Finish(ShotResult result, SoldierWeaponDefinition weapon)
    {
        // Missing hit regions represent ordinary world surfaces, not damageable actors.
        if (result.collider && result.collider.TryGetComponent(out SoldierHitRegion region))
        {
            result.damaged = region.ApplyHit(weapon, gameObject);
            result.killed = result.damaged && region.health.IsDead;
        }
        if (drawDebug) Debug.DrawLine(result.origin, result.point, Color.yellow, 1f);
        ShotResolved?.Invoke(result);
        return result;
    }

    bool IsOwnCollider(Collider collider) => collider.transform.IsChildOf(transform);

    bool IsShotCollider(Collider collider) => !IsOwnCollider(collider)
        && !(collider is CharacterController)
        && (!collider.isTrigger || collider.TryGetComponent<SoldierHitRegion>(out _));

    bool ClosestHit(Ray ray, float distance, out RaycastHit closest)
    {
        closest = default;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, distance, hitLayers, QueryTriggerInteraction.Collide))
        {
            if (!IsShotCollider(hit.collider) || (found && hit.distance >= closest.distance)) continue;
            closest = hit;
            found = true;
        }
        return found;
    }
}
