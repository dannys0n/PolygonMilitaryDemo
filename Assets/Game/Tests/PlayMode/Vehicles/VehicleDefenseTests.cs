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

// Isolate tests without changing the game's predefined Assembly-CSharp boundary.
public class VehicleDefenseTests
{
    readonly List<GameObject> objects=new List<GameObject>();
    readonly List<Component> vehicles=new List<Component>();
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static Type T(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).First(t=>t!=null);
    static object Get(object o,string name){var p=o.GetType().GetProperty(name,flags);return p!=null?p.GetValue(o):o.GetType().GetField(name,flags).GetValue(o);}
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,flags).SetValue(o,value);
    static void EnumField(object o,string name,string value)=>Set(o,name,Enum.Parse(o.GetType().GetField(name,flags).FieldType,value));
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,flags).Invoke(o,args);
    GameObject Root(string name){var g=new GameObject(name);objects.Add(g);return g;}
    Component Vehicle(string name,int faction,Vector3 position)
    {
        var root=Root(name);root.SetActive(false);root.transform.position=position;
        var body=root.AddComponent<Rigidbody>();body.useGravity=false;
        var v=(Behaviour)root.AddComponent(T("Vehicle"));v.enabled=false;
        var seat=Activator.CreateInstance(T("Vehicle").GetNestedType("Seat"));
        var actorRoot=Root(name+" operator");actorRoot.SetActive(false);
        var actor=actorRoot.AddComponent(T("VehicleOccupant"));Set(actor,"faction",faction);
        seat.GetType().GetField("occupant").SetValue(seat,actor);
        foreach(string n in new[]{"anchor","exit","eye"})seat.GetType().GetField(n).SetValue(seat,root.transform);
        var seats=Array.CreateInstance(seat.GetType(),1);seats.SetValue(seat,0);Set(v,"seats",seats);
        Set(v,"cameraTarget",root.transform);EnumField(v,"instruments","FixedWing");
        root.SetActive(true);((IList)T("Vehicle").GetField("Active").GetValue(null)).Add(v);vehicles.Add(v);return v;
    }
    Component Missile(string seeker,string guidance,out Component target)
    {
        var source=Vehicle("Source",0,new Vector3(0,1000,0));target=Vehicle("Target",1,new Vector3(0,1000,100));
        var root=Root("Seeker");root.transform.position=new Vector3(0,1000,5);
        var body=root.AddComponent<Rigidbody>();body.useGravity=false;body.linearVelocity=Vector3.forward*30;
        var m=root.AddComponent(T("VehicleMissileGuidance"));EnumField(m,"seeker",seeker);EnumField(m,"guidance",guidance);Set(m,"seekerDelay",0f);
        Call(m,"Initialize",source,new Ray(new Vector3(0,1000,0),Vector3.forward),null);
        Assert.AreEqual(target,Get(m,"Target"),"Seeker must acquire the hostile vehicle");return m;
    }
    Component Decoy(string signature)
    {
        var root=Root("Decoy");root.SetActive(false);root.transform.position=new Vector3(0,1000,95);
        var c=root.AddComponent(T("VehicleDecoy"));EnumField(c,"signature",signature);root.SetActive(true);return c;
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        var registry=(IList)T("Vehicle").GetField("Active").GetValue(null);foreach(var v in vehicles)registry.Remove(v);
        foreach(Component d in ((IList)T("VehicleDecoy").GetField("Active").GetValue(null)).Cast<Component>().ToArray())if(d)Object.Destroy(d.gameObject);
        foreach(Component c in ((IList)T("GrenadeSmoke").GetField("clouds",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Cast<Component>().ToArray())if(c)Object.Destroy(c.gameObject);
        foreach(var g in objects)if(g)Object.Destroy(g);objects.Clear();vehicles.Clear();yield return null;
    }
    [UnityTest] public IEnumerator InfraredSeekerFollowsFlare()
    {var m=Missile("Infrared","AirToAir",out _);var d=Decoy("Infrared");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(d,Get(m,"DecoyTarget"));Assert.IsNull(Get(m,"Target"));}
    [UnityTest] public IEnumerator InfraredSeekerIgnoresChaff()
    {var m=Missile("Infrared","AirToAir",out var target);Decoy("Radar");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator RadarSeekerFollowsChaff()
    {var m=Missile("Radar","AirDefence",out _);var d=Decoy("Radar");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(d,Get(m,"DecoyTarget"));}
    [UnityTest] public IEnumerator RadarSeekerIgnoresFlare()
    {var m=Missile("Radar","AirDefence",out var target);Decoy("Infrared");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator OpticalGuidanceIgnoresAircraftDecoys()
    {var m=Missile("Optical","LaserSpot",out var target);Decoy("Infrared");Decoy("Radar");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator SmokeBreaksOpticalGuidance()
    {var m=Missile("Optical","LaserSpot",out _);var cloud=Root("Obscurant");cloud.transform.position=new Vector3(0,1000,60);cloud.AddComponent(T("GrenadeSmoke"));yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.IsNull(Get(m,"Target"));}
    [UnityTest] public IEnumerator DistantFlareDoesNotProvideGlobalImmunity()
    {var m=Missile("Infrared","AirToAir",out var target);var d=Decoy("Infrared");d.transform.position=new Vector3(0,1000,250);yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator CountermeasureCooldownAndRechargeAreBounded()
    {
        var v=Vehicle("Dispenser",0,new Vector3(0,1000,0));var c=v.gameObject.AddComponent(T("VehicleCountermeasures"));
        Set(c,"automaticForAI",false);Set(c,"flares",true);Set(c,"dispensers",new Transform[0]);Set(c,"cooldown",.1f);Set(c,"rechargeSeconds",1f);
        Assert.IsTrue((bool)Call(c,"Deploy"));Assert.IsFalse((bool)Call(c,"Deploy"));yield return new WaitForSeconds(.15f);
        Assert.IsTrue((bool)Call(c,"Deploy"));Assert.AreEqual(0,Get(c,"Charges"));Assert.IsFalse((bool)Call(c,"Deploy"));
        yield return new WaitForSeconds(1.1f);Assert.GreaterOrEqual((int)Get(c,"Charges"),1);Assert.LessOrEqual((int)Get(c,"Charges"),2);
    }
    [UnityTest] public IEnumerator ExpiredDecoysLeaveRegistry()
    {var d=Decoy("Infrared");Set(d,"lifetime",.05f);d.gameObject.SetActive(false);d.gameObject.SetActive(true);yield return new WaitForSeconds(.15f);Assert.IsFalse((bool)d);Assert.AreEqual(0,((IList)T("VehicleDecoy").GetField("Active").GetValue(null)).Count);}
    [UnityTest] public IEnumerator AllVehiclePrefabsStartWithoutRuntimeErrors()
    {
        int count=0;foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Game/Prefabs/Vehicles"}))
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));if(!prefab.GetComponent(T("Vehicle")))continue;
            var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);count++;
            yield return new WaitForFixedUpdate();yield return null;Object.Destroy(root);yield return null;
        }
        Assert.AreEqual(32,count);
    }
    [UnityTest] public IEnumerator JetProducesThrustWithoutHoverLiftAtRest()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;var motor=root.GetComponent(T("JetMotor"));var input=Activator.CreateInstance(T("VehicleInput"));
        yield return null;for(int i=0;i<10;i++){Call(motor,"Simulate",input,true);yield return new WaitForFixedUpdate();}
        Assert.Greater(root.GetComponent<Rigidbody>().linearVelocity.z,.1f);Assert.Greater((float)Get(motor,"EnginePower"),0);Assert.Less(root.GetComponent<Rigidbody>().linearVelocity.y,-.2f);
    }
    [UnityTest] public IEnumerator RearDoorOpensAndClosesItsColliderWithTheMesh()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Transports/Van.prefab");var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;root.GetComponent<Rigidbody>().useGravity=false;
        var doors=root.GetComponent(T("VehicleDoors"));var door=((Array)Get(doors,"doors")).GetValue(0);Set(door,"duration",.1f);var leaf=((Array)Get(door,"leaves")).GetValue(0);var part=(Transform)Get(leaf,"part");Quaternion closed=part.localRotation;
        Assert.IsTrue((bool)Call(doors,"Toggle",0));yield return new WaitForSeconds(.2f);Assert.Greater(Quaternion.Angle(closed,part.localRotation),80);
        Assert.IsNotNull(part.GetComponentInChildren<BoxCollider>());Call(doors,"Toggle",0);yield return new WaitForSeconds(.2f);Assert.Less(Quaternion.Angle(closed,part.localRotation),1);
    }
    [UnityTest] public IEnumerator FlareBehindSolidCoverCannotDistractSeeker()
    {
        var m=Missile("Infrared","AirToAir",out var target);Decoy("Infrared");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(wall);wall.transform.position=new Vector3(0,1000,40);wall.transform.localScale=new Vector3(20,20,2);Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));
    }
    [UnityTest] public IEnumerator PassengerCannotUseCountermeasureControls()
    {
        var v=Vehicle("Crew",0,new Vector3(0,1000,0));var seat=((Array)Get(v,"seats")).GetValue(0);EnumField(seat,"role","Passenger");var actor=(Component)Get(seat,"occupant");Set(actor,"<MountedVehicle>k__BackingField",v);
        var c=v.gameObject.AddComponent(T("VehicleCountermeasures"));Set(c,"automaticForAI",false);Set(c,"flares",true);
        Assert.IsFalse((bool)Call(c,"TryDeploy",actor));Assert.AreEqual(2,Get(c,"Charges"));yield return null;
    }
    [UnityTest] public IEnumerator DestroyedVehicleCannotDispense()
    {
        var v=Vehicle("Destroyed",0,new Vector3(0,1000,0));var c=v.gameObject.AddComponent(T("VehicleCountermeasures"));Set(c,"automaticForAI",false);Set(c,"flares",true);
        Set(v,"<Health>k__BackingField",0f);Assert.IsFalse((bool)Call(c,"Deploy"));Assert.AreEqual(2,Get(c,"Charges"));yield return null;
    }
    [UnityTest] public IEnumerator AIReactsToIncomingMissile()
    {
        Missile("Infrared","AirToAir",out var target);var seat=((Array)Get(target,"seats")).GetValue(0);var actor=(Component)Get(seat,"occupant");
        var soldier=actor.gameObject.AddComponent(T("SoldierController"));Set(actor,"soldier",soldier);Set(actor,"<MountedVehicle>k__BackingField",target);
        var c=target.gameObject.AddComponent(T("VehicleCountermeasures"));Set(c,"flares",true);Set(c,"dispensers",new[]{target.transform});
        Set(c,"flarePrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Effects/Vehicles/Countermeasures/Flare.prefab").GetComponent(T("VehicleDecoy")));
        yield return new WaitForSeconds(.7f);Assert.Less((int)Get(c,"Charges"),2);
    }
    Component Platform(out Component vehicle)
    {
        vehicle=Vehicle("Moving deck",0,new Vector3(0,1000,0));var deck=GameObject.CreatePrimitive(PrimitiveType.Cube);deck.transform.SetParent(vehicle.transform,false);deck.transform.localScale=new Vector3(8,.2f,8);
        var player=Root("Standing controller");player.transform.position=new Vector3(0,1000.12f,0);var cc=player.AddComponent<CharacterController>();cc.height=2;cc.radius=.3f;cc.center=Vector3.up;
        var platform=player.AddComponent(T("SoldierVehiclePlatform"));Physics.SyncTransforms();Call(platform,"EndStep",false);Assert.AreEqual(vehicle,Get(platform,"Support"));return platform;
    }
    [UnityTest] public IEnumerator MovingPlatformCarriesUnparentedCharacter()
    {
        var p=Platform(out var v);v.transform.position+=Vector3.right*2;Physics.SyncTransforms();Call(p,"BeginStep",Vector3.zero,.02f,false);
        Assert.Greater(p.transform.position.x,1.8f);Assert.IsNull(p.transform.parent);yield return null;
    }
    [UnityTest] public IEnumerator JumpAndExcessiveTiltReleasePlatformSupport()
    {
        var p=Platform(out var v);Call(p,"BeginStep",Vector3.zero,.02f,true);Assert.IsNull(Get(p,"Support"));Call(p,"EndStep",false);Assert.AreEqual(v,Get(p,"Support"));
        v.transform.rotation=Quaternion.Euler(0,0,60);Physics.SyncTransforms();Call(p,"BeginStep",Vector3.zero,.02f,false);Assert.IsNull(Get(p,"Support"));yield return null;
    }
    [UnityTest] public IEnumerator BoardingSwitchingAndLeavingRestoreInfantryControl()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Hummer.prefab");var car=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(car);
        var v=(Behaviour)car.GetComponent(T("Vehicle"));v.enabled=false;car.GetComponent<Rigidbody>().useGravity=false;
        var match=(Behaviour)Root("Boarding test match").AddComponent(T("ArenaMatch"));match.enabled=false;
        Set(match,"settings",AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Matches/Vehicle_Test.asset"));
        var holder=Root("Inactive actor setup");holder.SetActive(false);
        var actorPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Matches/Vehicles/Player_Polygon_Vehicles.prefab");var player=Object.Instantiate(actorPrefab,new Vector3(4,1000,0),Quaternion.identity,holder.transform);objects.Add(player);
        Set(player.GetComponent(T("ArenaParticipant")),"match",match);holder.SetActive(true);
        var actor=player.GetComponent(T("VehicleOccupant"));var soldier=player.GetComponent(T("SoldierController"));((Behaviour)Get(soldier,"inputSource")).enabled=false;
        yield return null;var entry=(Bounds)Get(v,"EntryBounds");player.transform.position=v.transform.TransformPoint(entry.center+Vector3.right*(entry.extents.x-.2f));Physics.SyncTransforms();
        Assert.IsTrue((bool)Call(v,"BoardFirstAvailable",actor));Assert.AreEqual(v,Get(actor,"MountedVehicle"));
        Call(actor,"SwitchSeat",1);Assert.AreEqual(1,Get(actor,"SeatIndex"));Assert.IsTrue((bool)Call(actor,"Leave",false));
        Assert.IsFalse((bool)Get(actor,"IsMounted"));Assert.IsTrue(((Behaviour)soldier).enabled);Assert.IsTrue(player.GetComponent<CharacterController>().enabled);yield return null;
    }
}
#endif
