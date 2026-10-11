using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Mode-owned membership and spawn eligibility; characters keep their existing lifecycle.
public class ConquestSquads : MonoBehaviour
{
    public ArenaMatch match;
    [Min(1)] public int squadSize=4;
    [Min(0)] public float combatCooldown=8;
    [Min(0)] public float unsafeEnemyDistance=12;
    readonly Dictionary<ArenaParticipant,int> membership=new Dictionary<ArenaParticipant,int>();
    readonly Dictionary<ArenaParticipant,float> damagedAt=new Dictionary<ArenaParticipant,float>();
    readonly Dictionary<ArenaParticipant,Action<float,GameObject>> damageHandlers=new Dictionary<ArenaParticipant,Action<float,GameObject>>();
    readonly List<Transform> candidates=new List<Transform>();
    [NonSerialized] Transform[] spawnPool;
    [NonSerialized] ArenaParticipant[] roster;
    static readonly string[] names={"ALPHA","BRAVO","CHARLIE","DELTA"};
    public static string SquadName(int number)=>number>=0 && number<names.Length?names[number]:"SQUAD "+(number+1);
    void OnEnable(){match.MatchStarted+=ResetSquads;}
    void OnDisable(){match.MatchStarted-=ResetSquads;Unsubscribe();}
    void Update(){if(roster!=match.participants)RefreshMembership();}
    void ResetSquads(){membership.Clear();damagedAt.Clear();RefreshMembership();}
    void Unsubscribe(){foreach(var pair in damageHandlers)if(pair.Key)pair.Key.health.Damaged-=pair.Value;damageHandlers.Clear();}
    void RefreshMembership()
    {
        Unsubscribe();roster=match.participants;
        foreach(var old in new List<ArenaParticipant>(membership.Keys))
            if(Array.IndexOf(roster,old)<0){membership.Remove(old);damagedAt.Remove(old);}
        foreach(var actor in roster)
        {
            if(!membership.ContainsKey(actor))
            {
                int squad=0;
                while(Count(actor.team,squad)>=squadSize)squad++;
                membership[actor]=squad;
            }
            var member=actor;Action<float,GameObject> handler=(damage,source)=>damagedAt[member]=Time.time;
            damageHandlers[member]=handler;member.health.Damaged+=handler;
        }
    }
    int Count(ArenaParticipant.Team team,int squad){int count=0;foreach(var p in membership)if(p.Key.team==team&&p.Value==squad)count++;return count;}
    public int SquadOf(ArenaParticipant actor)=>membership.TryGetValue(actor,out int squad)?squad:-1;
    public IEnumerable<ArenaParticipant> Members(ArenaParticipant actor)
    {
        int squad=SquadOf(actor);
        foreach(var pair in membership)if(pair.Key.team==actor.team && pair.Value==squad)yield return pair.Key;
    }
    public string UnavailableReason(ArenaParticipant incoming,ArenaParticipant member)
    {
        if(!member || member==incoming || member.match!=match || member.team!=incoming.team || SquadOf(member)!=SquadOf(incoming) || SquadOf(member)<0)return "UNAVAILABLE";
        if(member.health.IsDead || member.IsAwaitingSpawn)return "DOWN";
        var occupant=member.GetComponent<VehicleOccupant>();if(occupant && occupant.IsMounted)return "IN VEHICLE";
        var ai=member.GetComponent<ArenaAIInput>();
        if(Time.time-member.LastShotTime<combatCooldown || damagedAt.TryGetValue(member,out float damageTime)&&Time.time-damageTime<combatCooldown || ai&&(ai.Target || ai.IsCombatAlert))return "IN COMBAT";
        Vector3 eye=member.transform.position+Vector3.up*1.4f;
        foreach(var enemy in match.participants)
        {
            if(enemy.team==member.team || enemy.health.IsDead || enemy.IsAwaitingSpawn || Vector3.Distance(enemy.transform.position,member.transform.position)>unsafeEnemyDistance)continue;
            var delta=enemy.transform.position+Vector3.up*1.4f-eye;
            if(!Physics.Raycast(eye,delta.normalized,out var hit,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<ArenaParticipant>()==enemy)return "ENEMY NEARBY";
        }
        return "";
    }
    public bool CanDeploy(ArenaParticipant incoming,ArenaParticipant member)=>UnavailableReason(incoming,member)=="";
    public bool TryChooseSpawn(ArenaParticipant actor,ArenaParticipant member,Dictionary<Transform,float> history,out Transform spawn)
    {
        spawn=null;if(!CanDeploy(actor,member))return false;
        if(spawnPool==null || spawnPool.Length==0)
        {
            spawnPool=new Transform[8];
            for(int i=0;i<spawnPool.Length;i++){spawnPool[i]=new GameObject("Squad deployment candidate "+i).transform;spawnPool[i].SetParent(transform);}
        }
        candidates.Clear();
        var ai=actor.GetComponent<ArenaAIInput>();var filter=new NavMeshQueryFilter{agentTypeID=ai?ai.agent.agentTypeID:0,areaMask=NavMesh.AllAreas};
        for(int i=0;i<spawnPool.Length;i++)
        {
            Vector3 point=member.transform.position+Quaternion.Euler(0,i*45,0)*Vector3.forward*3;
            if(!NavMesh.SamplePosition(point,out var hit,1.5f,filter) || Mathf.Abs(hit.position.y-member.transform.position.y)>1.5f || NavMesh.Raycast(member.transform.position,hit.position,out _,filter))continue;
            var candidate=spawnPool[i];candidate.SetPositionAndRotation(hit.position+Vector3.up*.04f,member.transform.rotation);candidates.Add(candidate);
        }
        return ArenaSpawnSelection.TryChooseFrom(match,actor,candidates,history,out spawn);
    }
}
