#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;
[InitializeOnLoad]
public static class AIBehaviorVerificationRunner
{
 static TestRunnerApi api;
 const string Progress="C:/Users/2002d/Documents/Codex/2026-09-29/how/work/behavior-tests.txt";
 static AIBehaviorVerificationRunner(){api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());}
 public static string Run(string groupPattern=null){if(EditorApplication.isPlaying)return "Leave Play Mode first";SessionState.SetBool("AIBehaviorVerification",true);System.IO.File.WriteAllText(Progress,"START\n");api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.PlayMode,groupNames=new[]{groupPattern??"^(AIBehaviorScenarioTests|VehicleCoreRegressionTests|CombatResourceTests)"}}));return "Running saved-scene behavior and core regression tests";}
 sealed class Results:ICallbacks
 {
  public void RunStarted(ITestAdaptor t){}public void TestStarted(ITestAdaptor t){}
  public void TestFinished(ITestResultAdaptor r){if(SessionState.GetBool("AIBehaviorVerification",false)&&!r.Test.IsSuite)System.IO.File.AppendAllText(Progress,r.Test.FullName+": "+r.ResultState+" "+r.Message+"\n");}
  public void RunFinished(ITestResultAdaptor r){if(!SessionState.GetBool("AIBehaviorVerification",false))return;TestRunnerApi.SaveResultToFile(r,"C:/Users/2002d/Documents/Codex/2026-09-29/how/work/behavior-tests.xml");System.IO.File.AppendAllText(Progress,"DONE passed="+r.PassCount+" failed="+r.FailCount+"\n");SessionState.SetBool("AIBehaviorVerification",false);}
 }
}
#endif
