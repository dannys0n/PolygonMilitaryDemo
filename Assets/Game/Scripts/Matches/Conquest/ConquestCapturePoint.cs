using UnityEngine;

public class ConquestCapturePoint : MonoBehaviour
{
    public string letter = "A", displayName = "Yard";
    [Min(1)] public float radius = 9;
    [Min(1)] public float height = 6;
    [Min(1)] public float captureSeconds = 15;
    [Min(1)] public int maximumCapturers = 3;
    public Transform[] spawnPoints = System.Array.Empty<Transform>();
    public Renderer flag;
    public MatchParticipant.Team? Owner { get; private set; }
    public float Progress { get; private set; }
    public int RedCount { get; private set; }
    public int BlueCount { get; private set; }
    public bool IsContested => RedCount > 0 && BlueCount > 0;
    public int Count(MatchParticipant.Team team) => team == MatchParticipant.Team.Red ? RedCount : BlueCount;
    public bool Contains(Vector3 position)
    {
        Vector3 offset = position-transform.position;
        return offset.y >= -.5f && offset.y <= height && offset.x*offset.x+offset.z*offset.z <= radius*radius;
    }
    public void ResetCapture() { Owner=null; Progress=0; RedCount=BlueCount=0; UpdateFlag(); }
    public bool Tick(MatchParticipant[] actors, float deltaTime)
    {
        RedCount=BlueCount=0;
        foreach(var actor in actors)
        {
            if(!actor.isActiveAndEnabled || actor.health.IsDead || actor.IsAwaitingSpawn || !Contains(actor.transform.position)) continue;
            if(actor.team==MatchParticipant.Team.Red)RedCount++;else BlueCount++;
        }
        var previous=Owner;
        int advantage=BlueCount-RedCount;
        if(advantage!=0)
        {
            Progress=Mathf.Clamp(Progress+Mathf.Clamp(advantage,-maximumCapturers,maximumCapturers)*Mathf.Max(0,deltaTime)/captureSeconds,-1,1);
            if(Owner==MatchParticipant.Team.Red && Progress>=0 || Owner==MatchParticipant.Team.Blue && Progress<=0)Owner=null;
            if(Progress<=-1)Owner=MatchParticipant.Team.Red;
            else if(Progress>=1)Owner=MatchParticipant.Team.Blue;
        }
        UpdateFlag();return previous!=Owner;
    }
    public static Color TeamColor(MatchParticipant.Team? team) => team==MatchParticipant.Team.Red
        ? new Color(1,.57f,.12f) : team==MatchParticipant.Team.Blue ? new Color(.82f,.9f,1) : new Color(.5f,.52f,.54f);
    Material flagMaterial;
    void UpdateFlag()
    {
        if(!flag)return;
        if(!flagMaterial)flagMaterial=flag.material;
        flagMaterial.color=TeamColor(Owner);
    }
    void OnDestroy() { if(flagMaterial)Destroy(flagMaterial); }
    void OnDrawGizmosSelected()
    {
        Gizmos.color=TeamColor(Owner);Gizmos.DrawWireSphere(transform.position,radius);
        Gizmos.DrawWireCube(transform.position+Vector3.up*height*.5f,new Vector3(radius*2,height,radius*2));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position+Vector3.up*3,letter+" — "+displayName);
#endif
    }
}
