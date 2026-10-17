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
