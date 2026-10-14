using UnityEngine;

// Event equipment belongs to this life, never to a shared weapon definition.
public sealed class HecticDropSupply : MonoBehaviour
{
    SoldierLife life;
    ArenaParticipant actor;
    float nextCheck;
    void Awake(){life=GetComponent<SoldierLife>();actor=GetComponent<ArenaParticipant>();}
    void OnEnable(){life.Respawning+=ResetSupply;life.health.Died+=OnDied;}
    void OnDisable(){life.Respawning-=ResetSupply;life.health.Died-=OnDied;}
    void OnDied(GameObject source)=>ResetSupply();
    void ResetSupply()=>life.loadout.SetInfiniteUtilityAmmo(false);
    public void Grant()=>life.loadout.SetInfiniteUtilityAmmo(true);
    public bool HasAmmunition()
    {
        foreach(int index in life.loadout.AvailableSlots)
        {
            var slot=life.loadout.slots[index];
            if(slot.definition.infiniteAmmunition||slot.state.InfiniteOverride||slot.state.MagazineAmmo+slot.state.ReserveAmmo>0)return true;
        }
        var utilities=GetComponent<SoldierUtilitySlots>();
        if(utilities)for(int i=0;i<utilities.utilityWeapons.Length;i++)if(utilities.Count(i)>0)return true;
        return false;
    }
    void Update()
    {
        if(Time.time<nextCheck||!actor.match.IsRunning||actor.health.IsDead||actor.IsAwaitingSpawn||actor.IsSpectator)return;
        nextCheck=Time.time+.5f;
        var occupant=GetComponent<VehicleOccupant>();
        if(occupant&&occupant.IsMounted)return;
        if(!HasAmmunition())actor.health.ApplyDamage(actor.health.maxHealth,null);
    }
}
