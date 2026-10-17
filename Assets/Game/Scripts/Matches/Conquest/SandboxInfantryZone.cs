using UnityEngine;
using UnityEngine.AI;

// Author the fight by moving this capture center and resizing its child spawn volume.
public sealed class SandboxInfantryZone : MonoBehaviour
{
    public ConquestCapturePoint point;
    public BoxCollider respawnBox;
    [Min(1)] public int playersPerTeam=6;
    NavMeshPath path;
    void Awake(){path=new NavMeshPath();}
    public bool TryPosition(ArenaParticipant actor,out Vector3 position)
    {
        position=default;if(!point||!respawnBox)return false;
        path ??=new NavMeshPath();
        var agent=actor.GetComponent<NavMeshAgent>();
        var filter=new NavMeshQueryFilter{agentTypeID=agent?agent.agentTypeID:0,areaMask=NavMesh.AllAreas};
        if(!NavMesh.SamplePosition(point.transform.position,out var goal,point.radius,filter))return false;
        for(int attempt=0;attempt<64;attempt++)
        {
            Vector3 local=respawnBox.center+new Vector3(Random.Range(-.5f,.5f)*respawnBox.size.x,0,Random.Range(-.5f,.5f)*respawnBox.size.z);
            var proposed=respawnBox.transform.TransformPoint(local);
            if(!NavMesh.SamplePosition(proposed,out var hit,respawnBox.size.y*.5f,filter))continue;
            var inBox=respawnBox.transform.InverseTransformPoint(hit.position)-respawnBox.center;
            var half=respawnBox.size*.5f;
            if(Mathf.Abs(inBox.x)>half.x||Mathf.Abs(inBox.y)>half.y||Mathf.Abs(inBox.z)>half.z)continue;
            if(!NavMesh.CalculatePath(hit.position,goal.position,filter,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
            // Check real standing room without importing HQ spawn-history or authored-point rules.
            bool blocked=false;
            foreach(var c in Physics.OverlapCapsule(hit.position+Vector3.up*.55f,hit.position+Vector3.up*1.5f,.4f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!c.transform.IsChildOf(actor.transform)){blocked=true;break;}
            if(blocked)continue;
            foreach(var other in actor.match.participants)
                if(other!=actor&&!other.health.IsDead&&!other.IsAwaitingSpawn
                    && Vector3.Distance(other.transform.position,hit.position)<2){blocked=true;break;}
            if(blocked)continue;
            position=hit.position+Vector3.up*.05f;return true;
        }
        return false;
    }
    void OnDrawGizmosSelected()
    {
        if(!respawnBox)return;Gizmos.color=new Color(1,.5f,.1f,.7f);Gizmos.matrix=respawnBox.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(respawnBox.center,respawnBox.size);
    }
}
