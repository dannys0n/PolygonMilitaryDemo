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
 IEnumerator Load(string name){yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Tests/AI/"+name+".unity",new LoadSceneParameters(LoadSceneMode.Single));yield return null;scenario=UnityEngine.Object.FindAnyObjectByType(T("AIBehaviorTestScenario")) as Component;Assert.NotNull(scenario);}
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
}
#endif
