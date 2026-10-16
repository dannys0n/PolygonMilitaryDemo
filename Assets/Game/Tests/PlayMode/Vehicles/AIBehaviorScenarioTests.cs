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

public class AIBehaviorScenarioTests
{
 const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
 static Type T(string name)=>UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t=>t.Name==name);
 static object Get(object o,string name){var p=o.GetType().GetProperty(name,Flags);return p!=null?p.GetValue(o):o.GetType().GetField(name,Flags).GetValue(o);}
 static void Command(object o,string name)=>o.GetType().GetMethod(name).Invoke(o,null);
 Component scenario;
 IEnumerator Load(string name){if(scenario){Command(scenario,"ResetScenario");yield return null;}yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Tests/AI/"+name+".unity",new LoadSceneParameters(LoadSceneMode.Single));yield return null;scenario=UnityEngine.Object.FindAnyObjectByType(T("AIBehaviorTestScenario")) as Component;Assert.NotNull(scenario);}
 [UnityTearDown] public IEnumerator Cleanup(){if(scenario)Command(scenario,"ResetScenario");yield return null;}
 [UnityTest] public IEnumerator PlaneCargoActorsExitAndLandThroughTheSceneButtons()
 {
  yield return Load("AI_Plane_Cargo_Exit");Command(scenario,"LoadPlaneCargo");yield return new WaitForSeconds(1.5f);
  Assert.AreEqual(7,((IList)Get(scenario,"Actors")).Count);Assert.AreEqual(0,Get(scenario,"ErrorCount"));
  Command(scenario,"ExitCargo");float until=Time.time+30;
  while(Time.time<until&&(int)Get(scenario,"CompletedCount")<6)yield return null;
  Assert.AreEqual(6,Get(scenario,"CompletedCount"),"All six actual cargo actors must leave and land");
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator FullTransportKeepsCrewWhilePassengersExit()
 {
  yield return Load("AI_Transport_Overflow");yield return new WaitForSeconds(1.5f);Command(scenario,"FillSeats");yield return new WaitForSeconds(1);
  var vehicle=(Component)Get(scenario,"Vehicle");var seats=(Array)Get(vehicle,"seats");
  int occupied=0;foreach(var seat in seats)if((UnityEngine.Object)Get(seat,"occupant"))occupied++;
  Assert.AreEqual(seats.Length,occupied,"Fill button must populate every functional seat");
  Command(scenario,"UnloadPassengers");float unloadUntil=Time.time+22;while(Time.time<unloadUntil&&(bool)Get(scenario,"Unloading"))yield return null;
  foreach(var seat in seats){var role=Get(seat,"role").ToString();Assert.AreEqual(role!="Passenger",(bool)(UnityEngine.Object)Get(seat,"occupant"),"Only driver/gunner remain after passenger unload");}
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator InfantrySceneActuallyDamagesAndKillsOpponents()
 {
  yield return Load("AI_Infantry_Combat");Command(scenario,"StartCombat");yield return new WaitForSeconds(1);
  var actors=(IList)Get(scenario,"Actors");Assert.AreEqual(4,actors.Count);
  bool damaged=false;float until=Time.time+15;
  while(Time.time<until&&!damaged){foreach(var actor in actors){var health=Get(actor,"health");if((float)Get(health,"CurrentHealth")<(float)Get(health,"maxHealth"))damaged=true;}yield return null;}
  Assert.IsTrue(damaged,"Production AI must spot an enemy and inflict real damage");Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator FullGroundCrewsFireAndDamageOpposingVehicles()
 {
  foreach(var name in new[]{"AI_Tank_Combat","AI_Full_Technical_Combat"})
  {
   yield return Load(name);Command(scenario,"StartVehicleCombat");
   float until=Time.time+8;while(Time.time<until&&!(bool)Get(scenario,"CrewReady"))yield return null;
   Assert.IsTrue((bool)Get(scenario,"CrewReady"),name+" crew setup");AssertFullCrew();
   var red=(Component)Get(scenario,"Vehicle");var blue=(Component)Get(scenario,"Opponent");
   until=Time.time+25;
   while(Time.time<until&&(float)Get(red,"Health")>=(float)Get(red,"maxHealth")&&(float)Get(blue,"Health")>=(float)Get(blue,"maxHealth"))yield return null;
   Assert.Greater((int)Get(scenario,"ShotsFired"),0,name+" must fire production weapons");
   Assert.IsTrue((float)Get(red,"Health")<(float)Get(red,"maxHealth")||(float)Get(blue,"Health")<(float)Get(blue,"maxHealth"),name+" must cause actual vehicle damage");
   Assert.AreEqual(0,Get(scenario,"ErrorCount"));
  }
 }
 [UnityTest] public IEnumerator AircraftCrewsUseProductionFlightInputsWithoutRuntimeErrors()
 {
  foreach(var name in new[]{"AI_Jet_Dogfight","AI_Helicopter_Combat"})
  {
   yield return Load(name);Command(scenario,"StartVehicleCombat");
   float until=Time.time+8;while(Time.time<until&&!(bool)Get(scenario,"CrewReady"))yield return null;
   Assert.IsTrue((bool)Get(scenario,"CrewReady"));AssertFullCrew();
   var red=(Component)Get(scenario,"Vehicle");var blue=(Component)Get(scenario,"Opponent");
   Vector3 initial=red.transform.position;yield return new WaitForSeconds(10);
   Assert.Greater(Vector3.Distance(initial,red.transform.position),5,name+" physically flies");
   Assert.Greater((int)Get(scenario,"ShotsFired"),0,name+" must engage with production weapons");
   until=Time.time+20;while(Time.time<until&&(int)Get(scenario,"VehicleDamageHits")==0)yield return null;
   Assert.Greater((int)Get(scenario,"VehicleDamageHits"),0,name+" must hit a moving opponent, not just fire into empty space");
   Assert.IsTrue((bool)Get(red,"IsDestroyed")||red.transform.position.y>5,name+" surviving red aircraft remains airborne");Assert.IsTrue((bool)Get(blue,"IsDestroyed")||blue.transform.position.y>5,name+" surviving blue aircraft remains airborne");
   foreach(var actor in (IList)Get(scenario,"Actors"))Assert.IsTrue(((Behaviour)((Component)actor).GetComponent(T("ArenaVehicleAI"))).enabled,"Use the existing production brain");
   Assert.AreEqual(0,Get(scenario,"ErrorCount"));
  }
 }
  [UnityTest] public IEnumerator TransportSelectorCanSpawnEveryCatalogVehicle()
 {
  yield return Load("AI_Transport_Overflow");var options=(Array)Get(scenario,"VehicleOptions");Assert.Greater(options.Length,20);
  for(int i=0;i<options.Length;i++)
  {
   scenario.GetType().GetMethod("SelectVehicle").Invoke(scenario,new object[]{i});yield return null;
   Assert.NotNull((UnityEngine.Object)Get(scenario,"Vehicle"));Assert.AreEqual(options.GetValue(i),Get(scenario,"vehiclePrefab"));
   Assert.AreEqual(0,((IList)Get(scenario,"Actors")).Count,"Switching vehicle clears the old crew");
   Assert.AreEqual(0,Get(scenario,"ErrorCount"),"Vehicle selector must spawn without runtime errors");
  }
 }
 [UnityTest] public IEnumerator SeatedTransportTripUsesProductionAIAndMoves()
 {
  yield return Load("AI_Transport_Overflow");yield return new WaitForSeconds(1.5f);Command(scenario,"FillSeats");yield return new WaitForSeconds(1);
  var vehicle=(Component)Get(scenario,"Vehicle");var initial=vehicle.transform.position;Command(scenario,"StartTransportTrip");
  float until=Time.time+15;while(Time.time<until&&Vector3.Distance(initial,vehicle.transform.position)<5)yield return null;
  Assert.Greater(Vector3.Distance(initial,vehicle.transform.position),5,"Production driver must move toward the trip destination");
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator DynamicGroupBoardsWaitsForFreedSeatAndExits()
 {
  yield return Load("AI_Transport_Overflow");var options=(Array)Get(scenario,"VehicleOptions");
  int index=Array.FindIndex(options.Cast<Component>().ToArray(),v=>v.name=="PickupTechnical");Assert.GreaterOrEqual(index,0);
  scenario.GetType().GetMethod("SelectVehicle").Invoke(scenario,new object[]{index});yield return new WaitForSeconds(1.5f);
  var vehicle=(Component)Get(scenario,"Vehicle");int seats=((Array)Get(vehicle,"seats")).Length;
  for(int i=0;i<seats+1;i++){Command(scenario,"AddPlayer");yield return new WaitForSeconds(.15f);}
  Command(scenario,"EnterAll");float until=Time.time+25;
  while(Time.time<until&&MountedActors()<seats)yield return null;
  Assert.AreEqual(seats,MountedActors(),"Production approach and reservations must fill every seat");
  Command(scenario,"AddPlayer");yield return new WaitForSeconds(.3f);
  Assert.AreEqual(seats+2,((IList)Get(scenario,"Actors")).Count,"Adding during an active enter order joins the same request");
  scenario.GetType().GetMethod("RemovePlayerAt").Invoke(scenario,new object[]{seats+1});yield return null;
  var actors=(IList)Get(scenario,"Actors");int seatedIndex=Enumerable.Range(0,actors.Count).First(i=>IsMounted(actors[i]));
  scenario.GetType().GetMethod("RemovePlayerAt").Invoke(scenario,new object[]{seatedIndex});yield return null;
  until=Time.time+15;while(Time.time<until&&MountedActors()<seats)yield return null;
  Assert.AreEqual(seats,MountedActors(),"The waiting actor must claim the newly freed seat");
  Command(scenario,"ExitAll");until=Time.time+20;while(Time.time<until&&MountedActors()>0)yield return null;
  Assert.AreEqual(0,MountedActors(),"Exit must include driver and gunner");
  Command(scenario,"EnterAll");yield return new WaitForSeconds(.1f);Command(scenario,"ExitAll");yield return new WaitForSeconds(2);
  Assert.AreEqual(0,MountedActors(),"Reversing an approach cancels entry");
  foreach(var seat in (Array)Get(vehicle,"seats"))Assert.IsFalse((bool)(UnityEngine.Object)Get(seat,"reservation"),"Exit must release all pending reservations");
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 bool IsMounted(object actor)=>(bool)Get(((Component)actor).GetComponent(T("VehicleOccupant")),"IsMounted");
 int MountedActors()=>((IList)Get(scenario,"Actors")).Cast<object>().Count(IsMounted);
 [UnityTest] public IEnumerator OutsidePlayerCanReplaceFriendlyAIOnlyInFullVehicle()
 {
  yield return Load("AI_Transport_Overflow");var options=(Array)Get(scenario,"VehicleOptions");
  int index=Array.FindIndex(options.Cast<Component>().ToArray(),v=>v.name=="PickupTechnical");
  scenario.GetType().GetMethod("SelectVehicle").Invoke(scenario,new object[]{index});yield return new WaitForSeconds(1.5f);
  Command(scenario,"FillSeats");yield return new WaitForSeconds(1);
  var vehicle=(Component)Get(scenario,"Vehicle");var seats=(Array)Get(vehicle,"seats");
  foreach(var actor in (IList)Get(scenario,"Actors"))
  {var component=(Component)actor;component.GetComponent(T("SoldierController")).GetType().GetField("inputSource").SetValue(component.GetComponent(T("SoldierController")),component.GetComponent(T("ArenaAIInput")));}
  Command(scenario,"AddPlayer");yield return new WaitForSeconds(.3f);
  var actors=(IList)Get(scenario,"Actors");var player=(Component)actors[actors.Count-1];MakeLocal(player);
  var occupant=player.GetComponent(T("VehicleOccupant"));var ai=Get(seats.GetValue(0),"occupant");
  var entry=(Bounds)Get(vehicle,"EntryBounds");
  player.transform.position=vehicle.transform.TransformPoint(entry.center+Vector3.right*(entry.extents.x+5));Physics.SyncTransforms();
  Assert.IsFalse((bool)vehicle.GetType().GetMethod("BoardFirstAvailable").Invoke(vehicle,new[]{occupant}),"Outside the entry box cannot eject anyone");
  Assert.AreEqual(ai,Get(seats.GetValue(0),"occupant"));
  player.transform.position=vehicle.transform.TransformPoint(entry.center);Physics.SyncTransforms();
  Assert.AreEqual(0,vehicle.GetType().GetMethod("FirstEntrySeat").Invoke(vehicle,new[]{occupant}),"A full vehicle offers its first AI seat");
  Assert.IsTrue((bool)vehicle.GetType().GetMethod("BoardFirstAvailable").Invoke(vehicle,new[]{occupant}));
  Assert.AreEqual(occupant,Get(seats.GetValue(0),"occupant"));Assert.IsFalse((bool)Get(ai,"IsMounted"),"AI is ejected through the ordinary exit lifecycle");
  // Human occupants must never become displacement candidates.
  for(int i=0;i<seats.Length;i++)
  {
   var seated=(Component)Get(seats.GetValue(i),"occupant");
   if((bool)Get(seated,"IsLocal"))continue;
   seated.GetType().GetMethod("Leave").Invoke(seated,new object[]{true});MakeLocal(seated);
   seated.transform.position=vehicle.transform.TransformPoint(entry.center);
   Assert.IsTrue((bool)vehicle.GetType().GetMethod("Board").Invoke(vehicle,new object[]{seated,i}));
  }
  MakeLocal((Component)ai);
  Assert.AreEqual(-1,vehicle.GetType().GetMethod("FirstEntrySeat").Invoke(vehicle,new[]{ai}));
  Assert.IsFalse((bool)vehicle.GetType().GetMethod("BoardFirstAvailable").Invoke(vehicle,new[]{ai}));
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 void MakeLocal(Component actor)
 {
  var local=(Behaviour)actor.GetComponent(T("SoldierLocalInput"));if(!local)local=(Behaviour)actor.gameObject.AddComponent(T("SoldierLocalInput"));local.enabled=false;
  var controller=actor.GetComponent(T("SoldierController"));controller.GetType().GetField("inputSource").SetValue(controller,local);
 }
 [UnityTest] public IEnumerator CrowdedSameSideBoardingAvoidsPhysicalPushing()
 {
  yield return Load("AI_Transport_Overflow");var options=(Array)Get(scenario,"VehicleOptions");
  int index=Array.FindIndex(options.Cast<Component>().ToArray(),v=>v.name=="PickupTechnical");scenario.GetType().GetMethod("SelectVehicle").Invoke(scenario,new object[]{index});yield return new WaitForSeconds(1.5f);
  var vehicle=(Component)Get(scenario,"Vehicle");int seats=((Array)Get(vehicle,"seats")).Length;
  for(int i=0;i<seats+1;i++){Command(scenario,"AddPlayer");yield return new WaitForSeconds(.15f);}
  var actors=((IList)Get(scenario,"Actors")).Cast<Component>().ToArray();var bounds=(Bounds)Get(vehicle,"EntryBounds");
  for(int i=0;i<actors.Length;i++)
  {
   var point=vehicle.transform.TransformPoint(new Vector3(bounds.max.x+2,.1f,bounds.center.z+(i-(actors.Length-1)*.5f)*1.2f));
   actors[i].GetType().GetMethod("Respawn",new[]{typeof(Vector3),typeof(Quaternion)}).Invoke(actors[i],new object[]{point,Quaternion.Euler(0,-90,0)});
   var agent=actors[i].GetComponent<UnityEngine.AI.NavMeshAgent>();Assert.GreaterOrEqual(agent.radius,.5f,"Avoidance includes clearance beyond the physical capsule");
  }
  Command(scenario,"EnterAll");float until=Time.time+20;float deepest=0;
  while(Time.time<until&&MountedActors()<seats)
  {
   for(int i=0;i<actors.Length;i++)for(int j=i+1;j<actors.Length;j++)
   {
    if(IsMounted(actors[i])||IsMounted(actors[j]))continue;
    var a=(CapsuleCollider)Get(actors[i].GetComponent(T("SoldierPhysicsBody")),"capsule");var b=(CapsuleCollider)Get(actors[j].GetComponent(T("SoldierPhysicsBody")),"capsule");
    if(Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float depth))deepest=Mathf.Max(deepest,depth);
   }
   yield return null;
  }
  Assert.AreEqual(seats,MountedActors(),"Approaching agents must fill the vehicle without a doorway deadlock");Assert.Less(deepest,.1f,"Agents must not force their physical capsules deeply through one another");Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator GroupCommandsOverwriteStateAndNewPlayersInheritLatestOrder()
 {
  yield return Load("AI_Transport_Overflow");var options=(Array)Get(scenario,"VehicleOptions");
  int index=Array.FindIndex(options.Cast<Component>().ToArray(),v=>v.name=="PickupTechnical");scenario.GetType().GetMethod("SelectVehicle").Invoke(scenario,new object[]{index});yield return new WaitForSeconds(1.5f);
  Command(scenario,"EnterAll");Command(scenario,"EnterAll");
  Command(scenario,"AddPlayer");Command(scenario,"RemovePlayer"); // Remove before initialization resumes.
  yield return null;Assert.AreEqual(0,((IList)Get(scenario,"Actors")).Count);
  Command(scenario,"AddPlayer");Command(scenario,"AddPlayer");
  yield return new WaitForSeconds(.2f);
  foreach(Component actor in (IList)Get(scenario,"Actors"))
   Assert.AreEqual(true,Get(actor.GetComponent(T("ArenaVehicleAI")),"enterOrderedVehicle"),"New actors inherit the existing enter order");
  for(int i=0;i<10;i++){Command(scenario,"EnterAll");yield return new WaitForSeconds(.1f);}
  float until=Time.time+15;while(Time.time<until&&MountedActors()<2)yield return null;
  Assert.AreEqual(2,MountedActors(),"Repeated Enter All must preserve progress and keep seated players inside");
  Command(scenario,"ExitAll");Command(scenario,"ExitAll");Command(scenario,"AddPlayer");
  until=Time.time+15;while(Time.time<until&&MountedActors()>0)yield return null;
  yield return new WaitForSeconds(1);
  Assert.AreEqual(3,((IList)Get(scenario,"Actors")).Count);Assert.AreEqual(0,MountedActors());
  foreach(Component actor in (IList)Get(scenario,"Actors"))
   Assert.AreEqual(false,Get(actor.GetComponent(T("ArenaVehicleAI")),"enterOrderedVehicle"),"New actors inherit Exit All");
  Command(scenario,"AddPlayer");Command(scenario,"EnterAll");Command(scenario,"ExitAll");yield return new WaitForSeconds(.3f);
  foreach(Component actor in (IList)Get(scenario,"Actors"))
   Assert.AreEqual(false,Get(actor.GetComponent(T("ArenaVehicleAI")),"enterOrderedVehicle"),"Spawning actors use the last command, not a stale snapshot");
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator OutsideEntryMarkersDoNotStrandBoardingAI()
 {
  yield return Load("AI_Transport_Overflow");var options=(Array)Get(scenario,"VehicleOptions");int index=Array.FindIndex(options.Cast<Component>().ToArray(),v=>v.name=="PickupTechnical");
  scenario.GetType().GetMethod("SelectVehicle").Invoke(scenario,new object[]{index});yield return new WaitForSeconds(1.5f);
  var vehicle=(Component)Get(scenario,"Vehicle");var exits=((Array)Get(vehicle,"seats")).Cast<object>().Select(s=>(Transform)Get(s,"exit")).ToArray();var positions=exits.Select(e=>e.localPosition).ToArray();
  try
  {
   foreach(var exit in exits)exit.position+=vehicle.transform.right*20;
   Command(scenario,"AddPlayer");yield return new WaitForSeconds(.2f);Command(scenario,"EnterAll");float until=Time.time+15;
   while(Time.time<until&&MountedActors()<1)yield return null;
   Assert.AreEqual(1,MountedActors(),"An exit marker outside the entry volume must not become an unreachable boarding destination");Assert.AreEqual(0,Get(scenario,"ErrorCount"));
  }
  finally{for(int i=0;i<exits.Length;i++)if(exits[i])exits[i].localPosition=positions[i];}
 }
 [UnityTest] public IEnumerator FullTruckExcessAIWaitForSeatsAndEveryoneExits()
 {
  yield return Load("AI_Transport_Overflow");yield return new WaitForSeconds(1.5f);var vehicle=(Component)Get(scenario,"Vehicle");int seats=((Array)Get(vehicle,"seats")).Length;
  Command(scenario,"EnterAll");
  for(int i=0;i<seats+2;i++){Command(scenario,"AddPlayer");yield return new WaitForSeconds(.12f);}
  for(int cycle=0;cycle<2;cycle++)
  {
   Command(scenario,"EnterAll");
   float until=Time.time+40;while(Time.time<until&&MountedActors()<seats)yield return null;
   Assert.AreEqual(seats,MountedActors(),"All functional seats fill through normal AI approaches");
   yield return new WaitForSeconds(2);
   Assert.AreEqual(2,((IList)Get(scenario,"Actors")).Count-MountedActors(),"Excess AI wait outside; they must not physically board cargo");
   Command(scenario,"ExitAll");until=Time.time+25;
   while(Time.time<until&&(MountedActors()>0||((IList)Get(scenario,"Actors")).Cast<Component>().Any(a=>(string)Get(a.GetComponent(T("ArenaVehicleAI")),"BoardingStatus")!="Outside")))yield return null;
   Assert.AreEqual(0,MountedActors());
   var footprint=(Bounds)Get(vehicle,"NavigationBounds");
   foreach(Component actor in (IList)Get(scenario,"Actors"))
   {
    Assert.IsInstanceOf(T("ArenaAIInput"),Get(actor.GetComponent(T("SoldierController")),"inputSource"),"Exit must use production navigation, not straight-line test movement");
    Assert.AreEqual("Outside",Get(actor.GetComponent(T("ArenaVehicleAI")),"BoardingStatus"));
    Assert.IsFalse(footprint.Contains(vehicle.transform.InverseTransformPoint(actor.transform.position)),"No actor remains inside the canopy or vehicle footprint");
    var support=(Rigidbody)Get(actor.GetComponent(T("SoldierPhysicsBody")),"Support");
    Assert.IsFalse(support&&support.GetComponent(T("SoldierPhysicsBody")),"No actor finishes the exit standing on a teammate");
   }
   Assert.AreEqual(0,Get(scenario,"ErrorCount"));
  }
 }
 IEnumerator CombatReady(string scene)
 {
  yield return Load(scene);Command(scenario,"StartVehicleCombat");
  float until=Time.time+10;while(Time.time<until&&!(bool)Get(scenario,"CrewReady"))yield return null;
  Assert.IsTrue((bool)Get(scenario,"CrewReady"));
 }
 [UnityTest] public IEnumerator DogfightBreaksTurningStalemateAndReengages()
 {
  yield return CombatReady("AI_Jet_Dogfight");
  var red=(Component)Get(scenario,"Vehicle");var blue=(Component)Get(scenario,"Opponent");
  // Keep both alive long enough to evaluate the maneuver, without modifying their motors/AI.
  foreach(var v in new[]{red,blue})v.GetType().GetField("DamageFilter").SetValue(v,new Func<GameObject,bool>(_=>false));
  Command(scenario,"SetAircraftClosePass");
  var brains=new[]{red,blue}.Select(v=>((Component)Get(v,"Driver")).GetComponent(T("ArenaVehicleAI"))).ToArray();
  bool extended=false,reengaged=false;float until=Time.time+45;
  while(Time.time<until)
  {
   extended|=brains.Any(a=>Get(a,"Maneuver").ToString()=="Extend");
   reengaged|=brains.Any(a=>(int)Get(a,"ExtensionCount")>0&&Get(a,"Maneuver").ToString()=="Pursuit");
   if(extended&&reengaged)break;yield return null;
  }
  Assert.IsTrue(extended,"Jets must break off rather than endlessly chase each other's tails.");
  Assert.IsTrue(reengaged,"A break-off must be followed by another approach, not permanent fleeing.");
  Assert.Greater(red.transform.position.y,10);Assert.Greater(blue.transform.position.y,10);
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator HelicoptersBrakeAndKeepWeaponStandOff()
 {
  yield return CombatReady("AI_Helicopter_Combat");
  var red=(Component)Get(scenario,"Vehicle");var blue=(Component)Get(scenario,"Opponent");
  foreach(var v in new[]{red,blue})v.GetType().GetField("DamageFilter").SetValue(v,new Func<GameObject,bool>(_=>false));
  Command(scenario,"SetAircraftClosePass");float minimum=1000,until=Time.time+25;
  while(Time.time<until)
  {minimum=Mathf.Min(minimum,Vector3.Distance(red.transform.position,blue.transform.position));yield return null;}
  Assert.Greater(minimum,22,"Closing attack helicopters must not fly into each other.");
  Assert.Greater(Vector3.Distance(red.transform.position,blue.transform.position),40,"Pilots must recover a usable weapon stand-off.");
  Assert.Greater((int)Get(scenario,"ShotsFired"),0,"Separation must retain actual weapon engagement.");
  Assert.Greater(red.transform.position.y,8);Assert.Greater(blue.transform.position.y,8);
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator VehicleHitAlertsCrewAndPersistsAfterDismount()
 {
  yield return CombatReady("AI_Full_Technical_Combat");
  var vehicle=(Component)Get(scenario,"Vehicle");var enemy=(Component)Get(scenario,"Opponent");
  var attacker=(Component)Get(enemy,"Driver");
  var seats=(Array)Get(vehicle,"seats");var passenger=seats.Cast<object>().First(s=>Get(s,"role").ToString()=="Passenger");
  var rider=(Component)Get(passenger,"occupant");var ai=rider.GetComponent(T("ArenaAIInput"));
  vehicle.GetType().GetMethod("ApplyDamage").Invoke(vehicle,new object[]{1f,attacker.gameObject});
  Assert.IsTrue((bool)Get(ai,"IsCombatAlert"),"A vehicle hit must notify seated passengers, not just the driver.");
  Assert.AreEqual(attacker.GetComponent(T("ArenaParticipant")),Get(ai,"Target"));
  rider.GetType().GetMethod("Leave").Invoke(rider,new object[]{true});yield return null;
  Assert.IsTrue((bool)Get(ai,"IsCombatAlert"),"Combat evidence must survive leaving a vehicle.");
  Assert.AreEqual(attacker.GetComponent(T("ArenaParticipant")),Get(ai,"Target"));
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator VehicleDetectionDoesNotUseWeaponMaximumRangeOrKeepStaleDistantTargets()
 {
  yield return CombatReady("AI_Tank_Combat");
  var vehicle=(Component)Get(scenario,"Vehicle");var enemy=(Component)Get(scenario,"Opponent");
  var driver=(Component)Get(vehicle,"Driver");var ai=driver.GetComponent(T("ArenaVehicleAI"));
  var target=((Component)Get(enemy,"Driver")).GetComponent(T("ArenaParticipant"));
  ai.GetType().GetField("vehicleTarget",Flags).SetValue(ai,target);
  ai.GetType().GetField("nextTargetDecision",Flags).SetValue(ai,Time.time+10);
  var body=enemy.GetComponent<Rigidbody>();body.position=vehicle.transform.position+Vector3.forward*350;
  enemy.transform.position=body.position;Physics.SyncTransforms();
  var found=ai.GetType().GetMethod("FindVehicleTarget",Flags).Invoke(ai,new object[]{vehicle,vehicle.transform.position+Vector3.up*2,1000f,true,false});
  Assert.IsNull(found,"A 1000m weapon must not detect/retain a target 350m away.");
  Assert.AreEqual(0,Get(scenario,"ErrorCount"));
 }
 [UnityTest] public IEnumerator AircraftEngageGroundArmorWithoutAnAirOpponent()
 {
  foreach(var scene in new[]{"AI_Jet_Dogfight","AI_Helicopter_Combat"})
  {
   yield return Load(scene);Command(scenario,"StartAirGroundCombat");
   float until=Time.time+10;while(Time.time<until&&!(bool)Get(scenario,"CrewReady"))yield return null;
   Assert.IsTrue((bool)Get(scenario,"CrewReady"));
   var aircraft=(Component)Get(scenario,"Vehicle");
   aircraft.GetType().GetField("DamageFilter").SetValue(aircraft,new Func<GameObject,bool>(_=>false));
   until=Time.time+45;
   while(Time.time<until&&(int)Get(scenario,"AircraftGroundHits")==0)yield return null;
   Assert.Greater((int)Get(scenario,"AircraftGroundHits"),0,scene+" must cause real air-to-ground weapon damage");
   Assert.Greater(aircraft.transform.position.y,8,scene+" must attack without flying into the ground");
   Assert.AreEqual(0,Get(scenario,"ErrorCount"));
  }
 }
 [UnityTest] public IEnumerator AircraftSelectLoadedGroundExplosivesAndRejectAirOnlyMissiles()
 {
  foreach(var scene in new[]{"AI_Jet_Dogfight","AI_Helicopter_Combat"})
  {
   yield return Load(scene);Command(scenario,"StartAirGroundCombat");
   float until=Time.time+10;while(Time.time<until&&!(bool)Get(scenario,"CrewReady"))yield return null;
   Assert.IsTrue((bool)Get(scenario,"CrewReady"));
   var vehicle=(Component)Get(scenario,"Vehicle");var opponent=(Component)Get(scenario,"Opponent");
   var driver=(Component)Get(vehicle,"Driver");var ai=driver.GetComponent(T("ArenaVehicleAI"));var systems=vehicle.GetComponent(T("VehicleWeaponSystems"));
   var target=((Component)Get(opponent,"Driver")).GetComponent(T("ArenaParticipant"));
   var weapons=(Array)Get(systems,"weapons");var profiles=weapons.Cast<object>().Where(w=>Get(w,"role").ToString()=="Driver").ToArray();
   var typed=Array.CreateInstance(weapons.GetType().GetElementType(),profiles.Length);for(int i=0;i<profiles.Length;i++)typed.SetValue(profiles[i],i);
   var choose=ai.GetType().GetMethod("ChooseAircraftWeapon",Flags);
   int selected=(int)choose.Invoke(ai,new object[]{vehicle,systems,typed,target,vehicle.transform.position});
   Assert.GreaterOrEqual(selected,0);var weapon=profiles[selected];
   var launcher=Get(weapon,"launcher") as Component;
   if(launcher)
   {
    var rocket=(Component)Get(launcher,"projectile");var guidance=rocket.GetComponent(T("VehicleMissileGuidance"));
    if(guidance)Assert.That(Get(guidance,"guidance").ToString(),Is.Not.EqualTo("AirToAir").And.Not.EqualTo("AirDefence"));
   }
   else Assert.IsTrue((bool)Get(weapon,"explosive"),"An explosive cannon is the valid ground fallback for an air-to-air-only missile loadout");
   var counts=profiles.Select(w=>Get(w,"launcher") as Component).Where(l=>l).Distinct().Select(l=>new{launcher=l,ammo=(int)Get(l,"missiles")}).ToArray();
   foreach(var saved in counts)saved.launcher.GetType().GetField("missiles",Flags).SetValue(saved.launcher,0);
   int fallback=(int)choose.Invoke(ai,new object[]{vehicle,systems,typed,target,vehicle.transform.position});
   Assert.IsTrue(fallback<0||Get(profiles[fallback],"system").ToString()=="Gun","An empty launcher must not prevent another station/weapon from fighting");
   foreach(var saved in counts)saved.launcher.GetType().GetField("missiles",Flags).SetValue(saved.launcher,saved.ammo);
  }
 }
 void AssertFullCrew()
 {
  int total=0;
  foreach(string side in new[]{"Vehicle","Opponent"})
  {
   var vehicle=Get(scenario,side);foreach(var seat in (Array)Get(vehicle,"seats"))
   {var occupant=Get(seat,"occupant");Assert.IsTrue((bool)(UnityEngine.Object)occupant,"Every functional seat is occupied");var actor=((Component)occupant).GetComponent(T("ArenaParticipant"));Assert.AreEqual(side=="Vehicle"?"Red":"Blue",Get(actor,"team").ToString());total++;}
  }
  Assert.AreEqual(total,((IList)Get(scenario,"Actors")).Count);
 }
}
#endif
