using UnityEngine;
[CreateAssetMenu(menuName = "Arena Shooter/Class")]
public class ArenaClassDefinition : ScriptableObject
{
    public string id;
    public string displayName;
    public SoldierWeaponDefinition primary, secondary;
    public WeaponSightDefinition primarySight, secondarySight;
    public int[] ResolveSlots(SoldierWeaponLoadout loadout)
    {
        int Find(SoldierWeaponDefinition weapon)
        {
            int slot = System.Array.FindIndex(loadout.slots, s => s.weapon.definition == weapon);
            if (slot < 0) throw new System.InvalidOperationException($"Class {displayName}: {weapon.name} is missing from {loadout.name}'s catalog.");
            return slot;
        }
        int[] indices = { Find(primary), Find(secondary) };
        WeaponSightDefinition[] sights = { primarySight, secondarySight };
        for (int i=0;i<indices.Length;i++)
        {
            if (!sights[i]) continue;
            var module=loadout.slots[indices[i]].sights;
            if (!module || !module.Supports(sights[i]))
                throw new System.InvalidOperationException($"Class {displayName}: {sights[i].name} does not fit the selected weapon.");
        }
        return indices;
    }
}
