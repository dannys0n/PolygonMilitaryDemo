using System.Collections.Generic;
using UnityEngine;

public class SoldierHitscan : MonoBehaviour
{
    public Camera aimCamera;
    public LayerMask hitLayers = Physics.DefaultRaycastLayers;
    [Min(.001f)] public float muzzleClearance = .01f;
    public bool drawDebug;
    public Ray AimRay => aimCamera.ViewportPointToRay(new Vector3(.5f,.5f,0));
    public Vector3 AimUp => aimCamera.transform.up;
    int visualShot;
    bool showShotVisual;
    RaycastHit[] queryHits = new RaycastHit[32];
    Collider[] overlapHits = new Collider[32];
    public event System.Action<ShotResult> ShotResolved;
    // Optional local world interactions may consume a shot before damage/projectiles.
    public event System.Func<Ray, bool> ShotInteraction;

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
        return Fire(weapon,muzzle,AimRay,
            AimUp,GetComponent<SoldierMovement>().Velocity);
    }

    // The shot resolver accepts an authoritative world-space aim without needing a camera.
    public ShotResult Fire(SoldierWeaponDefinition weapon, Transform muzzle, Ray aim, Vector3 up, Vector3 inheritedVelocity, float hipSpreadDegrees = 0f)
    {
        if (ShotInteraction != null)
            foreach (System.Func<Ray, bool> handler in ShotInteraction.GetInvocationList())
                if (handler(aim))
                {
                    var interaction = new ShotResult { origin = muzzle.position, point = aim.GetPoint(weapon.range) };
                    ShotResolved?.Invoke(interaction);
                    return interaction;
                }
        if (!weapon.UsesSpread)
            aim.direction = SpreadDirection(aim.direction, up, weapon.spreadDegrees + hipSpreadDegrees);
        var orientation = Quaternion.LookRotation(aim.direction, up);
        if(weapon.rocketProjectile)
        {
            Vector3 target=ClosestHit(aim,weapon.range,out var hit)?hit.point:aim.GetPoint(weapon.range);
            var rocket=ProjectilePool.Spawn(weapon.rocketProjectile,muzzle.position,Quaternion.LookRotation((target-muzzle.position).normalized));
            rocket.Launch(gameObject,transform,inheritedVelocity);
            var shot=new ShotResult{origin=muzzle.position,point=target};ShotResolved?.Invoke(shot);return shot;
        }
        if(weapon.thrownExplosive)
        {
            var definition=weapon.thrownExplosive;
            var grenade=ProjectilePool.Spawn(definition.projectile,muzzle.position,orientation);
            grenade.Launch(definition,gameObject,aim.direction*definition.throwSpeed+Vector3.up*definition.upwardSpeed);
            var shot=new ShotResult{origin=muzzle.position,point=aim.GetPoint(weapon.range)};ShotResolved?.Invoke(shot);return shot;
        }
        showShotVisual=BulletTracer.ShowVisual(++visualShot,weapon.roundsPerMinute);
        return weapon.UsesSpread ? FireSpread(weapon, muzzle, aim, Mathf.Max(weapon.spreadDegrees, hipSpreadDegrees)) : FireBullet(weapon, muzzle, aim);
    }
    ShotResult FireBullet(SoldierWeaponDefinition weapon, Transform muzzle, Ray cameraRay)
    {
        Vector3 point = cameraRay.GetPoint(weapon.range);
        if (ClosestHit(cameraRay, weapon.range, out RaycastHit aimHit)) point = aimHit.point;

        Vector3 origin = muzzle.position;
        var result = new ShotResult { origin = origin, point = point };
        if (TryMuzzleBlock(origin, out var cover, out var coverPoint))
        {
            result.collider = cover; result.point = coverPoint;
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

    // Uniform sampling over the aiming disk; angle is its cone half-angle.
    public static Vector3 SpreadDirection(Vector3 direction, Vector3 up, float degrees)
    {
        if (degrees <= 0f) return direction.normalized;
        var basis = Quaternion.LookRotation(direction, up);
        Vector2 disk = Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
        return (direction.normalized + basis * new Vector3(disk.x, disk.y, 0f)).normalized;
    }

    struct SpreadHit
    {
        public SoldierHealth health;
        public Vehicle vehicle;
        public Collider collider;
        public Vector3 point;
        public float damage;
    }
    readonly Dictionary<GameObject, SpreadHit> spreadHits = new Dictionary<GameObject, SpreadHit>();

    ShotResult FireSpread(SoldierWeaponDefinition weapon, Transform muzzle, Ray aim, float coneDegrees)
    {
        var result = new ShotResult { origin = muzzle.position, point = aim.GetPoint(weapon.range) };
        if (TryMuzzleBlock(result.origin, out var cover, out var coverPoint))
        {
            result.collider = cover; result.point = coverPoint;
            return Publish(result);
        }
        if (ClosestHit(aim, weapon.range, out var centerHit))
        { result.collider = centerHit.collider; result.point = centerHit.point; }

        spreadHits.Clear();
        float slope = Mathf.Tan(coneDegrees * Mathf.Deg2Rad);
        float halfRange = weapon.range * .5f;
        // One broadphase query encloses the cone; the actual hit test is its radius at target depth.
        float radius = Mathf.Sqrt(halfRange * halfRange + weapon.range * weapon.range * slope * slope);
        int count=CombatQueries.OverlapSphere(aim.GetPoint(halfRange),radius,
            ProjectileCollision.WithoutProjectiles(hitLayers),QueryTriggerInteraction.Collide,ref overlapHits);
        for(int i=0;i<count;i++)
        {
            var collider=overlapHits[i];
            if (!IsShotCollider(collider)) continue;
            var region = collider.GetComponent<SoldierHitRegion>();
            var vehicle = region ? null : collider.GetComponentInParent<Vehicle>();
            if (!region && !vehicle) continue;
            var owner = region ? region.health.gameObject : vehicle.gameObject;
            float centerDepth = Vector3.Dot(collider.bounds.center - aim.origin, aim.direction);
            Vector3 point = collider.ClosestPoint(aim.GetPoint(centerDepth));
            Vector3 delta = point - aim.origin;
            float depth = Vector3.Dot(delta, aim.direction);
            if (depth <= 0f || depth > weapon.range) continue;
            float spreadRadius = Mathf.Max(.001f, depth * slope);
            float radialDistance = (delta - aim.direction * depth).magnitude;
            if (radialDistance > spreadRadius) continue;
            if (!VisibleTo(point, aim.origin, owner) || !VisibleTo(point, result.origin, owner)) continue;
            // Center hits retain close-range damage; distance and cone-edge hits lose power smoothly.
            float damage = weapon.damage * Mathf.Lerp(1f, .15f, depth / weapon.range)
                * Mathf.Lerp(1f, .25f, radialDistance / spreadRadius);
            if (spreadHits.TryGetValue(owner, out var previous) && previous.damage >= damage) continue;
            spreadHits[owner] = new SpreadHit { health = region ? region.health : null, vehicle = vehicle,
                collider = collider, point = point, damage = damage };
        }
        foreach (var hit in spreadHits.Values)
        {
            bool damaged = hit.health ? hit.health.ApplyDamage(hit.damage, gameObject)
                : hit.vehicle.ApplyBulletDamage(hit.damage, gameObject);
            if (!damaged) continue;
            result.collider = hit.collider; result.point = hit.point; result.damaged = true;
            result.killed |= hit.health ? hit.health.IsDead : hit.vehicle.IsDestroyed;
        }
        return Publish(result);
    }

    bool VisibleTo(Vector3 point, Vector3 origin, GameObject owner)
    {
        Vector3 direction = point - origin;
        if (!ClosestHit(new Ray(origin, direction), direction.magnitude + .01f, out var hit)) return true;
        var region = hit.collider.GetComponent<SoldierHitRegion>();
        if (region) return region.health.gameObject == owner;
        var vehicle = hit.collider.GetComponentInParent<Vehicle>();
        return vehicle && vehicle.gameObject == owner;
    }

    bool TryMuzzleBlock(Vector3 origin, out Collider cover, out Vector3 point)
    {
        int count=CombatQueries.OverlapSphere(origin,muzzleClearance,
            ProjectileCollision.WithoutProjectiles(hitLayers),QueryTriggerInteraction.Collide,ref overlapHits);
        for(int i=0;i<count;i++)
        {
            var collider=overlapHits[i];
            if (IsShotCollider(collider))
            {
                cover = collider;
                // The overlap already confirms a blocked muzzle. Non-convex meshes
                // cannot supply ClosestPoint; use the muzzle for impact feedback.
                point = collider is MeshCollider mesh && !mesh.convex
                    ? origin : collider.ClosestPoint(origin);
                return true;
            }
        }
        Vector3 firingOrigin = transform.position + Vector3.up * GetComponent<SoldierPhysicsBody>().height * .75f;
        Vector3 toMuzzle = origin - firingOrigin;
        if (ClosestHit(new Ray(firingOrigin, toMuzzle), toMuzzle.magnitude, out var hit))
        { cover = hit.collider; point = hit.point; return true; }
        cover = null; point = default; return false;
    }

    ShotResult Finish(ShotResult result, SoldierWeaponDefinition weapon)
    {
        // Missing hit regions represent ordinary world surfaces, not damageable actors.
        if (result.collider && result.collider.TryGetComponent(out SoldierHitRegion region))
        {
            result.damaged = region.ApplyHit(weapon, gameObject);
            result.killed = result.damaged && region.health.IsDead;
        }
        else if (result.collider && result.collider.GetComponentInParent<Vehicle>() is Vehicle vehicle)
        {
            result.damaged = vehicle.ApplyBulletDamage(weapon.damage, gameObject);
            result.killed = result.damaged && vehicle.IsDestroyed;
        }
        return Publish(result);
    }

    ShotResult Publish(ShotResult result)
    {
        if(showShotVisual && !GetComponent<SoldierController>().IsLocallyControlled)
        {
            var loadout=GetComponent<SoldierWeaponLoadout>();
            var view=GetComponent<SoldierFirstPersonView>();
            var weaponView=loadout&&loadout.HasFirstPersonPresentation&&view&&view.firstPerson
                ?loadout.FirstPersonView:loadout?loadout.ThirdPersonView:null;
            var muzzle=weaponView?weaponView.effects.muzzleFlash:null;
            if(muzzle)BulletTracer.Emit(muzzle,result.point);
            else BulletTracer.Emit(result.origin,result.point);
        }
        if (drawDebug) Debug.DrawLine(result.origin, result.point, Color.yellow, 1f);
        ShotResolved?.Invoke(result);
        return result;
    }

    bool IsOwnCollider(Collider collider) => collider.transform.IsChildOf(transform);

    bool IsShotCollider(Collider collider) => !IsOwnCollider(collider)
        && !(SoldierPhysicsBody.IsMotorCollider(collider))
        && (!collider.isTrigger || collider.TryGetComponent<SoldierHitRegion>(out _));

    bool ClosestHit(Ray ray, float distance, out RaycastHit closest)
    {
        closest = default;
        bool found = false;
        int count=CombatQueries.Raycast(ray,distance,ProjectileCollision.WithoutProjectiles(hitLayers),QueryTriggerInteraction.Collide,ref queryHits);
        for(int i=0;i<count;i++)
        {
            var hit=queryHits[i];
            if (!IsShotCollider(hit.collider) || (found && hit.distance >= closest.distance)) continue;
            closest = hit;
            found = true;
        }
        return found;
    }
}
