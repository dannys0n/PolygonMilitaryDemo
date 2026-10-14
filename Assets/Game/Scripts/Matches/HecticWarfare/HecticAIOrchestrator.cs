using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Strategic decisions are squad-sized and infrequent. Infantry retains perception and firing.
public class HecticAIOrchestrator : MonoBehaviour
{
    public ArenaMatch match;
    public ConquestRules conquest;
    public ConquestSquads squads;
    public float decisionInterval=4, buddySeparation=4, regroupDistance=14;
    [Range(1,3)] public int maximumSquadsPerObjective=3;
    public sealed class SquadOrder
    {
        public ArenaParticipant.Team team;
        public int squad;
        public ConquestCapturePoint point;
        public readonly List<ArenaAIInput> members=new List<ArenaAIInput>(4);
    }
    public readonly List<SquadOrder> Orders=new List<SquadOrder>();
    readonly Dictionary<ArenaParticipant,SquadOrder> lookup=new Dictionary<ArenaParticipant,SquadOrder>();
    ArenaParticipant[] roster;
    float nextDecision;
    void Awake(){if(!match)match=GetComponent<ArenaMatch>();if(!conquest)conquest=GetComponent<ConquestRules>();if(!squads)squads=GetComponent<ConquestSquads>();}
    void Update(){if(!match||!match.IsRunning)return;if(Time.time>=nextDecision){RefreshOrders();nextDecision=Time.time+decisionInterval;}}
    public void RefreshOrders()
    {
        if(!match||!conquest||!squads)return;
        if(roster!=match.participants||lookup.Count==0)
        {
            roster=match.participants;Orders.Clear();lookup.Clear();
            foreach(var actor in roster)
            {
                if(!actor||!actor.TryGetComponent<ArenaAIInput>(out var ai))continue;
                int index=squads.SquadOf(actor);if(index<0)continue;
                var order=Orders.Find(o=>o.team==actor.team&&o.squad==index);
                if(order==null){order=new SquadOrder{team=actor.team,squad=index};Orders.Add(order);}
                order.members.Add(ai);lookup[actor]=order;
            }
        }
        var counts=new Dictionary<ConquestCapturePoint,int>();
        foreach(ArenaParticipant.Team team in System.Enum.GetValues(typeof(ArenaParticipant.Team)))
        {
            counts.Clear();
            foreach(var order in Orders)
            {
                if(order.team!=team)continue;
                ArenaAIInput leader=Leader(order);if(!leader)continue;
                bool recentCombat=Time.time-leader.LastCombatTime<conquest.postCaptureDefenseTime;
                ConquestCapturePoint choice=null;float best=float.NegativeInfinity;
                foreach(var point in conquest.points)
                {
                    if(!point)continue;counts.TryGetValue(point,out int assigned);
                    // Soft overflow only when the map has fewer objectives than squads/3.
                    float priority=point.Owner==team?(recentCombat&&order.point==point?65:8):50;
                    float score=priority-assigned*18-(assigned>=maximumSquadsPerObjective?100:0)
                        -Mathf.Min(15,Vector3.Distance(leader.transform.position,point.transform.position)*.04f)
                        +(point==order.point&&point.Owner!=team?5:0);
                    if(score>best){best=score;choice=point;}
                }
                order.point=choice;if(choice){counts.TryGetValue(choice,out int n);counts[choice]=n+1;}
            }
        }
    }
    public static ArenaAIInput Leader(SquadOrder order)
    {foreach(var ai in order.members)if(ai&&!ai.life.health.IsDead&&!ai.participant.IsAwaitingSpawn)return ai;return null;}
    public SquadOrder OrderFor(ArenaParticipant actor){lookup.TryGetValue(actor,out var value);return value;}
    public ConquestCapturePoint ObjectivePoint(ArenaParticipant actor)=>OrderFor(actor)?.point;
    public ArenaParticipant PreferredSquadSpawn(ArenaParticipant actor)
    {
        ArenaParticipant best=null;float closest=float.PositiveInfinity;
        foreach(var member in squads.Members(actor))if(squads.CanDeploy(actor,member))
        {float d=(member.transform.position-actor.transform.position).sqrMagnitude;if(d<closest){closest=d;best=member;}}
        return best;
    }
    public bool TryGetCombatDestination(ArenaAIInput ai, Vector3 threat, out Vector3 destination)
    {
        destination=default;var order=OrderFor(ai.participant);if(order==null)return false;
        int index=order.members.IndexOf(ai);
        Vector3 delta=Vector3.ProjectOnPlane(threat-ai.transform.position,Vector3.up);
        Vector3 forward=delta.sqrMagnitude>.01f?delta.normalized:ai.transform.forward;
        Vector3 right=Vector3.Cross(Vector3.up,forward);
        float advance=Mathf.Max(0,delta.magnitude-ai.attackRange*.85f);
        Vector3 candidate=ai.transform.position+forward*Mathf.Min(advance,12)
            +right*((index%2==0?-1:1)*(3+index/2));
        var filter=new NavMeshQueryFilter{agentTypeID=ai.agent.agentTypeID,areaMask=ai.agent.areaMask};
        if(!NavMesh.SamplePosition(candidate,out var hit,3,filter)||NavMesh.Raycast(ai.transform.position,hit.position,out _,filter))return false;
        destination=hit.position;return true;
    }
    public bool TryGetObjective(ArenaAIInput ai,out Vector3 destination)
    {
        destination=default;var order=OrderFor(ai.participant);if(order==null||!order.point)return false;
        var leader=Leader(order);int index=order.members.IndexOf(ai);
        float radius=Mathf.Min(order.point.radius*.65f,8);
        Vector3 target=order.point.transform.position;
        bool fighting=ai.IsCombatAlert||ai.Target;
        // Pairs travel together, with separate lanes. Under attack they widen spacing.
        if(leader&&leader!=ai&&!fighting&&(leader.transform.position-target).sqrMagnitude>625)
        {
            var buddy=order.members[Mathf.Max(0,index-index%2)];
            Vector3 anchor=buddy&&buddy!=ai&&!buddy.life.health.IsDead?buddy.transform.position:leader.transform.position;
            if((anchor-ai.transform.position).sqrMagnitude>regroupDistance*regroupDistance)
                target=anchor;
            else target=leader.transform.position+leader.transform.forward*6;
            target+=leader.transform.right*((index%2==0?-1:1)*buddySeparation)+leader.transform.forward*(-(index/2)*5);
        }
        else target+=Quaternion.Euler(0,index*90+order.squad*31,0)*Vector3.forward*(fighting?radius:radius*.6f);
        var filter=new NavMeshQueryFilter{agentTypeID=ai.agent.agentTypeID,areaMask=ai.agent.areaMask};
        if(!NavMesh.SamplePosition(target,out var hit,8,filter))return false;
        destination=hit.position;return true;
    }
}
