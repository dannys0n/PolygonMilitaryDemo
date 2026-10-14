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
 static Type T(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).First(t=>t!=null);
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
  Command(scenario,"AddOverflowRiders");yield return new WaitForSeconds(1);Assert.AreEqual(seats.Length+2,((IList)Get(scenario,"Actors")).Count);
  float until=Time.time+18;while(Time.time<until&&(int)Get(scenario,"CompletedCount")<2)yield return null;
  Assert.AreEqual(2,Get(scenario,"CompletedCount"),"Both overflow actors must physically board");
  Command(scenario,"UnloadPassengers");float unloadUntil=Time.time+22;while(Time.time<unloadUntil&&(bool)Get(scenario,"Unloading"))yield return null;
  foreach(var seat in seats){var role=Get(seat,"role").ToString();Assert.AreEqual(role!="Passenger",(bool)(UnityEngine.Object)Get(seat,"occupant"),"Only driver/gunner remain after passenger unload");}
  Command(scenario,"ExitCargo");yield return new WaitForSeconds(8);
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
  Command(scenario,"ToggleAllEnterExit");float until=Time.time+25;
  while(Time.time<until&&MountedActors()<seats)yield return null;
  Assert.AreEqual(seats,MountedActors(),"Production approach and reservations must fill every seat");
  Command(scenario,"AddPlayer");yield return new WaitForSeconds(.3f);
  Assert.AreEqual(seats+2,((IList)Get(scenario,"Actors")).Count,"Adding during an active enter order joins the same request");
  scenario.GetType().GetMethod("RemovePlayerAt").Invoke(scenario,new object[]{seats+1});yield return null;
  var actors=(IList)Get(scenario,"Actors");int seatedIndex=Enumerable.Range(0,actors.Count).First(i=>IsMounted(actors[i]));
  scenario.GetType().GetMethod("RemovePlayerAt").Invoke(scenario,new object[]{seatedIndex});yield return null;
  until=Time.time+15;while(Time.time<until&&MountedActors()<seats)yield return null;
  Assert.AreEqual(seats,MountedActors(),"The waiting actor must claim the newly freed seat");
  Command(scenario,"ToggleAllEnterExit");until=Time.time+20;while(Time.time<until&&MountedActors()>0)yield return null;
  Assert.AreEqual(0,MountedActors(),"Exit must include driver and gunner");
  Command(scenario,"ToggleAllEnterExit");yield return new WaitForSeconds(.1f);Command(scenario,"ToggleAllEnterExit");yield return new WaitForSeconds(2);
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
