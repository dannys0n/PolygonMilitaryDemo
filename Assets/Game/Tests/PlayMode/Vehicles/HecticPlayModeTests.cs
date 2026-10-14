#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using Object=UnityEngine.Object;

// Exercise commands through the production motor and collision geometry, without changing scenes.
public class HecticPlayModeTests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    readonly List<GameObject> roots=new List<GameObject>();
    readonly List<UnityEngine.AI.NavMeshDataInstance> navigation=new List<UnityEngine.AI.NavMeshDataInstance>();
    readonly List<UnityEngine.AI.NavMeshData> navigationData=new List<UnityEngine.AI.NavMeshData>();
    Component match;
    static Type T(string n)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(n)).First(t=>t!=null);
    static object Get(object o,string n){var p=o.GetType().GetProperty(n,F);return p!=null?p.GetValue(o):o.GetType().GetField(n,F).GetValue(o);}
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,F).Invoke(o,args);
    GameObject Root(string n){var g=new GameObject(n);roots.Add(g);return g;}
    Component Vehicle(string path)
    {
        var g=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/"+path+".prefab"),new Vector3(0,1000,0),Quaternion.identity);roots.Add(g);
        var v=(Behaviour)g.GetComponent(T("Vehicle"));v.enabled=false;((Behaviour)Get(v,"motor")).enabled=false;
        var body=g.GetComponent<Rigidbody>();body.collisionDetectionMode=CollisionDetectionMode.Discrete;body.isKinematic=true;return v;
    }
    Component Actor(Vector3 position)
    {
        if(!match){match=Root("Test match").AddComponent(T("ArenaMatch"));((Behaviour)match).enabled=false;Set(match,"settings",AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Matches/Vehicle_Test.asset"));}
        var holder=Root("Inactive actor setup");holder.SetActive(false);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Matches/Vehicles/Player_Polygon_VehicleAI.prefab");
        var g=Object.Instantiate(prefab,position,Quaternion.identity,holder.transform);roots.Add(g);
        Set(g.GetComponent(T("ArenaParticipant")),"match",match);
        foreach(string type in new[]{"ArenaAIInput","ArenaVehicleAI","SoldierController","ArenaParticipant"}){var b=g.GetComponent(T(type)) as Behaviour;if(b)b.enabled=false;}
        var nav=g.GetComponent<UnityEngine.AI.NavMeshAgent>();if(nav)nav.enabled=false;
        holder.SetActive(true);return g.GetComponent(T("ArenaVehicleAI"));
    }
    [UnityTearDown] public IEnumerator Cleanup(){foreach(var g in roots)if(g){var occupant=g.GetComponent(T("VehicleOccupant"));if(occupant&&(bool)Get(occupant,"IsMounted"))Call(occupant,"Leave",true);}foreach(var g in roots)if(g)Object.Destroy(g);foreach(var instance in navigation)instance.Remove();foreach(var data in navigationData)Object.Destroy(data);navigation.Clear();navigationData.Clear();roots.Clear();match=null;yield return null;}
    void BuildExitNavigation(GameObject floor,Component actor)
    {
        var agent=actor.GetComponent<UnityEngine.AI.NavMeshAgent>();
        var settings=UnityEngine.AI.NavMesh.GetSettingsByID(agent.agentTypeID);
        var source=new UnityEngine.AI.NavMeshBuildSource{shape=UnityEngine.AI.NavMeshBuildSourceShape.Box,transform=floor.transform.localToWorldMatrix,size=Vector3.one,area=0};
        var data=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,new List<UnityEngine.AI.NavMeshBuildSource>{source},new Bounds(new Vector3(0,1000,0),new Vector3(50,10,50)),Vector3.zero,Quaternion.identity);
        Assert.NotNull(data,"Build a navigable infantry floor for legitimate AI dismounts");
        navigationData.Add(data);navigation.Add(UnityEngine.AI.NavMesh.AddNavMeshData(data));
    }
    IEnumerator EnterCargo(string id)
    {
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);roots.Add(ground);ground.transform.position=new Vector3(0,999.5f,0);ground.transform.localScale=new Vector3(40,1,40);
        var v=Vehicle(id);var doors=v.GetComponent(T("VehicleDoors"));Assert.NotNull(doors);
        var door=((Array)Get(doors,"doors")).GetValue(0);var entry=(Transform)Get(door,"interaction");Call(doors,"Toggle",0);
        yield return new WaitForSeconds(1.3f);
        var local=(Vector3)T("HecticCompanySupervisor").GetMethod("CargoPoint").Invoke(null,new object[]{v,entry,0});
        Vector3 destination=v.transform.TransformPoint(local);Vector3 outward=Vector3.ProjectOnPlane(entry.position-destination,Vector3.up).normalized;
        Vector3 start=entry.position+outward*.5f;start.y=1000.03f;
        var ai=Actor(start);var life=ai.GetComponent(T("SoldierLife"));var movement=ai.GetComponent(T("SoldierMovement"));var look=ai.GetComponent(T("SoldierLook"));var physical=ai.gameObject.AddComponent(T("HecticPhysicalBoarding"));
        var supervisor=Root("Supervisor").AddComponent(T("HecticCompanySupervisor"));((Behaviour)supervisor).enabled=false;
        Set(physical,"infantry",Get(ai,"infantry"));Set(physical,"supervisor",supervisor);Set(physical,"transport",ai);
        var order=Activator.CreateInstance(T("HecticCompanySupervisor").GetNestedType("BoardingOrder"));Set(order,"vehicle",v);Set(order,"physical",true);Set(order,"entry",entry);Set(order,"localDestination",local);Set(order,"expires",Time.time+18);Call(physical,"Begin",order);
        yield return new WaitForSeconds(.25f);
        float peak=ai.transform.position.y;bool jumped=false;
        for(int i=0;i<800 && Get(physical,"Carrier")==null;i++)
        {
            object[] args={Activator.CreateInstance(T("SoldierInputFrame"))};bool supplies=(bool)Call(physical,"TryRead",args);
            if(supplies){var frame=args[0];Call(look,"ApplyLookDelta",Get(frame,"lookDelta"));Call(movement,"SimulateMovement",Get(frame,"move"),Time.fixedDeltaTime,false);if((bool)Get(frame,"jump")){jumped=true;Call(movement,"RequestJump");}}
            peak=Mathf.Max(peak,ai.transform.position.y);yield return new WaitForFixedUpdate();
        }
        Assert.IsTrue(jumped,"AI must use the ordinary jump command, not teleport");
        Assert.Greater(peak,start.y+.3f,"The motor must physically lift the capsule");
        Assert.AreSame(v,Get(physical,"Carrier"),id+" rider must reach cargo floor through the open door");
        Assert.IsFalse((bool)Get(Get(ai,"occupant"),"IsMounted"),"Physical cargo rider must not acquire an invisible seat");
        Assert.AreSame(v.GetComponent<Rigidbody>(),Get(Get(life,"controller"),"Support"));
    }
    [UnityTest] public IEnumerator PickupPhysicalCargoBoarding()=>EnterCargo("Transports/Pickup");
    [UnityTest] public IEnumerator TruckPhysicalCargoBoarding()=>EnterCargo("Transports/TruckTransport");
    [UnityTest] public IEnumerator HelicopterPhysicalCargoBoarding()=>EnterCargo("Transports/TransportHelicopter");
    [UnityTest] public IEnumerator AbandonedReservationExpiresAndSeatCanBeClaimed()
    {
        var v=Vehicle("Transports/Pickup");var a=Actor(new Vector3(5,1000,0));var b=Actor(new Vector3(5,1000,0));
        var first=Get(a,"occupant");var next=Get(b,"occupant");yield return null;
        Assert.IsTrue((bool)Call(v,"Reserve",first,0));Assert.IsFalse((bool)Call(v,"CanBoard",next,0));
        // Advance expiry directly to avoid eight seconds of idle fixture time; production CanBoard does the recovery.
        var seat=((Array)Get(v,"seats")).GetValue(0);Set(seat,"reservedUntil",Time.time-.01f);
        Assert.IsTrue((bool)Call(v,"Reserve",next,0));Assert.AreSame(next,Get(seat,"reservation"));
        Call(v,"ReleaseReservation",next);Assert.IsTrue((bool)Call(v,"CanBoard",first,0));
    }
    [UnityTest] public IEnumerator ObjectiveArrivalUnloadsPassengerAndKeepsDriverAndGunner()
    {
        var v=Vehicle("Combat/PickupTechnical");var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);roots.Add(ground);ground.transform.position=new Vector3(0,999.5f,0);ground.transform.localScale=new Vector3(40,1,40);var chassis=v.GetComponent<Rigidbody>();chassis.isKinematic=false;chassis.constraints=RigidbodyConstraints.FreezeAll;Physics.SyncTransforms();
        var actors=new Component[3];
        for(int i=0;i<3;i++)actors[i]=Actor(new Vector3(0,1000,-3));
        yield return null;
        for(int settle=0;settle<8;settle++)yield return new WaitForFixedUpdate();
        Assert.IsTrue((bool)Get(Get(v,"motor"),"CanDisembark"),"Crew vehicle must be resting on grounded suspension before boarding");
        BuildExitNavigation(ground,actors[0]);
        var seats=(Array)Get(v,"seats");
        for(int i=0;i<3;i++){var bounds=(Bounds)Get(v,"EntryBounds");actors[i].transform.position=v.transform.TransformPoint(bounds.center);Physics.SyncTransforms();Assert.IsTrue((bool)Call(v,"Board",Get(actors[i],"occupant"),i),"Crew fixture must board actual seat "+i);}
        var supervisor=Root("Objective supervisor").AddComponent(T("HecticCompanySupervisor"));((Behaviour)supervisor).enabled=false;
        var point=Root("Nearby objective").AddComponent(T("ConquestCapturePoint"));((Behaviour)point).enabled=false;point.transform.position=v.transform.position+Vector3.forward*10;
        var company=Activator.CreateInstance(T("HecticCompanySupervisor").GetNestedType("Company"));Set(company,"objective",point);
        var mapping=(IDictionary)Get(supervisor,"companyOf");foreach(var actor in actors)mapping.Add(actor,company);
        int passenger=Enumerable.Range(0,3).First(i=>Get(seats.GetValue(i),"role").ToString()=="Passenger");
        for(int i=0;i<3;i++)Assert.AreEqual(i==passenger,(bool)Call(supervisor,"ShouldUnload",actors[i]),"Only infantry passenger should unload at objective");
        Assert.IsTrue((bool)Call(Get(actors[passenger],"occupant"),"Leave",false));
        Assert.IsTrue(((Behaviour)actors[passenger].GetComponent(T("SoldierPhysicsBody"))).enabled,"Dismounted passenger regains physical motor");
        for(int i=0;i<3;i++)if(i!=passenger)Assert.IsTrue((bool)Get(Get(actors[i],"occupant"),"IsMounted"),"Driver and gunner remain crewed");
        foreach(var actor in actors)if((bool)Get(Get(actor,"occupant"),"IsMounted"))Call(Get(actor,"occupant"),"Leave",true);
    }
    [UnityTest] public IEnumerator JetAIClimbsForObstacleAndUsesPhysicalFlightMotor()
    {
        var v=Vehicle("Prototypes/Jet01");var body=v.GetComponent<Rigidbody>();body.isKinematic=false;body.constraints=RigidbodyConstraints.FreezeAll;
        var motor=Get(v,"motor");var gear=(SphereCollider[])Get(motor,"landingGear");
        float floorY=gear.Min(g=>g.transform.TransformPoint(g.center).y-g.radius*Mathf.Max(g.transform.lossyScale.x,g.transform.lossyScale.y,g.transform.lossyScale.z))+.01f;
        var runway=GameObject.CreatePrimitive(PrimitiveType.Cube);roots.Add(runway);runway.transform.position=new Vector3(0,floorY-.5f,0);runway.transform.localScale=new Vector3(80,1,80);Physics.SyncTransforms();
        var ai=Actor(new Vector3(15,floorY+.05f,0));yield return null;
        Call(motor,"Simulate",Activator.CreateInstance(T("VehicleInput")),false);yield return new WaitForFixedUpdate();
        var occupant=Get(ai,"occupant");var entry=(Bounds)Get(v,"EntryBounds");ai.transform.position=v.transform.TransformPoint(entry.center);Physics.SyncTransforms();
        Assert.IsTrue((bool)Call(v,"Board",occupant,0),"AI pilot must board using the real vehicle lifecycle");
        Set(ai,"<Goal>k__BackingField",new Vector3(0,1000,1000));
        Object.Destroy(runway);yield return null;body.constraints=RigidbodyConstraints.None;body.useGravity=true;body.linearVelocity=Vector3.forward*75;
        // Refresh production airspeed/ground sensing before asking AI for its first flight command.
        Call(motor,"Simulate",Activator.CreateInstance(T("VehicleInput")),false);yield return new WaitForFixedUpdate();
        var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);roots.Add(obstacle);obstacle.transform.position=new Vector3(0,1000,120);obstacle.transform.localScale=new Vector3(50,50,5);Physics.SyncTransforms();
        Call(ai,"FlyJet",v,motor);var command=Get(v,"DriverInput");Assert.LessOrEqual((float)Get(command,"viewPitch"),-24.9f,"Obstacle must request a climb");
        float altitude=body.position.y;Quaternion rotation=body.rotation;
        for(int i=0;i<40;i++){Call(ai,"FlyJet",v,motor);command=Call(v,"PrepareDriverInput",Get(v,"DriverInput"),true);Call(motor,"Simulate",command,true);yield return new WaitForFixedUpdate();}
        Assert.Greater(Quaternion.Angle(rotation,body.rotation),3,"AI command must rotate the physical jet");Assert.Greater(body.position.z,20,"Aircraft must move rather than teleport");Assert.IsFalse(float.IsNaN(body.linearVelocity.x));
        Assert.IsTrue((bool)Call(occupant,"Leave",true));
    }
}
#endif
