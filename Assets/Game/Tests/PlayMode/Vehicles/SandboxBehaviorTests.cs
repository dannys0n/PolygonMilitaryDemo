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
    static void Set(object o,string name,object value)
    {
        var field=o.GetType().GetField(name,Flags);
        if(field!=null)field.SetValue(o,value);else o.GetType().GetProperty(name,Flags).SetValue(o,value);
    }
    static Component Rider(Component actor)=>actor.GetComponent(T("VehicleOccupant"));
    static void Board(Component actor,Component vehicle,int seat)
    {
        var rider=Rider(actor);
        if((bool)Get(rider,"IsMounted"))Assert.IsTrue((bool)Call(rider,"Leave",true));
        actor.transform.position=vehicle.transform.TransformPoint(((Bounds)Get(vehicle,"EntryBounds")).center);
        Physics.SyncTransforms();
        Assert.IsTrue((bool)Call(vehicle,"Board",rider,seat),"Production boarding must succeed for test setup.");
    }
    static void MakeHuman(Component actor)
    {
        var local=(Behaviour)actor.gameObject.AddComponent(T("SoldierLocalInput"));local.enabled=false;
        ((Behaviour)actor.GetComponent(T("ArenaAIInput"))).enabled=false;
        ((Behaviour)actor.GetComponent(T("ArenaVehicleAI"))).enabled=false;
        Set(actor.GetComponent(T("SoldierController")),"inputSource",local);
    }
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
        Assert.AreEqual(3f,Get(ai,"aimSettleTime"));Assert.AreEqual(0f,Get(ai,"settledAimError"));
        ai.GetType().GetField("target",Flags).SetValue(ai,enemy);
        ai.GetType().GetField("acquiredAt",Flags).SetValue(ai,Time.time-2);
        Assert.Greater((float)Get(ai,"CurrentAimError"),0f,"Aim must still be settling at two seconds");
        ai.GetType().GetField("acquiredAt",Flags).SetValue(ai,Time.time-3.01f);
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
    [UnityTest] public IEnumerator OrchestratorRespectsHumanSeatSwapAndDriverTakeover()
    {
        yield return Load("AI_Orchestrator");
        var actors=((IEnumerable)Get(scenario,"actors")).Cast<Component>().ToArray();
        var orchestrator=(Component)Get(scenario,"orchestrator");
        var bot=actors[0];var human=actors[1];
        var vehicle=((IEnumerable)Get(scenario,"vehicles")).Cast<Component>()
            .First(v=>!(bool)Get(v,"IsAircraft") && ((Array)Get(v,"seats")).Length>=3);
        foreach(var seat in (Array)Get(vehicle,"seats"))
            if(Get(seat,"occupant") is Component occupant)Call(occupant,"Leave",true);
        MakeHuman(human);Board(bot,vehicle,0);Board(human,vehicle,1);
        Call(orchestrator,"Replan");
        Assert.IsTrue((bool)Call(Rider(human),"SwitchSeat",0));
        Call(orchestrator,"Replan");
        var botAI=bot.GetComponent(T("ArenaVehicleAI"));
        Assert.AreSame(Rider(human),Get(vehicle,"Driver"));
        Assert.AreEqual(1,Get(Rider(bot),"SeatIndex"));
        Assert.IsFalse((bool)Get(botAI,"MissionCrew"),"A displaced driver must act as support in its actual seat.");
        Assert.IsFalse((bool)Get(botAI,"HoldDeparture"),"A passenger must not retain a driver loading hold.");
        Assert.IsFalse(((IEnumerable)Get(orchestrator,"Orders")).Cast<object>().Any(o=>Get(o,"actor")==human));

        Call(Rider(human),"Leave",true);Call(orchestrator,"Replan");
        Assert.AreSame(Rider(bot),Get(vehicle,"Driver"),"An AI already aboard should take over vacant controls.");
        Assert.IsTrue((bool)Get(botAI,"MissionCrew"));
        // Outside entry uses the real displacement path, forcing the bot entirely out.
        Board(human,vehicle,0);Call(orchestrator,"Replan");
        Assert.IsFalse((bool)Get(Rider(bot),"IsMounted"));
        Assert.IsFalse((bool)Get(bot.GetComponent(T("ArenaAIInput")),"HoldMovement"));
        Assert.IsFalse((bool)Get(botAI,"HoldDeparture"));
        var order=((IEnumerable)Get(orchestrator,"Orders")).Cast<object>().Single(o=>Get(o,"actor")==bot);
        Assert.IsNull(Get(order,"vehicle"));
        Assert.Greater((float)Get(order,"retryAt"),Time.time,"An evicted bot needs a cooldown rather than immediately trying to retake the seat.");
        Assert.AreEqual(0,Get(scenario,"errors"));
    }
    [UnityTest] public IEnumerator OrchestratorFillsHumanVehicleWithoutExistingDriverOrder()
    {
        yield return Load("AI_Orchestrator");
        var actors=((IEnumerable)Get(scenario,"actors")).Cast<Component>().ToArray();
        var orchestrator=(Component)Get(scenario,"orchestrator");
        var vehicle=((IEnumerable)Get(scenario,"vehicles")).Cast<Component>()
            .First(v=>!(bool)Get(v,"IsAircraft") && ((Array)Get(v,"seats")).Length>=3);
        foreach(var actor in actors)if((bool)Get(Rider(actor),"IsMounted"))Call(Rider(actor),"Leave",true);
        foreach(var order in ((IEnumerable)Get(orchestrator,"Orders")).Cast<object>().ToArray())
        {
            Call(orchestrator,"Release",order);
            Set(order,"role",Enum.Parse(Get(order,"role").GetType(),"Infantry"));Set(order,"retryAt",0f);
        }
        var human=actors[0];MakeHuman(human);Board(human,vehicle,1);
        for(int i=1;i<12;i++)actors[i].transform.position=vehicle.transform.position+new Vector3(10+i,0,-12);
        Physics.SyncTransforms();Call(orchestrator,"Replan");Call(orchestrator,"Replan");
        var seats=(Array)Get(vehicle,"seats");
        Assert.AreSame(Rider(human),Get(seats.GetValue(1),"occupant"));
        Assert.NotNull(Get(seats.GetValue(0),"reservation"),"A bot should fill the empty driver seat for a human passenger.");
        Assert.NotNull(Get(seats.GetValue(2),"reservation"),"Mixed crews should also recruit into remaining seats.");
        Assert.IsTrue((bool)Call(Rider(human),"SwitchSeat",0));
        Call(orchestrator,"Replan");yield return null;Call(orchestrator,"Replan");
        Assert.AreSame(Rider(human),Get(vehicle,"Driver"));
        Assert.IsTrue(((IEnumerable)Get(orchestrator,"Orders")).Cast<object>().Any(o=>Get(o,"vehicle")==vehicle),
            "Bots should keep supporting the vehicle when the human takes its controls.");
        Assert.IsFalse(((IEnumerable)Get(orchestrator,"Orders")).Cast<object>().Any(o=>Get(o,"actor")==human));
        Assert.AreEqual(0,Get(scenario,"errors"));
    }
}
#endif
