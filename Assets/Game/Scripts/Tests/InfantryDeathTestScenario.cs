using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A small, controlled lab using the real health, movement, ragdoll and respawn lifecycle.
public sealed class InfantryDeathTestScenario : MonoBehaviour
{
    public enum TestCase { Standing, Walking, Jumping, Crowd }
    public ArenaMatch match;
    public GameObject actorPrefab;
    public BehaviorTestCamera observer;
    public TestCase testCase;
    public bool ragdollEnabled=true, fallImpulseEnabled=true;
    public float crowdSpacing=.85f;
    public int crowdSize=6;
    public sealed class Sample
    {
        public SoldierLife life;
        public SoldierRagdoll ragdoll;
        public InfantryDeathTestInput input;
        public Vector3 spawn;
        public float livePeak, preDeathSpeed, ragdollPeak, peakUpward, maxRise, overlapDepth;
        public string deepestContact="none",fastestBody="none";
        public float deathTime=-1,hipsAtDeath;
        public bool motorOffAtDeath;
    }
    public readonly List<Sample> Actors=new();
    public bool Ready {get;private set;}
    public int Deaths {get;private set;}
    public int CompletedCycles {get;private set;}
    public int Errors {get;private set;}
    public bool Cycling {get;private set;}
    public float MaxObservedCapsuleSpeed {get;private set;}
    public float MaxObservedRagdollSpeed {get;private set;}
    public float MaxObservedRise {get;private set;}
    GameObject population;
    int selected;
    string status="Reset and kill an actor. Compare capsule speed with ragdoll speed.";
    readonly Collider[] overlaps=new Collider[128];
    Vector2 scroll;
    void OnEnable()=>Application.logMessageReceived+=OnLog;
    void OnDisable(){Application.logMessageReceived-=OnLog;Time.timeScale=1;}
    void OnLog(string message,string stack,LogType type)
    {if(type==LogType.Error||type==LogType.Exception){Errors++;status=message;}}
    void Start(){match.enabled=false;ResetScenario();}
    public void SetCase(TestCase value){testCase=value;ResetScenario();}
    public void ResetScenario()
    {
        StopAllCoroutines();Cycling=false;Ready=false;selected=0;Deaths=CompletedCycles=Errors=0;
        MaxObservedCapsuleSpeed=MaxObservedRagdollSpeed=MaxObservedRise=0;
        if(observer)observer.follow=null;
        if(population){population.SetActive(false);Destroy(population);}
        Actors.Clear();population=new GameObject("Death test infantry");population.SetActive(false);
        int count=testCase==TestCase.Crowd?Mathf.Clamp(crowdSize,2,12):1;
        for(int i=0;i<count;i++)
        {
            var actor=Instantiate(actorPrefab,population.transform);actor.name="Infantry "+(i+1);
            var participant=actor.GetComponent<ArenaParticipant>();participant.match=match;participant.enabled=false;
            foreach(var behaviour in actor.GetComponents<Behaviour>())
                if(behaviour is ArenaAIInput || behaviour is ArenaVehicleAI || behaviour is SoldierLocalInput || behaviour is SandboxInfantryInput || behaviour is UnityEngine.AI.NavMeshAgent)behaviour.enabled=false;
            var life=actor.GetComponent<SoldierLife>();
            life.view.firstPerson=false;life.view.playerCamera.enabled=false;
            var ragdoll=actor.GetComponent<SoldierRagdoll>();ragdoll.enabled=ragdollEnabled;
            if(!fallImpulseEnabled)ragdoll.fallImpulse=0;
            var input=actor.AddComponent<InfantryDeathTestInput>();input.walking=testCase==TestCase.Walking;
            actor.GetComponent<SoldierController>().SetControl(input, SoldierControlRole.AI, false);
            Vector3 point=new Vector3(i%3*crowdSpacing,.08f,i/3*crowdSpacing);
            actor.transform.SetPositionAndRotation(point,Quaternion.identity);
            var sample=new Sample{life=life,ragdoll=ragdoll,input=input,spawn=point};Actors.Add(sample);
        }
        population.SetActive(true);
        // Observe after the production ragdoll's OnEnable has subscribed its death transition.
        foreach(var sample in Actors)sample.life.health.Died+=source=>RecordDeath(sample);
        StartCoroutine(FinishSetup());
    }
    IEnumerator FinishSetup()
    {
        yield return null;yield return null;
        RespawnAll();Ready=true;status="Ready. Foot IK remains off. No match AI, damage forces, or automatic respawn.";
        if(testCase==TestCase.Jumping)JumpSelected();
    }
    public void KillSelected(){if(Ready&&selected<Actors.Count)Kill(Actors[selected]);}
    public void KillAll(){if(Ready)foreach(var sample in Actors)Kill(sample);}
    void Kill(Sample sample)
    {
        if(sample.life.health.IsDead)return;
        sample.preDeathSpeed=sample.life.controller.Velocity.magnitude;
        sample.life.health.ApplyDamage(sample.life.health.maxHealth,null);
    }
    void RecordDeath(Sample sample)
    {
        Deaths++;sample.deathTime=Time.time;
        sample.hipsAtDeath=sample.ragdoll.bodies[0].position.y;
        sample.motorOffAtDeath=!sample.life.controller.enabled&&!sample.life.controller.capsule.enabled&&sample.life.controller.body.isKinematic;
        Physics.SyncTransforms();
        foreach(var shape in sample.ragdoll.bodyColliders)
        {
            if(!shape.enabled)continue;
            int count=Physics.OverlapBoxNonAlloc(shape.bounds.center,shape.bounds.extents,overlaps,Quaternion.identity,Physics.AllLayers,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var other=overlaps[i];
                if(!other||other.transform.IsChildOf(sample.life.transform)||Physics.GetIgnoreLayerCollision(shape.gameObject.layer,other.gameObject.layer)||Physics.GetIgnoreCollision(shape,other))continue;
                if(Physics.ComputePenetration(shape,shape.transform.position,shape.transform.rotation,other,other.transform.position,other.transform.rotation,out _,out float depth)&&depth>sample.overlapDepth)
                {sample.overlapDepth=depth;sample.deepestContact=shape.name+" / "+other.name;}
            }
        }
        SampleMotion(sample);
    }
    void FixedUpdate(){if(Ready)foreach(var sample in Actors)SampleMotion(sample);}
    void SampleMotion(Sample sample)
    {
        if(!sample.life.health.IsDead)
        {sample.livePeak=Mathf.Max(sample.livePeak,sample.life.controller.Velocity.magnitude);MaxObservedCapsuleSpeed=Mathf.Max(MaxObservedCapsuleSpeed,sample.livePeak);return;}
        if(!sample.ragdoll.IsRagdoll)return;
        foreach(var body in sample.ragdoll.bodies)
        {
            float speed=body.linearVelocity.magnitude;
            if(speed>sample.ragdollPeak){sample.ragdollPeak=speed;sample.fastestBody=body.name;}
            sample.peakUpward=Mathf.Max(sample.peakUpward,body.linearVelocity.y);
        }
        sample.maxRise=Mathf.Max(sample.maxRise,sample.ragdoll.bodies[0].position.y-sample.hipsAtDeath);
        MaxObservedRagdollSpeed=Mathf.Max(MaxObservedRagdollSpeed,sample.ragdollPeak);
        MaxObservedRise=Mathf.Max(MaxObservedRise,sample.maxRise);
    }
    void Respawn(Sample sample)
    {
        sample.life.Respawn(sample.spawn,Quaternion.identity);
        sample.livePeak=sample.preDeathSpeed=sample.ragdollPeak=sample.peakUpward=sample.maxRise=sample.overlapDepth=0;
        sample.deepestContact=sample.fastestBody="none";sample.deathTime=-1;
    }
    public void RespawnSelected(){if(selected<Actors.Count)Respawn(Actors[selected]);}
    public void RespawnAll(){foreach(var sample in Actors)Respawn(sample);}
    public void JumpSelected(){if(Ready&&selected<Actors.Count&&!Actors[selected].life.health.IsDead)Actors[selected].life.movement.RequestJump();}
    public void CycleDeaths(int count){if(Ready&&!Cycling)StartCoroutine(Cycle(count));}
    IEnumerator Cycle(int count)
    {
        Cycling=true;CompletedCycles=0;
        for(int i=0;i<count;i++)
        {
            RespawnAll();yield return new WaitForSeconds(.8f);
            if(testCase==TestCase.Jumping){JumpSelected();yield return new WaitForSeconds(.2f);}
            KillAll();yield return new WaitForSeconds(2);
            CompletedCycles++;
            float peak=0;foreach(var sample in Actors)peak=Mathf.Max(peak,sample.ragdollPeak);
            Debug.Log($"Death lab cycle {CompletedCycles}: ragdoll peak {peak:F2} m/s; errors {Errors}.");
        }
        Cycling=false;status="Repeated deaths complete. Last cycle measurements remain visible.";
    }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(14,14,390,Screen.height-28),GUI.skin.box);
        scroll=GUILayout.BeginScrollView(scroll);
        GUILayout.Label("INFANTRY DEATH / RAGDOLL LAB");
        GUILayout.Label("RMB + WASD: free camera; Q/E: height; Shift: fast");
        int choice=GUILayout.Toolbar((int)testCase,new[]{"Standing","Walking","Jumping","Crowd"});
        if(choice!=(int)testCase)SetCase((TestCase)choice);
        bool rag=GUILayout.Toggle(ragdollEnabled,"Ragdoll enabled"),impulse=GUILayout.Toggle(fallImpulseEnabled,"Normal death fall impulse");
        if(rag!=ragdollEnabled||impulse!=fallImpulseEnabled){ragdollEnabled=rag;fallImpulseEnabled=impulse;ResetScenario();}
        if(testCase==TestCase.Crowd)
        {
            GUILayout.Label($"Spacing: {crowdSpacing:F2} m (reset to apply)");
            crowdSpacing=GUILayout.HorizontalSlider(crowdSpacing,.5f,1.5f);
        }
        GUI.enabled=Ready&&!Cycling;
        if(GUILayout.Button("RESET SCENARIO"))ResetScenario();
        GUILayout.BeginHorizontal();if(GUILayout.Button("Kill selected"))KillSelected();if(GUILayout.Button("Kill all"))KillAll();GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();if(GUILayout.Button("Respawn selected"))RespawnSelected();if(GUILayout.Button("Respawn all"))RespawnAll();GUILayout.EndHorizontal();
        if(GUILayout.Button("Jump selected"))JumpSelected();
        if(GUILayout.Button("Repeat 20 death / respawn cycles"))CycleDeaths(20);
        GUI.enabled=true;
        if(GUILayout.Button(Time.timeScale==1?"Slow motion (0.2x)":"Normal speed"))Time.timeScale=Time.timeScale==1?.2f:1;
        for(int i=0;i<Actors.Count;i++)
        {
            var sample=Actors[i];
            if(GUILayout.Button((i==selected?"> ":"")+sample.life.name+ (sample.life.health.IsDead?" / DEAD":" / ALIVE")))selected=i;
        }
        if(selected<Actors.Count)
        {
            var sample=Actors[selected];
            if(GUILayout.Button("Follow selected (RMB returns to free camera)"))observer.follow=sample.ragdoll.IsRagdoll?sample.ragdoll.bodies[0].transform:sample.life.transform;
            GUILayout.Label($"Live capsule peak: {sample.livePeak:F2} m/s\nSpeed before kill: {sample.preDeathSpeed:F2} m/s\nRagdoll peak: {sample.ragdollPeak:F2} m/s ({sample.fastestBody})\nUpward peak: {sample.peakUpward:F2} m/s\nHips rise after death: {sample.maxRise:F2} m\nInitial overlap: {sample.overlapDepth*100:F1} cm\nDeepest contact: {sample.deepestContact}\nCapsule safely disabled: {(sample.deathTime<0?"not dead":sample.motorOffAtDeath.ToString())}");
        }
        GUILayout.Label($"Deaths: {Deaths}   Cycles: {CompletedCycles}   Errors: {Errors}\nRun maximum: capsule {MaxObservedCapsuleSpeed:F2} m/s / ragdoll {MaxObservedRagdollSpeed:F2} m/s / rise {MaxObservedRise:F2} m\n{status}");
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
}
