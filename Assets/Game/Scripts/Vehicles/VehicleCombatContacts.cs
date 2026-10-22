using System.Collections.Generic;
using UnityEngine;

// One short-lived contact snapshot per vehicle, shared by its driver and weapon stations.
// Mounted crew share one hull target; dismounted actors remain individual contacts.
public sealed class VehicleCombatContacts : MonoBehaviour
{
    readonly List<MatchParticipant> contacts = new List<MatchParticipant>();
    readonly HashSet<Vehicle> hulls = new HashSet<Vehicle>();
    MatchController owner;
    MatchParticipant.Team side;
    float refreshAt;
    public IReadOnlyList<MatchParticipant> Read(MatchParticipant observer)
    {
        if(owner == observer.match && side == observer.team && Time.time < refreshAt)return contacts;
        owner=observer.match;side=observer.team;
        refreshAt=Time.time+.2f;
        contacts.Clear();hulls.Clear();
        foreach(var actor in owner.participants)
        {
            if(!actor || !actor.isActiveAndEnabled || actor.team==side || actor.IsAwaitingSpawn || actor.health.IsDead)continue;
            var occupant=actor.GetComponent<VehicleOccupant>();
            if(occupant && occupant.IsMounted && !hulls.Add(occupant.MountedVehicle))continue;
            contacts.Add(actor);
        }
        return contacts;
    }
}
