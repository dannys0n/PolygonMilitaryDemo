#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class CombatOptimizationTests
{
    readonly List<GameObject> objects=new List<GameObject>();
    static Type T(string name)=>Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetType(name)!=null).GetType(name);
    GameObject Cube(string name,Vector3 position)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.position=position;objects.Add(go);return go;
    }
    [UnityTearDown] public IEnumerator Cleanup(){foreach(var go in objects)if(go)UnityEngine.Object.Destroy(go);objects.Clear();yield return null;}

    [UnityTest]
    public IEnumerator FullQueryBuffersGrowInsteadOfDiscardingHits()
    {
        var origin=Vector3.up*10000;
        for(int i=0;i<40;i++)Cube("Dense target "+i,origin+Vector3.forward*(i+2));
        Physics.SyncTransforms();
        object[] rayArgs={new Ray(origin,Vector3.forward),50f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore,new RaycastHit[2]};
        int rayCount=(int)T("CombatQueries").GetMethod("Raycast").Invoke(null,rayArgs);
        Assert.AreEqual(40,rayCount);
        Assert.Greater(((RaycastHit[])rayArgs[4]).Length,40);
        object[] overlapArgs={origin+Vector3.forward*21,25f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore,new Collider[2]};
        int overlapCount=(int)T("CombatQueries").GetMethod("OverlapSphere").Invoke(null,overlapArgs);
        Assert.AreEqual(40,overlapCount);
        Assert.Greater(((Collider[])overlapArgs[4]).Length,40);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RoundIgnoresItsOwnerWithoutPerShotCollisionPairs()
    {
        var origin=Vector3.up*10000;
        var owner=Cube("Owner hull",origin);owner.transform.localScale=Vector3.one*4;
        var ownerHealth=owner.AddComponent(T("SoldierHealth"));
        var target=Cube("Enemy",origin+Vector3.forward*10);
        var targetHealth=target.AddComponent(T("SoldierHealth"));
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Projectiles/HelicopterCannonRound.prefab").GetComponent(T("VehicleRocket"));
        var rocket=(Component)T("ProjectilePool").GetMethod("Spawn").MakeGenericMethod(prefab.GetType())
            .Invoke(null,new object[]{prefab,origin,Quaternion.identity});
        rocket.GetType().GetField("showImpactVisual").SetValue(rocket,false);
        rocket.GetType().GetMethod("Launch",new[]{typeof(GameObject),typeof(Transform),typeof(Vector3),typeof(Sprite)})
            .Invoke(rocket,new object[]{owner,owner.transform,Vector3.zero,null});
        Assert.AreEqual(LayerMask.NameToLayer("Projectiles"),rocket.GetComponent<Collider>().gameObject.layer);
        Assert.IsTrue(Physics.GetIgnoreLayerCollision(LayerMask.NameToLayer("Projectiles"),LayerMask.NameToLayer("Projectiles")));
        var member=rocket.GetComponent(T("PooledProjectile"));
        var pairs=(ICollection)member.GetType().GetField("ignored",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(member);
        Assert.AreEqual(0,pairs.Count,"Launch must not rebuild an owner/projectile pair list.");
        yield return new WaitForSeconds(.2f);
        Assert.AreEqual(100f,(float)ownerHealth.GetType().GetProperty("CurrentHealth").GetValue(ownerHealth));
        Assert.Less((float)targetHealth.GetType().GetProperty("CurrentHealth").GetValue(targetHealth),100f);
    }

    Component RoundConfig(Vector3 position, float lifetime = 5)
    {
        var go = new GameObject("Ballistic config"); objects.Add(go); go.transform.position = position;
        var config = go.AddComponent(T("VehicleRocket"));
        go.GetComponent<Rigidbody>().isKinematic = true;
        config.GetType().GetField("ballistic").SetValue(config, true);
        config.GetType().GetField("lifetime").SetValue(config, lifetime);
        config.GetType().GetField("radius").SetValue(config, 1f);
        config.GetType().GetField("speed").SetValue(config, 1000f);
        return config;
    }
    void FireBatch(Component config, Vector3 origin, Vector3 inherited, Transform owner = null)
    {
        T("BallisticRounds").GetMethod("Fire").Invoke(null, new object[] {
            config, origin, Vector3.forward, inherited, owner, owner ? owner.gameObject : null, null, false });
        var manager = (Component)T("BallisticRounds").GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        if (!objects.Contains(manager.gameObject)) objects.Add(manager.gameObject);
    }
    [UnityTest]
    public IEnumerator BatchedRoundsIgnoreOwnerAndDamageTriggerRegions()
    {
        var origin = Vector3.up * 10000;
        var owner = Cube("Owner hull", origin); owner.transform.localScale = Vector3.one * 4;
        var ownerHealth = owner.AddComponent(T("SoldierHealth"));
        var target = Cube("Trigger target", origin + Vector3.forward * 10);
        var health = target.AddComponent(T("SoldierHealth"));
        target.GetComponent<Collider>().isTrigger = true;
        var region = target.AddComponent(T("SoldierHitRegion"));
        region.GetType().GetField("health").SetValue(region, health);
        var config = RoundConfig(origin + Vector3.right * 20);
        Physics.SyncTransforms();
        FireBatch(config, origin, Vector3.zero, owner.transform);
        yield return new WaitForSeconds(.1f);
        Assert.AreEqual(100f, (float)ownerHealth.GetType().GetProperty("CurrentHealth").GetValue(ownerHealth));
        Assert.Less((float)health.GetType().GetProperty("CurrentHealth").GetValue(health), 100f);
        Assert.AreEqual(0, T("BallisticRounds").GetProperty("ActiveCount").GetValue(null));
    }
    [UnityTest]
    public IEnumerator BatchedRoundsPreserveGravityVelocityAndExpiryWithoutClones()
    {
        var origin = Vector3.up * 10000;
        var config = RoundConfig(origin + Vector3.right * 20, .15f);
        config.GetType().GetField("useGravity").SetValue(config, true);
        FireBatch(config, origin, Vector3.right * 20);
        var manager = (Component)T("BallisticRounds").GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var list = (IList)manager.GetType().GetField("rounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        var first = list[0];
        Assert.AreEqual(20f, ((Vector3)first.GetType().GetField("velocity").GetValue(first)).x);
        yield return new WaitForFixedUpdate();
        first = list[0];
        Assert.Less(((Vector3)first.GetType().GetField("velocity").GetValue(first)).y, 0f);
        yield return new WaitForSeconds(.2f);
        Assert.AreEqual(0, list.Count);
        var rockets = UnityEngine.Object.FindObjectsByType(T("VehicleRocket"), FindObjectsInactive.Include);
        Assert.AreEqual(1, rockets.Length, "Configuration only: firing must not clone a projectile hierarchy.");
    }

    [UnityTest]
    public IEnumerator FootQualityUsesNearestGameViewIncludingInsetCamera()
    {
        var origin=Vector3.up*10000;
        var viewer=new GameObject("Main test viewer");objects.Add(viewer);
        viewer.AddComponent<Camera>();viewer.transform.position=origin;
        var method=T("FootIkViewDistance").GetMethod("Weight");
        yield return new WaitForSecondsRealtime(.3f);
        Assert.AreEqual(1f,(float)method.Invoke(null,new object[]{origin+Vector3.forward*5,40f,65f}));
        var distant=origin+Vector3.forward*150;
        Assert.AreEqual(0f,(float)method.Invoke(null,new object[]{distant,40f,65f}));
        var inset=new GameObject("Inset test viewer");objects.Add(inset);
        inset.AddComponent<Camera>();inset.transform.position=distant;
        yield return new WaitForSecondsRealtime(.3f);
        Assert.AreEqual(1f,(float)method.Invoke(null,new object[]{distant,40f,65f}),"Spectated characters must retain detailed feet.");
    }
}
#endif
