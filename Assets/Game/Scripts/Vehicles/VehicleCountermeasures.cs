using UnityEngine;

// Mission-fit aircraft dispensers and armored smoke launchers. Recharge is arena gameplay tuning.
public sealed class VehicleCountermeasures : MonoBehaviour
{
    public bool flares, chaff, smoke;
    public Transform[] dispensers;
    public VehicleDecoy flarePrefab, chaffPrefab;
    public GameObject smokePrefab;
    [Min(1)] public int capacity=2;
    public float cooldown=10, rechargeSeconds=25, smokeDuration=10;
    public bool automaticForAI=true;
    public int Charges { get; private set; }
    public int Incoming { get; private set; }
    public float ReadyIn => Mathf.Max(0,nextDeploy-Time.time);
    public float RechargeIn => Charges<capacity ? Mathf.Max(0,nextRecharge-Time.time) : 0;
    public string SystemName => smoke ? "SMOKE" : flares&&chaff ? "FLARE / CHAFF" : flares ? "FLARES" : "CHAFF";
    Vehicle vehicle;
    float nextDeploy,nextRecharge,firstThreat=-1,lastHealth;
    void Awake(){vehicle=GetComponent<Vehicle>();Charges=capacity;lastHealth=vehicle.maxHealth;}
    void Update()
    {
        Refill();
        Incoming=0;
        bool relevant=false;
        foreach(var missile in VehicleMissileGuidance.Active)
            if(missile.Target==vehicle)
            {
                Incoming++;
                relevant|=smoke|| (missile.SeekerType==VehicleMissileGuidance.Seeker.Infrared&&flares)
                    ||(missile.SeekerType==VehicleMissileGuidance.Seeker.Radar&&chaff);
            }
        if(relevant){if(firstThreat<0)firstThreat=Time.time;}else firstThreat=-1;
        var driver=vehicle.Driver;
        if(automaticForAI && driver && !driver.IsLocal && !vehicle.IsDestroyed)
        {
            if(firstThreat>=0 && Time.time-firstThreat>=.5f)TryDeploy(driver);
            else if(smoke && vehicle.Health<lastHealth)TryDeploy(driver);
        }
        lastHealth=vehicle.Health;
    }
    void Refill()
    {
        while(Charges<capacity && Time.time>=nextRecharge)
        {Charges++;nextRecharge+=Mathf.Max(.1f,rechargeSeconds);}
    }
    public bool TryDeploy(VehicleOccupant operatorActor)
    {
        if(!operatorActor || operatorActor.MountedVehicle!=vehicle)return false;
        var role=vehicle.seats[operatorActor.SeatIndex].role;
        if(role==Vehicle.SeatRole.Passenger)return false;
        return Deploy();
    }
    public bool Deploy()
    {
        Refill();
        if(vehicle.IsDestroyed || Charges<=0 || Time.time<nextDeploy || !(flares||chaff||smoke))return false;
        if(Charges==capacity)nextRecharge=Time.time+Mathf.Max(.1f,rechargeSeconds);
        Charges--;nextDeploy=Time.time+Mathf.Max(.1f,cooldown);
        foreach(var socket in dispensers)
        {
            if(!socket)continue;
            if(flares&&flarePrefab)Release(flarePrefab,socket);
            if(chaff&&chaffPrefab)Release(chaffPrefab,socket);
            if(smoke&&smokePrefab)
            {
                var cloud=Instantiate(smokePrefab,socket.position+socket.forward*4,Quaternion.identity);
                var obscurant=cloud.GetComponent<GrenadeSmoke>();
                if(!obscurant)obscurant=cloud.AddComponent<GrenadeSmoke>();
                obscurant.duration=smokeDuration;obscurant.radius=5;
                // Re-arm expiry after configuring the cloned cloud.
                obscurant.enabled=false;obscurant.enabled=true;
                Destroy(cloud,smokeDuration+2);
            }
        }
        return true;
    }
    void Release(VehicleDecoy prefab,Transform socket)
    {
        var decoy=Instantiate(prefab,socket.position,socket.rotation);
        decoy.velocity=vehicle.Body.GetPointVelocity(socket.position)+socket.forward*12;
    }
}
