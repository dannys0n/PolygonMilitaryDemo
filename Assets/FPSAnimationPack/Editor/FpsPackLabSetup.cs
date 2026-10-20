using UnityEditor;

public static class FpsPackLabSetup
{
    public static void Run()
    {
        FpsPackValidation.Run();
        FpsPackPreview.RenderVerification();
        AssetDatabase.SaveAssets();
    }
}
