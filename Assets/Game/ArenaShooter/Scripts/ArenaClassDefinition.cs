using UnityEngine;
[CreateAssetMenu(menuName = "Arena Shooter/Class")]
public class ArenaClassDefinition : ScriptableObject
{
    public string id;
    public string displayName;
    public SoldierWeaponDefinition primary, secondary;
    public int[] ResolveSlots(SoldierWeaponLoadout loadout)
    {
        int Find(SoldierWeaponDefinition weapon)
        {
            int slot = System.Array.FindIndex(loadout.slots, s => s.weapon.definition == weapon);
            if (slot < 0) throw new System.InvalidOperationException($"Class {displayName}: {weapon.name} is missing from {loadout.name}'s catalog.");
            return slot;
        }
        return new[] { Find(primary), Find(secondary) };
    }
}
