using System.Collections.Generic;
using UnityEngine;
public class GrenadeSmoke : MonoBehaviour
{
    static readonly List<GrenadeSmoke> clouds=new List<GrenadeSmoke>();
    public float radius=4.5f,duration=12f;
    float expires;
    void OnEnable(){expires=Time.time+duration;clouds.Add(this);}
    void OnDisable()=>clouds.Remove(this);
    public static bool BlocksLine(Vector3 from,Vector3 to)
    {
        Vector3 delta=to-from;
        foreach(var cloud in clouds)
        {
            if(Time.time>=cloud.expires)continue;
            float t=Mathf.Clamp01(Vector3.Dot(cloud.transform.position-from,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
            if((from+delta*t-cloud.transform.position).sqrMagnitude<cloud.radius*cloud.radius)return true;
        }
        return false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset()=>clouds.Clear();
}
