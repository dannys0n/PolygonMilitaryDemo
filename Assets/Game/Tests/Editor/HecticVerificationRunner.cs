#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;

// Re-register callbacks across the Play Mode domain reload so verification has a saved result.
[InitializeOnLoad]
public static class HecticVerificationRunner
{
    const string Directory="C:/Users/2002d/Documents/Codex/2026-09-29/how/work/";
    static TestRunnerApi api;
    static HecticVerificationRunner(){api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());}
    public static string Run()
    {
        if(EditorApplication.isPlaying)return "Leave Play Mode before running verification";
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool("HecticVerificationRunning",true);
        System.IO.File.WriteAllText(Directory+"hectic-tests-progress.txt","STARTED\n");
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.PlayMode,
            groupNames=new[]{"^(HecticPlayModeTests|CombatResourceTests|VehicleDefenseTests.FlaresRecharge|VehicleDefenseTests.SmokeRetains)"}}));
        return "Play Mode verification started";
    }
    sealed class Results:ICallbacks
    {
        public void RunStarted(ITestAdaptor tests){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result)
        {
            if(SessionState.GetBool("HecticVerificationRunning",false)&&!result.Test.IsSuite)
                System.IO.File.AppendAllText(Directory+"hectic-tests-progress.txt",result.Test.FullName+": "+result.ResultState+" "+result.Message+"\n");
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            if(!SessionState.GetBool("HecticVerificationRunning",false))return;
            TestRunnerApi.SaveResultToFile(result,Directory+"hectic-tests-results.xml");
            System.IO.File.AppendAllText(Directory+"hectic-tests-progress.txt","FINISHED "+result.ResultState+" passed="+result.PassCount+" failed="+result.FailCount+"\n");
            SessionState.SetBool("HecticVerificationRunning",false);
        }
    }
}
#endif
