#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public class SandboxBehaviorTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Type T(string name)=>UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t=>t.Name==name);
    static object Get(object o,string name)=>o.GetType().GetProperty(name,Flags)?.GetValue(o)??o.GetType().GetField(name,Flags)?.GetValue(o);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    Component scenario;
    IEnumerator Load(string name)
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Tests/AI/"+name+".unity",new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;scenario=UnityEngine.Object.FindAnyObjectByType(T("CombinedArmsTestScenario")) as Component;
        Assert.NotNull(scenario);Call(scenario,"StartScenario");yield return new WaitForSeconds(1);
    }
    [UnityTearDown] public IEnumerator Cleanup(){if(scenario)Call(scenario,"ResetScenario");yield return null;}
    [UnityTest] public IEnumerator SettledAimHasNoResidualNoiseOrTrackingLag()
    {
        yield return Load("AI_Accuracy");
        var actors=(IList)Get(scenario,"actors");Assert.AreEqual(4,actors.Count);
        var actor=(Component)actors[0];var enemy=(Component)actors[2];var ai=actor.GetComponent(T("ArenaAIInput"));
        Assert.AreEqual(1.5f,Get(ai,"aimSettleTime"));Assert.AreEqual(0f,Get(ai,"settledAimError"));
        ai.GetType().GetField("target",Flags).SetValue(ai,enemy);
        ai.GetType().GetField("acquiredAt",Flags).SetValue(ai,Time.time-2);
        ai.GetType().GetField("aimNoise",Flags).SetValue(ai,new Vector2(4,4));
        ai.GetType().GetField("trackedAim",Flags).SetValue(ai,Vector3.one*100);
        var expected=(Vector3)Call(ai,"AimPoint",enemy);
        Assert.Less(Vector3.Distance(expected,(Vector3)Call(ai,"GetImperfectAim")),.00001f);
        Assert.AreEqual(Vector2.zero,Get(ai,"aimNoise"));Assert.AreEqual(0f,Get(ai,"CurrentAimError"));
    }
    [UnityTest] public IEnumerator SettledAimActuallyDamagesAndKillsInfantry()
    {
        yield return Load("AI_Accuracy");
        var actors=((IEnumerable)Get(scenario,"actors")).Cast<Component>().ToArray();
        // Keep the authored TPS camera offset and the production input/shooting path.
        // Stationary opponents isolate aim calibration from tactical movement.
        foreach(var actor in actors)
            actor.GetComponent(T("ArenaAIInput")).GetType().GetProperty("HoldPosition").SetValue(actor.GetComponent(T("ArenaAIInput")),true);
        var victim=actors[2];var health=Get(victim,"health");float initial=(float)Get(health,"CurrentHealth");
        foreach(var actor in actors.Skip(2))((Behaviour)actor.GetComponent(T("ArenaAIInput"))).enabled=false;
        bool damaged=false,killed=false;float until=Time.time+16;
        while(Time.time<until)
        {
            damaged|=(float)Get(health,"CurrentHealth")<initial;
            killed|=(bool)Get(health,"IsDead")||actors.Take(2).Any(a=>(int)Get(a,"Kills")>0);
            if(damaged&&killed)break;
            yield return null;
        }
        Assert.IsTrue(damaged,"Settled production shots must damage an opposing infantry collider.");
                string detail=string.Join("\n",actors.Select(a=>{
            var ai=a.GetComponent(T("ArenaAIInput"));var life=Get(ai,"life");var look=Get(life,"look");var camera=(Camera)Get(look,"aimCamera");
            var target=Get(ai,"Target") as Component;var loadout=Get(Get(ai,"soldier"),"loadout");var state=Get(loadout,"EquippedState");
            return a.name+" health="+Get(Get(a,"health"),"CurrentHealth")+" kills="+Get(a,"Kills")+" state="+Get(ai,"State")+" target="+(target?target.name:"none")+" yaw="+Get(look,"Yaw")+" camera="+camera.transform.eulerAngles+" error="+(target?Vector3.Angle(camera.transform.forward,target.transform.position+Vector3.up*1.25f-camera.transform.position):0)+" mag="+Get(state,"MagazineAmmo");}));
        Assert.IsTrue(killed,"Production aim must lead to a kill, not just a perfect internal aim point.\n"+detail);
        Assert.AreEqual(0,Get(scenario,"errors"));
    }
    [UnityTest] public IEnumerator SandboxKeepsScoringBeyondNormalMatchDuration()
    {
        yield return Load("AI_Accuracy");
        var match=Get(scenario,"match");var rules=Get(match,"rules");var actors=(IList)Get(scenario,"actors");
        var points=(Array)Get(rules,"points");var point=(Component)points.GetValue(0);
        for(int i=0;i<actors.Count;i++)((Component)actors[i]).transform.position=new Vector3(90+i*3,0,90);
        ((Component)actors[0]).transform.position=point.transform.position;
        Call(rules,"Tick",20f);int initial=(int)Get(match,"RedScore");Assert.Greater(initial,0);
        Call(match,"Tick",100000f);
        Assert.IsTrue((bool)Get(match,"IsRunning"));Assert.IsFalse((bool)Get(rules,"HasEnded"));
        Assert.Greater((int)Get(match,"RedScore"),initial);Assert.AreEqual("SANDBOX",Get(match,"ClockText"));
    }
    [UnityTest] public IEnumerator OrchestratorIssuesMixedRolesAndBoardsProductionVehicles()
    {
        yield return Load("AI_Orchestrator");
        var actors=(IList)Get(scenario,"actors");Assert.AreEqual(24,actors.Count);
        var orchestrator=(Component)Get(scenario,"orchestrator");
        var roles=((IEnumerable)Get(orchestrator,"Orders")).Cast<object>().Select(o=>Get(o,"role").ToString()).Distinct().ToArray();
        CollectionAssert.AreEquivalent(new[]{"Infantry","Armor","Transport","Helicopter","Fighter"},roles);
        int mounted=0;float until=Time.time+35;
        while(Time.time<until)
        {
            mounted=actors.Cast<Component>().Count(a=>(bool)Get(a.GetComponent(T("VehicleOccupant")),"IsMounted"));
            if(mounted>=4)break;yield return null;
        }
        Assert.GreaterOrEqual(mounted,4,"Multiple production actors must actually board the assigned vehicles.");
        Assert.AreEqual(0,Get(scenario,"errors"));
        // Isolate departure from combat policy: armed crews correctly brake to engage enemies.
        foreach(var actor in actors.Cast<Component>().ToArray())if(Get(actor,"team").ToString()=="Blue")actor.gameObject.SetActive(false);
        Call(scenario,"Depart");yield return new WaitForSeconds(12);
        var moving=((IEnumerable)Get(scenario,"vehicles")).Cast<Component>().ToArray();
        string detail=string.Join("\n",moving.Select(v=>{
            var driver=Get(v,"Driver") as Component;var ai=driver?driver.GetComponent(T("ArenaVehicleAI")):null;
            var input=Get(v,"DriverInput");var body=v.GetComponent<Rigidbody>();
            return v.name+" at "+v.transform.position+" speed="+body.linearVelocity.magnitude+" driver="+(driver?driver.name:"none")+" input="+Get(input,"move")+" brake="+Get(input,"brake")+" goal="+(ai?Get(ai,"Goal"):"none")+" state="+(ai?Get(ai,"State"):"none");}));
        Assert.IsTrue(moving.Any(v=>v.GetComponent<Rigidbody>().linearVelocity.magnitude>1),"Departure must move a vehicle through its normal motor.\n"+detail);
    }
}
#endif
