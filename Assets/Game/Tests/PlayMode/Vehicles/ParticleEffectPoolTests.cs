#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ParticleEffectPoolTests
{
    static Type PoolType => Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetType("ParticleEffectPool") != null).GetType("ParticleEffectPool");
    static ParticleSystem Play(ParticleSystem prefab, Vector3? scale = null, bool persistent = false) =>
        (ParticleSystem)PoolType.GetMethod("Play").Invoke(null,
            new object[] { prefab, Vector3.up * 10000, Quaternion.identity, scale, persistent });
    static void Stop(ParticleSystem particles) => PoolType.GetMethod("Stop").Invoke(null, new object[] { particles });
    static ParticleSystem Template(string name, float lifetime, bool loop = false)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = .05f; main.startLifetime = lifetime;
        main.startSpeed = 0; main.loop = loop;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
        return ps;
    }

    [UnityTest]
    public IEnumerator EffectWaitsForChildrenThenReusesAndRestoresScale()
    {
        var prefab = Template("Composite explosion", .02f);
        prefab.transform.localScale = Vector3.one * 2;
        var child = Template("Long smoke tail", .35f);
        child.transform.SetParent(prefab.transform, false);
        child.gameObject.SetActive(true);
        var first = Play(prefab, Vector3.one * 5);
        yield return new WaitForSeconds(.15f);
        Assert.IsTrue(first.gameObject.activeSelf, "The root ending must not truncate child smoke.");
        yield return new WaitForSeconds(.45f);
        Assert.IsTrue(first, "StopAction.Destroy must not destroy a pooled effect.");
        Assert.IsFalse(first.gameObject.activeSelf);
        var second = Play(prefab);
        Assert.AreSame(first, second);
        Assert.AreEqual(Vector3.one * 2, second.transform.localScale);
        var concurrent = Play(prefab);
        Assert.AreNotSame(second, concurrent, "Never steal an effect still in use.");
        Stop(second); Stop(concurrent);
        UnityEngine.Object.Destroy(prefab.gameObject);
    }

    [UnityTest]
    public IEnumerator PersistentSmokeFadesAfterStopAndCanBeReusedAsOneShot()
    {
        var prefab = Template("Looping wreck smoke", .08f, true);
        var smoke = Play(prefab, persistent: true);
        yield return new WaitForSeconds(.3f);
        Assert.IsTrue(smoke.gameObject.activeSelf);
        Assert.IsTrue(smoke.isPlaying);
        Stop(smoke);
        yield return new WaitForSeconds(.3f);
        Assert.IsFalse(smoke.gameObject.activeSelf);
        var next = Play(prefab);
        Assert.AreSame(smoke, next);
        Assert.IsFalse(next.main.loop, "A one-shot must not inherit persistent emission.");
        yield return new WaitForSeconds(.35f);
        Assert.IsFalse(next.gameObject.activeSelf);
        UnityEngine.Object.Destroy(prefab.gameObject);
    }

    [UnityTest]
    public IEnumerator PersistentEffectIsNotReassignedBeforeOwnerReleasesIt()
    {
        var prefab = Template("Owner-held smoke", .02f);
        var held = Play(prefab, persistent: true);
        yield return new WaitForSeconds(.3f);
        Assert.IsTrue(held.gameObject.activeSelf, "The owner still holds this effect even if its particles end.");
        var other = Play(prefab);
        Assert.AreNotSame(held, other);
        Stop(held); Stop(other);
        UnityEngine.Object.Destroy(prefab.gameObject);
    }

    [UnityTest]
    public IEnumerator ClearingPoolAfterSceneDestroysIdleEffectIsSafe()
    {
        var prefab=Template("Scene teardown effect",.02f);
        var effect=Play(prefab);
        yield return new WaitForSeconds(.3f);
        Assert.IsFalse(effect.gameObject.activeSelf);
        UnityEngine.Object.Destroy(effect.gameObject);yield return null;
        var pools=(System.Collections.IDictionary)PoolType.GetField("pools",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
        var pool=pools[prefab];
        Assert.DoesNotThrow(()=>pool.GetType().GetMethod("Clear").Invoke(pool,null));
        UnityEngine.Object.Destroy(prefab.gameObject);
    }

    [UnityTest]
    public IEnumerator ActualGasTankExplosionReturnsWithoutLosingChildren()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Prefabs/Effects/Vehicles/AtlasNet/Rocket_GasTankExplosion.prefab").GetComponent<ParticleSystem>();
        var effect = Play(prefab);
        int children = effect.GetComponentsInChildren<ParticleSystem>(true).Length;
        Assert.Greater(children, 1);
        Stop(effect);
        float until = Time.time + 20;
        while (effect.gameObject.activeSelf && Time.time < until) yield return null;
        Assert.IsFalse(effect.gameObject.activeSelf, "Real explosion particles must finish and return.");
        var reused = Play(prefab);
        Assert.AreSame(effect, reused);
        Assert.AreEqual(children, reused.GetComponentsInChildren<ParticleSystem>(true).Length);
        Stop(reused);
    }
}
#endif
