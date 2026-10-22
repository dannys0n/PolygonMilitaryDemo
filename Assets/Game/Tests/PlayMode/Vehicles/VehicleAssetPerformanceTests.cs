#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class VehicleAssetPerformanceTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Type T(string name)=>UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t=>t.Name==name);
    static object Get(object o,string n)=>o.GetType().GetProperty(n,Flags)?.GetValue(o)??o.GetType().GetField(n,Flags)?.GetValue(o);
    static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,Flags).Invoke(o,args);
    Component control;
    Component[] Vehicles=>UnityEngine.Object.FindObjectsByType(T("Vehicle")).Cast<Component>().ToArray();
    [UnityTest] public IEnumerator EveryVehicleSupportsUncrewedScaleFreezeRestoreAndCleanup()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_Vehicle_Assets.unity");yield return null;
        control=UnityEngine.Object.FindAnyObjectByType(T("VehiclePerformanceControl")) as Component;
        var prefabs=(Array)Get(control,"prefabs");Assert.GreaterOrEqual(prefabs.Length,28);
        var bays=new System.Collections.Generic.HashSet<Vector2Int>();
        var bayMethod=control.GetType().GetMethod("Bay",BindingFlags.NonPublic|BindingFlags.Static);
        for(int i=0;i<(int)Get(control,"maximumVehicles");i++)
        {
            var bay=(Vector2Int)bayMethod.Invoke(null,new object[]{i});
            Assert.IsTrue(bays.Add(bay),"Every instance needs a unique bay.");
            Assert.LessOrEqual(Mathf.Abs(bay.x),8);Assert.LessOrEqual(Mathf.Abs(bay.y),8);
        }
        Assert.IsNull(control.GetType().GetMethod("Update",Flags));Assert.AreEqual(0,Get(control,"TotalCount"));
        for(int i=0;i<prefabs.Length;i++)Call(control,"SetCount",i,1);
        yield return new WaitForSeconds(2);
        Assert.AreEqual(prefabs.Length,Vehicles.Length);Assert.AreEqual(prefabs.Length,Get(control,"TotalCount"));
        Assert.IsNull(UnityEngine.Object.FindAnyObjectByType(T("MatchParticipant")),"Asset baseline must not instantiate infantry or crews.");
        foreach(var vehicle in Vehicles)
        {
            Assert.IsFalse((bool)Get(vehicle,"IsDestroyed"));
            Assert.IsFalse((bool)Call(vehicle,"ApplyDamage",1000000f,null,null),"Incidental damage cannot shrink the benchmark fleet.");
        }
        Call(control,"SetFrozen",true);yield return null;
        Assert.IsTrue(Vehicles.All(v=>v.GetComponent<Rigidbody>().isKinematic));
        Assert.IsTrue(Vehicles.All(v=>!((Behaviour)v).enabled));
        Call(control,"SetCount",0,12);Call(control,"SetCount",1,3);yield return null;
        Assert.AreEqual(prefabs.Length+13,Get(control,"TotalCount"));
        Assert.IsTrue(Vehicles.All(v=>v.GetComponent<Rigidbody>().isKinematic),"New instances must inherit frozen mode.");
        Call(control,"FrameFleet");
        var observer=(Component)Get(control,"observer");Assert.Greater(observer.transform.position.y,100);
        Call(control,"SetCount",0,2);yield return null;
        Assert.AreEqual(prefabs.Length+3,Get(control,"TotalCount"));
        Call(control,"SetFrozen",false);yield return new WaitForSeconds(.5f);
        Assert.IsTrue(Vehicles.All(v=>((Behaviour)v).enabled));
        Assert.IsTrue(Vehicles.All(v=>!v.GetComponent<Rigidbody>().isKinematic));
        Call(control,"Clear");yield return null;
        Assert.AreEqual(0,Get(control,"TotalCount"));Assert.AreEqual(0,Vehicles.Length);
        Call(control,"SetCount",0,1);yield return null;Assert.AreEqual(1,Vehicles.Length,"Cleared grid bays can be reused.");
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if(control)Call(control,"Clear");yield return null;LogAssert.NoUnexpectedReceived();
    }
}
#endif
