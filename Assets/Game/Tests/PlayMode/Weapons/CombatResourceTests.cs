#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CombatResourceTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Type TypeOf(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).First(t=>t!=null);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
    static object Get(object o,string name)=>o.GetType().GetProperty(name,Flags).GetValue(o);

    [Test] public void PrimaryReserveResetsFromDefinitionAndReloadConsumesReserve()
    {
        var d=ScriptableObject.CreateInstance(TypeOf("SoldierWeaponDefinition"));
        try
        {
            Set(d,"magazineCapacity",30);Set(d,"startingReserveAmmo",90);Set(d,"roundsPerMinute",600f);
            var t=TypeOf("SoldierWeaponLoadout").GetNestedType("WeaponState");
            var state=Activator.CreateInstance(t,d,3);
            Assert.AreEqual(270,Get(state,"ReserveAmmo"));
            t.GetMethod("TryConsumeRound").Invoke(state,new object[]{0f,d});
            t.GetMethod("CompleteReload").Invoke(state,new object[]{d});
            Assert.AreEqual(269,Get(state,"ReserveAmmo"));Assert.AreEqual(30,Get(state,"MagazineAmmo"));
            Assert.AreEqual(270,Get(Activator.CreateInstance(t,d,3),"ReserveAmmo"));
            t.GetProperty("InfiniteOverride").SetValue(state,true);
            for(int i=1;i<=100;i++)Assert.IsTrue((bool)t.GetMethod("TryConsumeRound").Invoke(state,new object[]{(float)i,d}));
            Assert.AreEqual(30,Get(state,"MagazineAmmo"));
            Assert.IsFalse((bool)Get(Activator.CreateInstance(t,d,1),"InfiniteOverride"));
        }
        finally{UnityEngine.Object.DestroyImmediate(d);}
    }
    [Test] public void HighRateVisualCadenceScalesFromTwoToFourShots()
    {
        var method=TypeOf("BulletTracer").GetMethod("ShowVisual");
        for(int i=1;i<=16;i++)
        {
            Assert.AreEqual(i%2==0,method.Invoke(null,new object[]{i,600f}));
            Assert.AreEqual(i%3==0,method.Invoke(null,new object[]{i,900f}));
            Assert.AreEqual(i%4==0,method.Invoke(null,new object[]{i,1200f}));
            Assert.IsTrue((bool)method.Invoke(null,new object[]{i,500f}));
        }
    }
}
#endif
