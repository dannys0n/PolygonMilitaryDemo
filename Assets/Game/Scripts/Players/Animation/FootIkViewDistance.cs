using UnityEngine;

// Share a small camera snapshot instead of searching for the viewer for every soldier.
public static class FootIkViewDistance
{
    static Camera[] cameras=new Camera[8];
    static int count;
    static float refreshAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset(){count=0;refreshAt=float.NegativeInfinity;System.Array.Clear(cameras,0,cameras.Length);}

    public static float Weight(Vector3 position,float fullDistance,float fadeDistance)
    {
        if(Time.unscaledTime>=refreshAt)
        {
            int required=Camera.allCamerasCount;
            if(required>cameras.Length)System.Array.Resize(ref cameras,Mathf.NextPowerOfTwo(required));
            count=Camera.GetAllCameras(cameras);
            refreshAt=Time.unscaledTime+.25f;
        }
        float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var camera=cameras[i];
            if(!camera || !camera.isActiveAndEnabled || camera.cameraType!=CameraType.Game)continue;
            nearest=Mathf.Min(nearest,(camera.transform.position-position).sqrMagnitude);
        }
        // Tools/tests without a game camera retain the authored pose quality.
        if(float.IsPositiveInfinity(nearest))return 1;
        if(nearest<=fullDistance*fullDistance)return 1;
        if(nearest>=fadeDistance*fadeDistance)return 0;
        return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(fullDistance,fadeDistance,Mathf.Sqrt(nearest)));
    }
}
