#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ProjectilePoolTests
{
    static Type TypeOf(string name) =>
        System.Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetType(name) != null).GetType(name);
    static object InvokeStatic(string method, params object[] args) => TypeOf("ProjectilePool").GetMethod(method).Invoke(null, args);
    static Component Spawn(Component prefab) => (Component)TypeOf("ProjectilePool").GetMethod("Spawn")
        .MakeGenericMethod(prefab.GetType()).Invoke(null, new object[] { prefab, Vector3.up * 10000, Quaternion.identity });
    static void Set(Component c, string field, object value) => c.GetType().GetField(field).SetValue(c, value);
    static void Launch(Component c) => c.GetType().GetMethod("Launch", new[] { typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Sprite) })
        .Invoke(c, new object[] { null, null, Vector3.zero, null });

    [UnityTest]
    public IEnumerator ExpiredRoundsAreReusedWithFreshPhysicsLifetimeAndGuidance()
    {
        var template = new GameObject("Pool test template");
        template.SetActive(false);
        template.AddComponent<Rigidbody>();
        template.AddComponent<SphereCollider>();
        var prefab = template.AddComponent(TypeOf("VehicleRocket"));
        template.AddComponent(TypeOf("VehicleMissileGuidance"));
        Set(prefab, "lifetime", .05f);
        var first = Spawn(prefab);
        Launch(first);
        first.GetType().GetMethod("LimitRemainingLifetime").Invoke(first, new object[] { .01f, false });
        first.GetComponent<Rigidbody>().angularVelocity = Vector3.one * 10;
        yield return new WaitForSeconds(.12f);
        Assert.IsFalse(first.gameObject.activeSelf, "Expiry must return the shot rather than destroy it.");
        var second = Spawn(prefab);
        Assert.AreSame(first, second, "An idle projectile should service the next shot.");
        Assert.AreEqual(Vector3.zero, second.GetComponent<Rigidbody>().angularVelocity);
        Assert.IsTrue(second.GetComponent<Collider>().enabled);
        Assert.IsFalse((bool)second.GetComponent(TypeOf("VehicleMissileGuidance")).GetType().GetProperty("HasGuidance")
            .GetValue(second.GetComponent(TypeOf("VehicleMissileGuidance"))));
        Launch(second);
        Assert.Greater((float)second.GetType().GetProperty("RemainingLifetime").GetValue(second), .03f);
        var simultaneous = Spawn(prefab);
        Assert.AreNotSame(second, simultaneous, "Live shots must not be recycled.");
        InvokeStatic("Release", second, 0f);
        InvokeStatic("Release", simultaneous, 0f);
        UnityEngine.Object.Destroy(template);
    }

    [UnityTest]
    public IEnumerator ReturnRestoresCollisionPairsAndWaitsForExhaustTail()
    {
        var template = new GameObject("Tail test template");
        template.SetActive(false);
        template.AddComponent<Rigidbody>(); template.AddComponent<SphereCollider>();
        var prefab = template.AddComponent(TypeOf("VehicleRocket"));
        var owner = new GameObject("Previous shooter");
        var ownerCollider = owner.AddComponent<BoxCollider>();
        var shot = Spawn(prefab);
        var collider = shot.GetComponent<Collider>();
        InvokeStatic("IgnoreCollision", shot, collider, ownerCollider);
        Assert.IsTrue(Physics.GetIgnoreCollision(collider, ownerCollider));
        InvokeStatic("Release", shot, .05f);
        Assert.IsFalse(Physics.GetIgnoreCollision(collider, ownerCollider));
        Assert.IsTrue(shot.gameObject.activeSelf, "Keep the exhaust alive while it fades.");
        Assert.IsFalse(collider.enabled);
        yield return new WaitForSeconds(.1f);
        Assert.IsFalse(shot.gameObject.activeSelf);
        UnityEngine.Object.Destroy(template); UnityEngine.Object.Destroy(owner);
    }

    [UnityTest]
    public IEnumerator ReusedCannonRoundStillDamagesEveryTarget()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Prefabs/Vehicles/Projectiles/HelicopterCannonRound.prefab").GetComponent(TypeOf("VehicleRocket"));
        Component previous = null;
        for (int i = 0; i < 2; i++)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = Vector3.up * 10000 + Vector3.forward * 10;
            var health = target.AddComponent(TypeOf("SoldierHealth"));
            var shot = Spawn(prefab);
            if (previous) Assert.AreSame(previous, shot);
            Set(shot, "showImpactVisual", false);
            Launch(shot);
            yield return new WaitForSeconds(.2f);
            Assert.Less((float)health.GetType().GetProperty("CurrentHealth").GetValue(health), 100f);
            Assert.IsFalse(shot.gameObject.activeSelf);
            previous = shot;
            UnityEngine.Object.Destroy(target);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator ReturningGrenadeCancelsOldFuseBeforeReuse()
    {
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Equipment/Grenades/Frag.asset");
        var definition = UnityEngine.Object.Instantiate(asset);
        var dataType = definition.GetType();
        dataType.GetField("fuse").SetValue(definition, .04f);
        var prefab = (Component)dataType.GetField("projectile").GetValue(definition);
        var owner = new GameObject("Grenade shooter");
        var first = Spawn(prefab);
        first.GetType().GetMethod("Launch").Invoke(first, new object[] { definition, owner, Vector3.zero });
        InvokeStatic("Release", first, 0f);
        dataType.GetField("fuse").SetValue(definition, .3f);
        var second = Spawn(prefab);
        Assert.AreSame(first, second);
        second.GetType().GetMethod("Launch").Invoke(second, new object[] { definition, owner, Vector3.zero });
        yield return new WaitForSeconds(.12f);
        Assert.IsTrue(second.gameObject.activeSelf, "The previous fuse must not detonate the new grenade.");
        Assert.IsTrue(second.GetComponent<Collider>().enabled);
        InvokeStatic("Release", second, 0f);
        UnityEngine.Object.Destroy(definition); UnityEngine.Object.Destroy(owner);
    }
}
#endif
