using System.Collections;
using UnityEngine;

// A transport plane uses normal flight inputs. Respawns enter real cargo space then walk out.
[DefaultExecutionOrder(-120)]
public sealed class HecticTransportPass : MonoBehaviour
{
    public HecticWarfareScenario scenario;
    public ArenaParticipant.Team team;
    public Vehicle prefab;
    public GameObject pilotPrefab;
    public float altitude=110,duration=60,cruiseSpeed=75;
    public Vehicle Aircraft { get; private set; }
    VehicleOccupant pilot;
    GameObject pilotContainer;
    Transform exit;
    float endsAt,nextDeployment;
    int dropIndex,leg;
    bool ready, retired;
    public bool CanReceiveDeployment=>Aircraft&&!Aircraft.IsDestroyed&&!retired;
    public void Begin()=>StartCoroutine(Launch());
    IEnumerator Launch()
    {
        Vector3 side=team==ArenaParticipant.Team.Red?Vector3.left:Vector3.right;
        Vector3 start=scenario.battlefieldCenter+side*scenario.battlefieldRadius*.65f+Vector3.up*altitude;
        Vector3 forward=-side;
        Aircraft=Instantiate(prefab,start,Quaternion.LookRotation(forward));Aircraft.name="Hectic transport "+team;
        if(Aircraft.motor is JetMotor jet)jet.startingThrottle=.6f;
        pilotContainer=new GameObject("Event pilot");pilotContainer.SetActive(false);
        var actor=Instantiate(pilotPrefab,pilotContainer.transform);
        actor.GetComponent<ArenaParticipant>().enabled=false;
        actor.GetComponent<ArenaAIInput>().enabled=false;
        actor.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled=false;
        var transport=actor.GetComponent<ArenaVehicleAI>();if(transport)transport.enabled=false;
        pilot=actor.GetComponent<VehicleOccupant>();pilot.faction=(int)team;
        pilotContainer.SetActive(true);yield return null;
        if(!Aircraft||Aircraft.IsDestroyed)yield break;
        Aircraft.seats[0].occupant=pilot;pilot.Mount(Aircraft,0);
        Aircraft.Body.linearVelocity=forward*cruiseSpeed;
        var doors=Aircraft.GetComponent<VehicleDoors>();if(doors)doors.ToggleAll();
        exit=new GameObject("Cargo drop exit").transform;exit.SetParent(Aircraft.transform,false);
        exit.localPosition=doors&&doors.doors.Length>0
            ?Aircraft.transform.InverseTransformPoint(doors.doors[0].interaction.position)+new Vector3(0,1,-3)
            :new Vector3(0,2,-22);
        endsAt=Time.time+duration;ready=true;
    }
    public bool TryDeploy(ArenaParticipant actor)
    {
        if(!ready||!Aircraft||Aircraft.IsDestroyed||Time.time>=endsAt||Time.time<nextDeployment)return false;
        // Space candidates are inside the cargo floor, separated from seated pilot positions.
        for(int attempt=0;attempt<8;attempt++)
        {
            int index=(dropIndex+attempt)%8;
            var b=Aircraft.NavigationBounds;
            Vector3 local=new Vector3((index%2==0?-1:1)*.65f,1.5f,Mathf.Lerp(-9,-3,index/2f/3));
            Vector3 position=Aircraft.transform.TransformPoint(local);
            Vector3 rayOrigin=position+Aircraft.transform.up*2;
            RaycastHit best=default;bool found=false;
            foreach(var hit in Physics.RaycastAll(rayOrigin,-Aircraft.transform.up,4,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(hit.collider.attachedRigidbody==Aircraft.Body&&hit.normal.y>.6f&&(!found||hit.distance<best.distance)){best=hit;found=true;}
            if(!found)continue;
            position=best.point+Vector3.up*.12f;
            if(!ArenaSpawnSelection.HasSpace(scenario.match,actor,position))continue;
            actor.Respawn(position,Quaternion.Euler(0,Aircraft.transform.eulerAngles.y+180,0));
            if(actor.IsAwaitingSpawn)return false;
            actor.player.controller.body.linearVelocity=Aircraft.Body.GetPointVelocity(position);
            var input=actor.GetComponent<HecticAirDeploymentInput>();if(input)input.Begin(Aircraft,exit,scenario.battlefieldCenter);
            var utility=actor.GetComponent<SoldierUtilitySlots>();if(utility&&scenario.dropUtilities!=null)utility.Configure(scenario.dropUtilities);
            var supply=actor.GetComponent<HecticDropSupply>();if(supply)supply.Grant();
            dropIndex=(index+1)%8;nextDeployment=Time.time+.25f;return true;
        }
        return false;
    }
    void FixedUpdate()
    {
        if(!ready||!Aircraft||Aircraft.IsDestroyed)return;
        if(Time.time>=endsAt){Retire();return;}
        Vector3 from=Aircraft.transform.position-scenario.battlefieldCenter;
        float sign=(team==ArenaParticipant.Team.Red?1:-1)*(leg==0?1:-1);
        Vector3 target=scenario.battlefieldCenter+new Vector3(sign*scenario.battlefieldRadius*.6f,altitude,0);
        if(Vector3.ProjectOnPlane(target-Aircraft.transform.position,Vector3.up).magnitude<250)
        {leg=1-leg;target=scenario.battlefieldCenter+new Vector3(-sign*scenario.battlefieldRadius*.6f,altitude,0);}
        if(Vector3.ProjectOnPlane(from,Vector3.up).magnitude>scenario.battlefieldRadius*.8f)target=scenario.battlefieldCenter+Vector3.up*altitude;
        Vector3 direction=target-Aircraft.transform.position;
        float horizontal=new Vector2(direction.x,direction.z).magnitude;
        // Altitude control is independent of distance to the far end of the map.
        // Include the airframe's level-flight trim instead of commanding a zero-lift nose angle.
        float trim=0;
        if(Aircraft.motor is JetMotor jet)
        {
            float pressure=.5f*jet.airDensity*Mathf.Max(25,Aircraft.Body.linearVelocity.sqrMagnitude);
            float coefficient=Aircraft.Body.mass*Physics.gravity.magnitude/(pressure*jet.wingArea);
            trim=Mathf.Clamp((coefficient-jet.liftBias)/jet.liftSlope*Mathf.Rad2Deg-jet.wingIncidence,0,8);
        }
        float climb=Mathf.Clamp(direction.y*.02f-Aircraft.Body.linearVelocity.y*.08f,-.2f,.2f);
        float pitch=-Mathf.Atan(climb)*Mathf.Rad2Deg-trim;
        Aircraft.DriverInput=new VehicleInput{directionalFlight=true,heading=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,
            viewPitch=Mathf.Clamp(pitch,-16,12),move=new Vector2(0,Aircraft.Body.linearVelocity.magnitude<cruiseSpeed?1:-.3f)};
    }
    public void Retire()
    {
        if(retired)return;
        retired=true;ready=false;
        // End of service retires the aircraft through the normal destruction effects.
        if(Aircraft&&Aircraft.Driver==pilot)pilot.Leave(true);
        if(Aircraft&&!Aircraft.IsDestroyed)Aircraft.ApplyDamage(Aircraft.maxHealth,null);
        if(Aircraft)Destroy(Aircraft.gameObject,7);
        if(pilotContainer)Destroy(pilotContainer);
        Destroy(gameObject);
    }
    void OnDestroy(){if(pilotContainer)Destroy(pilotContainer);if(!retired&&Aircraft)Destroy(Aircraft.gameObject);}
}
