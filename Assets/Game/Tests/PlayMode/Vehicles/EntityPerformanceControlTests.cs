#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

public class EntityPerformanceControlTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static Type T(string name) => UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t => t.Name == name);
    static object Get(object o, string name) => o.GetType().GetProperty(name, Flags)?.GetValue(o) ?? o.GetType().GetField(name, Flags)?.GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
    static object EnumArg(Component control, string name, int index) => Enum.ToObject(control.GetType().GetMethod(name).GetParameters()[0].ParameterType, index);
    Component control;
    Component[] Actors => ((IEnumerable)Get(Get(control,"match"),"participants")).Cast<Component>().ToArray();
    Component[] Vehicles => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren(T("Vehicle"))).ToArray();
    void Count(int team,int count) => Call(control,"SetCount",EnumArg(control,"SetCount",team),count);
    void Mode(int index) => Call(control,"SetActivity",EnumArg(control,"SetActivity",index));
    IEnumerator Load(string scene)
    {
        Time.timeScale=1;
        yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_"+scene+".unity");
        control=UnityEngine.Object.FindAnyObjectByType(T("EntityPerformanceControl")) as Component;
        yield return null;
        Assert.IsNotNull(control);
        Assert.AreEqual(0,Get(control,"ActorCount"));
        Assert.IsNull(control.GetType().GetMethod("Update",Flags),"Benchmark controller has no per-frame simulation loop.");
    }
    [UnityTest] public IEnumerator InfantryLiveCountsIdleAndCombatAreControlled()
    {
        yield return Load("Infantry");
        Count(0,3);Count(1,2);yield return null;yield return new WaitForSeconds(.5f);
        Assert.AreEqual(5,Actors.Length);
        Assert.AreEqual(3,Get(control,"RedCount"));Assert.AreEqual(2,Get(control,"BlueCount"));
        foreach(var actor in Actors)
        {
            Assert.IsFalse((bool)Get(actor,"IsAwaitingSpawn"));
            Assert.IsFalse(((Behaviour)actor.GetComponent(T("SandboxInfantryInput"))).enabled);
            Assert.IsFalse(actor.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled);
        }
        Mode(1);yield return new WaitForSeconds(2);
        Assert.IsTrue(Actors.All(a=>((Behaviour)a.GetComponent(T("SandboxInfantryInput"))).enabled));
        Assert.IsTrue(Actors.All(a=>a.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh));
        float firingDeadline=Time.time+12;
        while(Actors.All(a=>(float)Get(a,"LastShotTime")<0)&&Time.time<firingDeadline)yield return null;
        Assert.IsTrue(Actors.Any(a=>(float)Get(a,"LastShotTime")>=0),"Active infantry must actually acquire and fire at the other team.");
        var victim=Actors[0];var health=Get(victim,"health");
        float before=(float)Get(health,"CurrentHealth");
        Call(health,"ApplyDamage",10f,Actors.Last().gameObject,null);
        Assert.AreEqual(before,Get(health,"CurrentHealth"),"AI + FX mode suppresses infantry damage.");
        Mode(0);yield return null;
        Assert.IsTrue(Actors.All(a=>!a.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled));
        Count(0,1);Count(1,0);yield return null;
        Assert.AreEqual(1,Actors.Length);Assert.AreEqual(1,Get(control,"ActorCount"));
        Mode(2);yield return new WaitForSeconds(.5f);
        victim=Actors[0];health=Get(victim,"health");
        Call(health,"ApplyDamage",10000f,null,null);
        Assert.IsTrue((bool)Get(health,"IsDead"));
        yield return new WaitForSeconds(3.7f);
        Assert.IsFalse((bool)Get(health,"IsDead"),"Combat mode retains normal delayed infantry respawning.");
        Count(0,0);yield return null;Assert.AreEqual(0,Actors.Length);
    }
    [UnityTest] public IEnumerator VehicleCountsKeepFullCrewsAcrossModeChangesAndRespawn()
    {
        yield return Load("Vehicles");Count(0,1);Count(1,1);
        yield return null;yield return new WaitForSeconds(1);
        Assert.AreEqual(2,Vehicles.Length);
        int expected=Vehicles.Sum(v=>((Array)Get(v,"seats")).Length);
        Assert.AreEqual(expected,Actors.Length);
        Assert.IsTrue(Actors.All(a=>(bool)Get(a.GetComponent(T("VehicleOccupant")),"IsMounted")));
        Mode(1);yield return new WaitForSeconds(1);
        Assert.IsTrue(Actors.All(a=>((Behaviour)a.GetComponent(T("VehicleAI"))).enabled));
        float firingDeadline=Time.time+15;
        while(Vehicles.All(v=>(float)Get(v.GetComponentInChildren(T("VehicleTurret")),"NextShotTime")<=0)&&Time.time<firingDeadline)yield return null;
        Assert.IsTrue(Vehicles.Any(v=>(float)Get(v.GetComponentInChildren(T("VehicleTurret")),"NextShotTime")>0),"Active gunners must actually fire, not only enable their AI.");
        var vehicle=Vehicles[0];float before=(float)Get(vehicle,"Health");
        Call(vehicle,"ApplyBulletDamage",100f,null,null);
        Assert.AreEqual(before,Get(vehicle,"Health"));
        Mode(0);yield return null;
        Assert.IsTrue(Actors.All(a=>(bool)Get(a.GetComponent(T("VehicleOccupant")),"IsMounted")),"Idling production AI must not eject the control crew.");
        Assert.IsTrue(Actors.All(a=>!((Behaviour)a.GetComponent(T("VehicleAI"))).enabled));
        Mode(2);Call(vehicle,"ApplyBulletDamage",1000000f,null,null);
        Assert.IsTrue((bool)Get(vehicle,"IsDestroyed"));
        yield return new WaitForSeconds(4);
        Assert.AreEqual(2,Vehicles.Length);Assert.AreEqual(expected,Actors.Length);
        Assert.IsTrue(Vehicles.All(v=>!(bool)Get(v,"IsDestroyed")));
        Count(0,0);Count(1,0);yield return null;
        Assert.AreEqual(0,Actors.Length);Assert.AreEqual(0,Vehicles.Length);
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if(control){Count(0,0);Count(1,0);}
        Time.timeScale=1;yield return null;
        LogAssert.NoUnexpectedReceived();
    }
}
#endif


