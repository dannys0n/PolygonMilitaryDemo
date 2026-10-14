using System.Collections.Generic;
using UnityEngine;

// HQ deployment uses the ordinary vehicle seat ownership, never hidden duplicate seats.
public sealed class HecticVehicleDepot : MonoBehaviour
{
    public ArenaParticipant.Team team;
    public VehicleSpawnPoint[] pads;
    public float radius=120;
    readonly List<Vehicle> candidates=new List<Vehicle>();
    public static int AvailableSeat(Vehicle vehicle,VehicleOccupant occupant)
    {
        for(int i=0;i<vehicle.seats.Length;i++)
        {
            var seat=vehicle.seats[i];
            if(seat.anchor&&seat.exit&&!seat.occupant&&(!seat.reservation||seat.reservation==occupant||Time.time>=seat.reservedUntil))return i;
        }
        return -1;
    }
    public bool TryDeploy(ArenaParticipant actor)
    {
        var occupant=actor.GetComponent<VehicleOccupant>();if(!occupant)return false;
        candidates.Clear();
        foreach(var vehicle in Vehicle.Active)
        {
            if(!vehicle||vehicle.IsDestroyed||vehicle.gameObject.scene!=gameObject.scene
                ||(vehicle.transform.position-transform.position).sqrMagnitude>radius*radius
                ||vehicle.Body.linearVelocity.sqrMagnitude>4)continue;
            bool enemy=false;foreach(var seat in vehicle.seats)if(seat.occupant&&seat.occupant.faction!=(int)team){enemy=true;break;}
            if(!enemy&&AvailableSeat(vehicle,occupant)>=0)candidates.Add(vehicle);
        }
        if(candidates.Count==0)return false;
        // A random usable vehicle, preferentially filling existing crews for company transport.
        candidates.Sort((a,b)=>b.IsOccupied.CompareTo(a.IsOccupied));
        int occupied=0;while(occupied<candidates.Count&&candidates[occupied].IsOccupied)occupied++;
        var chosen=candidates[Random.Range(0,occupied>0?occupied:candidates.Count)];
        int index=AvailableSeat(chosen,occupant);
        occupant.faction=(int)actor.team;
        Vector3 position=chosen.transform.TransformPoint(chosen.EntryBounds.center);
        actor.Respawn(position,Quaternion.Euler(0,chosen.transform.eulerAngles.y,0));
        if(actor.IsAwaitingSpawn)return false;
        if(chosen.Board(occupant,index))return true;
        actor.IsAwaitingSpawn=true;actor.player.SetGameplayAllowed(false);return false;
    }
}
