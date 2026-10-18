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
public class VehicleCoreRegressionTests
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
    [UnityTest] public IEnumerator InfantryBodyHitSurvivesPhysicsLayerSetup()
    {
        var shooter=Actor(new Vector3(0,1000,0));var target=Actor(new Vector3(0,1000,10));
        yield return null;
        var hitscan=shooter.GetComponent(T("SoldierHitscan"));
        var region=target.GetComponentsInChildren(T("SoldierHitRegion")).First(r=>!(bool)Get(r,"head"));
        var collider=region.GetComponent<Collider>();
        var camera=(Camera)Get(hitscan,"aimCamera");
        camera.transform.position=shooter.transform.position+Vector3.up*1.4f;
        camera.transform.rotation=Quaternion.LookRotation(collider.bounds.center-camera.transform.position);
        Physics.SyncTransforms();
        Assert.AreNotEqual(0,((LayerMask)Get(hitscan,"hitLayers")).value&(1<<region.gameObject.layer),"Physics initialization must preserve a hittable damage layer");
        var loadout=shooter.GetComponent(T("SoldierWeaponLoadout"));
        var weapon=Get(loadout,"EquippedWeapon");
        var definition=Object.Instantiate((ScriptableObject)Get(weapon,"definition"));
        try
        {
            Set(definition,"damage",25f);Set(definition,"spreadDegrees",0f);Set(definition,"rocketProjectile",null);Set(definition,"thrownExplosive",null);
            var muzzle=Root("Damage test muzzle").transform;muzzle.position=camera.transform.position;
            var health=target.GetComponent(T("SoldierHealth"));float before=(float)Get(health,"CurrentHealth");
            var result=Call(hitscan,"Fire",definition,muzzle);
            Assert.IsTrue((bool)Get(result,"damaged"),"Production hitscan must damage the real infantry body region");
            Assert.AreEqual(before-25,(float)Get(health,"CurrentHealth"));
        }
        finally{Object.Destroy(definition);}
    }
    [UnityTearDown] public IEnumerator Cleanup(){foreach(var g in roots)if(g){var occupant=g.GetComponent(T("VehicleOccupant"));if(occupant&&(bool)Get(occupant,"IsMounted"))Call(occupant,"Leave",true);}foreach(var g in roots)if(g)Object.Destroy(g);foreach(var instance in navigation)instance.Remove();foreach(var data in navigationData)Object.Destroy(data);navigation.Clear();navigationData.Clear();roots.Clear();match=null;yield return null;}
    [UnityTest] public IEnumerator NearbyVehicleBehindIsVisibleButInfantryStillUsesItsCone()
    {
        var observer=Actor(new Vector3(0,1000,0));var enemy=Actor(new Vector3(0,1000,-12));yield return null;
        var input=observer.GetComponent(T("ArenaAIInput"));var target=enemy.GetComponent(T("ArenaParticipant"));
        observer.transform.rotation=Quaternion.identity;
        Assert.IsFalse((bool)Call(input,"CanSee",target),"On-foot infantry behind the observer stays outside its cone");
        var vehicle=Vehicle("Combat/PickupTechnical");vehicle.transform.position=new Vector3(0,1000,-12);
        var occupant=Get(enemy,"occupant");var seats=(Array)Get(vehicle,"seats");Set(seats.GetValue(0),"occupant",occupant);Call(occupant,"Mount",vehicle,0);Physics.SyncTransforms();
        Assert.IsTrue((bool)Call(input,"CanSee",target),"Nearby occupied vehicles are detected in a full circle");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);roots.Add(wall);wall.transform.position=new Vector3(0,1001,-5);wall.transform.localScale=new Vector3(8,6,.5f);Physics.SyncTransforms();
        Assert.IsFalse((bool)Call(input,"CanSee",target),"Full-circle awareness must still respect occlusion");
        Call(occupant,"Leave",true);
    }
    void BuildExitNavigation(GameObject floor,Component actor)
    {
        var agent=actor.GetComponent<UnityEngine.AI.NavMeshAgent>();
        var settings=UnityEngine.AI.NavMesh.GetSettingsByID(agent.agentTypeID);
        var source=new UnityEngine.AI.NavMeshBuildSource{shape=UnityEngine.AI.NavMeshBuildSourceShape.Box,transform=floor.transform.localToWorldMatrix,size=Vector3.one,area=0};
        var data=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,new List<UnityEngine.AI.NavMeshBuildSource>{source},new Bounds(new Vector3(0,1000,0),new Vector3(50,10,50)),Vector3.zero,Quaternion.identity);
        Assert.NotNull(data,"Build a navigable infantry floor for legitimate AI dismounts");
        navigationData.Add(data);navigation.Add(UnityEngine.AI.NavMesh.AddNavMeshData(data));
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
        // Ground sensing is cached. Let the removed runway expire from that cache
        // before asserting obstacle avoidance rather than the takeoff command.
        yield return new WaitForSeconds(.2f);
        Call(motor,"Simulate",Activator.CreateInstance(T("VehicleInput")),false);
        var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);roots.Add(obstacle);obstacle.transform.position=new Vector3(0,1000,120);obstacle.transform.localScale=new Vector3(50,50,5);Physics.SyncTransforms();
        Call(ai,"FlyJet",v,motor);var command=Get(v,"DriverInput");Assert.LessOrEqual((float)Get(command,"viewPitch"),-24.9f,"Obstacle must request a climb");
        float altitude=body.position.y;Quaternion rotation=body.rotation;
        for(int i=0;i<40;i++){Call(ai,"FlyJet",v,motor);command=Call(v,"PrepareDriverInput",Get(v,"DriverInput"),true);Call(motor,"Simulate",command,true);yield return new WaitForFixedUpdate();}
        Assert.Greater(Quaternion.Angle(rotation,body.rotation),3,"AI command must rotate the physical jet");Assert.Greater(body.position.z,20,"Aircraft must move rather than teleport");Assert.IsFalse(float.IsNaN(body.linearVelocity.x));
        Assert.IsTrue((bool)Call(occupant,"Leave",true));
    }
}
#endif
