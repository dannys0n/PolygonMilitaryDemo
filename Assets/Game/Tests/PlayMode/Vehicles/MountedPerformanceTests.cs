#if UNITY_EDITOR
using System;using System.Collections;using System.Linq;using System.Reflection;
using NUnit.Framework;using UnityEngine;using UnityEngine.SceneManagement;using UnityEngine.TestTools;
public class MountedPerformanceTests
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
 static Type T(string n)=>UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t=>t.Name==n);
 static object Get(object o,string n)=>o.GetType().GetProperty(n,F)?.GetValue(o)??o.GetType().GetField(n,F)?.GetValue(o);
 static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,F).Invoke(o,args);
 Component control;
 Component[] Actors=>((IEnumerable)Get(Get(control,"match"),"participants")).Cast<Component>().ToArray();
 void Count(int team,int count){var m=control.GetType().GetMethod("SetCount");m.Invoke(control,new[]{Enum.ToObject(m.GetParameters()[0].ParameterType,team),(object)count});}
 IEnumerator Load(int blue=1)
 {
  yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_Vehicles.unity");yield return null;
  control=UnityEngine.Object.FindAnyObjectByType(T("EntityPerformanceControl")) as Component;
  Count(0,1);Count(1,blue);yield return null;yield return new WaitForSeconds(.5f);
 }
 [UnityTest] public IEnumerator CrewIdleAnimationContinuesAndExitRestoresInfantryAndRagdoll()
 {
  yield return Load(0);
  var actor=Actors[0];var occupant=actor.GetComponent(T("VehicleOccupant"));
  var animator=(Animator)Get(Get(Get(actor,"player"),"actions"),"animator");
  Assert.IsTrue(animator.enabled);Assert.IsNotNull(animator.runtimeAnimatorController);
  Assert.AreEqual(AnimatorCullingMode.CullUpdateTransforms,animator.cullingMode);
  Camera.main.transform.SetPositionAndRotation(actor.transform.position+new Vector3(3,2,-4),Quaternion.LookRotation(actor.transform.position+Vector3.up-(actor.transform.position+new Vector3(3,2,-4))));
  yield return new WaitForSeconds(.3f);
  float animationTime=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
  yield return new WaitForSeconds(.4f);
  Assert.Greater(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,animationTime,"Visible seated crew must retain animated idle playback.");
  Assert.IsTrue(actor.GetComponentsInChildren<Rigidbody>(true).All(b=>!b.detectCollisions));
  foreach(string name in new[]{"SoldierFirstPersonCamera","SoldierScopeView","SoldierFirstPersonBody"})Assert.IsFalse(((Behaviour)actor.GetComponent(T(name))).enabled,name+" should be suspended while mounted.");
  Assert.IsTrue((bool)Call(occupant,"Leave",true));yield return null;
  Assert.IsTrue(actor.GetComponent<Rigidbody>().detectCollisions);
  foreach(string name in new[]{"SoldierFirstPersonCamera","SoldierScopeView","SoldierFirstPersonBody"})Assert.IsTrue(((Behaviour)actor.GetComponent(T(name))).enabled,name+" must restore on exit.");
  Assert.IsTrue(((Behaviour)Get(Get(actor,"player"),"controller")).enabled);
  Call(Get(actor,"health"),"ApplyDamage",10000f,null,null);yield return null;
  var ragdoll=actor.GetComponentInChildren(T("SoldierRagdoll"));Assert.IsTrue((bool)Get(ragdoll,"IsRagdoll"));
  yield return new WaitForSeconds(3.7f);
  Assert.IsFalse((bool)Get(Get(actor,"health"),"IsDead"));Assert.IsTrue((bool)Get(occupant,"IsMounted"));
  Assert.IsFalse((bool)Get(ragdoll,"IsRagdoll"));Assert.IsTrue(animator.enabled);
 }
 [UnityTest] public IEnumerator CrewMountedBeforeFirstStartKeepsPhysicalAndAnimationOwnership()
 {
  yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_Vehicles.unity");yield return null;
  control=UnityEngine.Object.FindAnyObjectByType(T("EntityPerformanceControl")) as Component;
  // Spawn immediately, before newly activated actors receive Start. Do not let the
  // match's normal deferred-join update hide the opening-scene initialization case.
  var match=(Behaviour)Get(control,"match");match.enabled=false;
  Count(0,1);
  foreach(var actor in Actors)Assert.IsTrue((bool)Call(control,"TrySpawn",actor));
  var first=Actors[0];var rider=first.GetComponent(T("VehicleOccupant"));
  var life=(Component)Get(first,"player");
  var animator=(Animator)Get(Get(life,"actions"),"animator");
  Camera.main.transform.SetPositionAndRotation(first.transform.position+new Vector3(3,2,-4),
   Quaternion.LookRotation(first.transform.position+Vector3.up-(first.transform.position+new Vector3(3,2,-4))));
  yield return null;yield return new WaitForSeconds(.2f);
  foreach(var actor in Actors)
  {
   var actorLife=Get(actor,"player");
   Assert.IsTrue((bool)Get(actor.GetComponent(T("VehicleOccupant")),"IsMounted"));
   foreach(string component in new[]{"controller","movement","look","actions","loadout"})
    Assert.IsFalse(((Behaviour)Get(actorLife,component)).enabled,component+" must stay suspended after the actor's first Start");
   Assert.IsTrue(actor.GetComponentsInChildren<Rigidbody>(true).All(b=>!b.detectCollisions),"Mounted capsules/bones must not rejoin the vehicle's physics contacts");
   Call(actorLife,"SetGameplayAllowed",true);
   Assert.IsFalse(((Behaviour)Get(actorLife,"controller")).enabled,"Later generic enable calls also respect mounted ownership");
  }
  float idleTime=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
  yield return new WaitForSeconds(.3f);
  Assert.IsTrue(animator.enabled);Assert.Greater(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,idleTime,"The guard must retain visible seated idle playback");
  Assert.IsTrue((bool)Call(rider,"Leave",true));yield return null;
  Assert.IsTrue(((Behaviour)Get(life,"controller")).enabled,"An actual exit restores normal infantry movement");
  Assert.IsTrue(first.GetComponent<Rigidbody>().detectCollisions);
 }
 [UnityTest] public IEnumerator RagdollSkinFollowsPhysicsOutsideAnimationBoundsThenRestoresCulling()
 {
  yield return Load(0);var actor=Actors[0];var occupant=actor.GetComponent(T("VehicleOccupant"));
  Call(occupant,"Leave",true);yield return null;
  var animator=(Animator)Get(Get(Get(actor,"player"),"actions"),"animator");
  // Equipment/appearance may bind new renderers after the actor's Awake.
  var lateSkin=new GameObject("Late bound skin",typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
  lateSkin.transform.SetParent(animator.transform,false);lateSkin.enabled=false;
  var meshes=animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);Assert.IsNotEmpty(meshes);
  var old=meshes.Select(m=>m.updateWhenOffscreen).ToArray();
  Call(Get(actor,"health"),"ApplyDamage",10000f,null,null);yield return null;
  foreach(var mesh in meshes)Assert.IsTrue(mesh.updateWhenOffscreen,"Corpse deformation must update its bounds after Animator ownership ends.");
  Assert.IsFalse(animator.enabled);
  yield return new WaitForSeconds(3.7f);
  for(int i=0;i<meshes.Length;i++)Assert.AreEqual(old[i],meshes[i].updateWhenOffscreen,"Respawn must restore living skin culling.");
 }
 [UnityTest] public IEnumerator LivingBodyRegistersDamageRegionsButNotDormantCorpseShapes()
 {
  yield return Load(0);var actor=Actors[0];Call(actor.GetComponent(T("VehicleOccupant")),"Leave",true);yield return null;
  var doll=actor.GetComponentInChildren(T("SoldierRagdoll"));var bones=(Rigidbody[])Get(doll,"bodies");
  var hitBodies=actor.GetComponentsInChildren(T("SoldierHitRegion"),true).Cast<Component>()
   .SelectMany(r=>r.GetComponents<Collider>()).Where(c=>c.enabled).Select(c=>c.attachedRigidbody).Where(b=>b).Distinct().ToArray();
  Assert.IsNotEmpty(hitBodies);Assert.Less(bones.Count(b=>b.detectCollisions),bones.Length);
  foreach(var b in hitBodies)Assert.IsTrue(b.detectCollisions,"Living head/torso damage queries must retain physics registration.");
  Call(Get(actor,"health"),"ApplyDamage",10000f,null,null);yield return null;
  Assert.IsTrue(bones.All(b=>b.detectCollisions&&!b.isKinematic),"Every authored corpse shape must restore at death.");
 }
 [UnityTest] public IEnumerator SharedContactsDeduplicateHullsAndRefreshAfterCrewDismounts()
 {
  yield return Load();var actor=Actors.First(a=>Get(a,"team").ToString()=="Red");
  var ai=actor.GetComponent(T("ArenaVehicleAI"));var vehicle=(Component)Get(actor.GetComponent(T("VehicleOccupant")),"MountedVehicle");
  var list=(IEnumerable)Call(ai,"Contacts",vehicle);
  Assert.AreEqual(1,list.Cast<object>().Count(),"The three hostile crew members share a single target hull.");
  var shared=vehicle.GetComponent(T("VehicleCombatContacts"));Assert.IsNotNull(shared);
  var red=Actors.Where(a=>Get(a,"team").ToString()=="Red").ToArray();
  Call(red[1].GetComponent(T("ArenaVehicleAI")),"Contacts",vehicle);
  Assert.AreSame(shared,Get(red[1].GetComponent(T("ArenaVehicleAI")),"contacts"));
  var blue=Actors.First(a=>Get(a,"team").ToString()=="Blue");Call(blue.GetComponent(T("VehicleOccupant")),"Leave",true);
  yield return new WaitForSeconds(.25f);
  list=(IEnumerable)Call(shared,"Read",actor);Assert.AreEqual(2,list.Cast<object>().Count(),"A dismounted infantry contact must separate from its former hull.");
 }
 [UnityTest] public IEnumerator OffscreenMountCalibratesSeatingBeforeCulling()
 {
  foreach(string prefab in new[]{"Combat/PickupTechnical","Transports/TransportHelicopter"})
  {
   yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_Vehicles.unity");yield return null;
   control=UnityEngine.Object.FindAnyObjectByType(T("EntityPerformanceControl")) as Component;
   var vehiclePrefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/"+prefab+".prefab").GetComponent(T("Vehicle"));
   control.GetType().GetField("vehiclePrefab").SetValue(control,vehiclePrefab);
   Camera.main.transform.SetPositionAndRotation(new Vector3(0,100,-500),Quaternion.Euler(-80,180,0));
   Count(0,1);yield return null;yield return new WaitForSeconds(.3f);
   foreach(var actor in Actors)
   {
    var animator=(Animator)Get(Get(Get(actor,"player"),"actions"),"animator");
    var rider=actor.GetComponent(T("VehicleOccupant"));var carrier=(Component)Get(rider,"MountedVehicle");
    var seat=((Array)Get(carrier,"seats")).GetValue((int)Get(rider,"SeatIndex"));
    if(!(bool)Get(seat,"showCharacter"))continue;
    var pose=animator.GetComponent(T("VehicleSeatPose"));
    var offset=(Vector3)Get(pose,"bodyOffset");
    Assert.Less(offset.magnitude,.35f,prefab+" must calibrate from the seated sample even before a camera can see the crew.");
    Assert.AreEqual(AnimatorCullingMode.CullUpdateTransforms,animator.cullingMode);
   }
   // Bring the vehicle into view: no stale standing-pose offset may be applied on un-culling.
   var occupant=Actors[0].GetComponent(T("VehicleOccupant"));var vehicle=(Component)Get(occupant,"MountedVehicle");
   Camera.main.transform.SetPositionAndRotation(vehicle.transform.position+new Vector3(8,5,-10),Quaternion.LookRotation(vehicle.transform.position+Vector3.up-(vehicle.transform.position+new Vector3(8,5,-10))));
   yield return new WaitForSeconds(.5f);
   foreach(var actor in Actors)
   {
    var animator=(Animator)Get(Get(Get(actor,"player"),"actions"),"animator");
    var rider=actor.GetComponent(T("VehicleOccupant"));var carrier=(Component)Get(rider,"MountedVehicle");
    var seat=((Array)Get(carrier,"seats")).GetValue((int)Get(rider,"SeatIndex"));var anchor=(Transform)Get(seat,"anchor");
    if(!(bool)Get(seat,"showCharacter"))continue;
    Assert.Less(anchor.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position).magnitude,.4f,prefab+" visible hips must stay at the authored seat, rather than sinking by standing height.");
   }
   Count(0,0);yield return null;
  }
 }
 [UnityTest] public IEnumerator LocalTransportCockpitUsesHeadlessBodyWithoutMovingItsEye()
 {
  yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_Vehicles.unity");yield return null;
  control=UnityEngine.Object.FindAnyObjectByType(T("EntityPerformanceControl")) as Component;
  control.GetType().GetField("vehiclePrefab").SetValue(control,UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Transports/TransportHelicopter.prefab").GetComponent(T("Vehicle")));
  Count(0,1);yield return null;yield return new WaitForSeconds(.3f);
  var actor=Actors[0];var rider=actor.GetComponent(T("VehicleOccupant"));var vehicle=(Component)Get(rider,"MountedVehicle");int index=(int)Get(rider,"SeatIndex");
  Call(rider,"Leave",true);var soldier=(Component)Get(rider,"soldier");
  var input=(Behaviour)actor.gameObject.AddComponent(T("SoldierLocalInput"));input.enabled=false;soldier.GetType().GetField("inputSource").SetValue(soldier,input);
  Call(rider,"Mount",vehicle,index);var seat=((Array)Get(vehicle,"seats")).GetValue(index);seat.GetType().GetField("occupant").SetValue(seat,rider);
  rider.GetType().GetProperty("CockpitView",F).SetValue(rider,true);yield return null;yield return null;
  var camera=(Camera)Get(rider,"ActiveVehicleCamera");var body=(Behaviour)actor.GetComponent(T("SoldierFirstPersonBody"));var eye=(Transform)Get(seat,"eye");
  Assert.IsTrue(body.enabled);Assert.Less(Vector3.Distance(eye.position,camera.transform.position),.001f);
  Call(body,"BeginCamera",camera);Assert.IsTrue((bool)Get(body,"headlessActive"));
  Assert.IsTrue(actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(m=>m.name.StartsWith("First-person body:")&&m.enabled&&!m.forceRenderingOff));
  Call(body,"EndCamera",camera);rider.GetType().GetProperty("CockpitView",F).SetValue(rider,false);yield return null;
  Assert.IsFalse(body.enabled,"TPS must suspend the headless copy");Call(rider,"Leave",true);yield return null;Assert.IsTrue(body.enabled,"Exit restores infantry body handling");
 }
 [UnityTest] public IEnumerator TransportInteriorViewsUseHeadlessBodiesAndAuthoredEyes()
 {
  foreach(string prefab in new[]{"Transports/TransportHelicopter","Transports/TruckTransport","Transports/TravelVan","Transports/SUV","Prototypes/TransportPlane"})
  {
   yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_Vehicles.unity");yield return null;
   control=UnityEngine.Object.FindAnyObjectByType(T("EntityPerformanceControl")) as Component;
   var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/"+prefab+".prefab").GetComponent(T("Vehicle"));
   control.GetType().GetField("vehiclePrefab").SetValue(control,source);Count(0,1);yield return null;yield return new WaitForSeconds(.3f);
   foreach(var actor in Actors)
   {
    var rider=actor.GetComponent(T("VehicleOccupant"));var vehicle=(Component)Get(rider,"MountedVehicle");
    var seat=((Array)Get(vehicle,"seats")).GetValue((int)Get(rider,"SeatIndex"));
    if(!(bool)Get(vehicle,"hasRenderedInterior") || !(bool)Get(seat,"showCharacter") || (bool)Get(seat,"externalView") || (bool)Get(seat,"weaponMountedView"))continue;
    var view=(Component)Get(Get(actor,"player"),"view");var camera=(Camera)Get(view,"playerCamera");
    var body=(Behaviour)actor.GetComponent(T("SoldierFirstPersonBody"));
    Call(view,"RestoreCameraPerspective",true);camera.enabled=true;
    Call(body,"PrepareForVehicleSpectating");yield return null;yield return null;
    Assert.IsTrue(body.enabled,prefab+" observed cockpit must restore headless-body callbacks");
    var eye=(Transform)Get(seat,"eye");Assert.IsNotNull(eye,prefab+" interior seat needs its authored eye");
    object[] pose={true,Vector3.zero,Quaternion.identity};rider.GetType().GetMethod("GetSpectatorCameraPose").Invoke(rider,pose);
    Assert.Less(Vector3.Distance(eye.position,(Vector3)pose[1]),.001f,prefab+" cockpit camera must stay on its eye anchor");
    var animator=(Animator)Get(Get(Get(actor,"player"),"actions"),"animator");
    Assert.Less(Vector3.Distance(eye.position,animator.GetBoneTransform(HumanBodyBones.Head).position),.25f,"The eye must remain near the seated head");
    Call(body,"BeginCamera",camera);Assert.IsTrue((bool)Get(body,"headlessActive"));
    var copies=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(m=>m.name.StartsWith("First-person body:")).ToArray();
    Assert.IsNotEmpty(copies);Assert.IsTrue(copies.Any(m=>m.enabled&&!m.forceRenderingOff),"The headless skin must actually render");
    Call(body,"EndCamera",camera);Assert.IsFalse((bool)Get(body,"headlessActive"));
    var original=animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);Assert.IsTrue(original.Any(m=>m.enabled&&m.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),"TPS must restore the full character");
    camera.enabled=false;yield return null;
    Assert.IsFalse(body.enabled,"Unobserved crew must return to the suspended presentation policy");
   }
   Count(0,0);yield return null;
  }
 }
 [UnityTest] public IEnumerator EmptyTargetSearchesRespectTheSameBudgetAsSuccessfulSearches()
 {
  yield return Load(0);var actor=Actors[0];var ai=actor.GetComponent(T("ArenaVehicleAI"));
  var vehicle=(Component)Get(actor.GetComponent(T("VehicleOccupant")),"MountedVehicle");
  var origin=vehicle.transform.position+Vector3.up*2;
  Assert.IsNull(Call(ai,"FindVehicleTarget",vehicle,origin,100f,true,false));
  var deadline=((float[])Get(ai,"stationScans"))[1];Assert.Greater(deadline,Time.time);
  for(int i=0;i<20;i++)Assert.IsNull(Call(ai,"FindVehicleTarget",vehicle,origin,100f,true,false));
  Assert.AreEqual(deadline,((float[])Get(ai,"stationScans"))[1],"No-contact stations should not scan the roster on every frame.");
 }
 [UnityTearDown]public IEnumerator Cleanup(){if(control){Count(0,0);Count(1,0);}yield return null;LogAssert.NoUnexpectedReceived();}
}
#endif
