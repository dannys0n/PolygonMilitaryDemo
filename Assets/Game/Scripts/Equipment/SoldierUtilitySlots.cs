using System;
using UnityEngine;

// Utility slots select normal weapons. Animation, sights, ammo and firing have one owner.
[DefaultExecutionOrder(120)]
public class SoldierUtilitySlots : MonoBehaviour
{
    public SoldierWeaponDefinition[] utilityWeapons = Array.Empty<SoldierWeaponDefinition>();
    public SoldierLife life;
    public int Selected => Array.IndexOf(utilityWeapons, life.loadout.EquippedWeapon.definition);
    public bool IsEquipped => Selected >= 0;
    public SoldierWeaponDefinition Equipped => life.loadout.EquippedWeapon.definition;
    public bool Ready => IsEquipped && !life.loadout.IsSwitching && !life.actions.IsReloading
        && life.loadout.EquippedState.ReadyAt <= Time.time;
    int previousSlot;
    public int Slot(int index) => index >= 0 && index < utilityWeapons.Length && utilityWeapons[index]
        ? Array.FindIndex(life.loadout.slots, s => s.definition == utilityWeapons[index]) : -1;
    public int Count(int index)
    {
        int slot = Slot(index); if (slot < 0) return 0;
        var state = life.loadout.slots[slot].state;
        return state == null ? utilityWeapons[index].magazineCapacity + utilityWeapons[index].startingReserveAmmo
            : state.MagazineAmmo + state.ReserveAmmo;
    }
    public bool SetUtility(int index, SoldierWeaponDefinition weapon)
    {
        if (index < 0 || index >= utilityWeapons.Length || !weapon || !weapon.isUtility
            || Array.FindIndex(life.loadout.slots, s => s.definition == weapon) < 0) return false;
        utilityWeapons[index] = weapon; return true;
    }
    public void Configure(SoldierWeaponDefinition[] weapons)
    {
        if (weapons == null) return;
        foreach (var weapon in weapons)
            if (weapon && (!weapon.isUtility || Array.FindIndex(life.loadout.slots, s => s.definition == weapon) < 0))
                throw new ArgumentException("Utility weapons must be compatible and present in the character catalog.");
        utilityWeapons = (SoldierWeaponDefinition[])weapons.Clone();
    }
    public bool TryEquip(int index)
    {
        int slot = Slot(index);
        if (slot < 0 || !life.actions.enabled || life.health.IsDead || life.actions.IsThrowing) return false;
        if (IsEquipped && Selected == index) return true;
        if (!IsEquipped) previousSlot = life.loadout.selectedSlot;
        return life.loadout.TrySelectUtilityWeapon(slot);
    }
    public void Unequip()
    {
        if (!IsEquipped) return;
        life.actions.CancelReload(); life.loadout.CancelSwitch();
        life.loadout.SetStartingSlot(Array.IndexOf(life.loadout.AvailableSlots, previousSlot) >= 0
            ? previousSlot : life.loadout.AvailableSlots[0]);
    }
}
