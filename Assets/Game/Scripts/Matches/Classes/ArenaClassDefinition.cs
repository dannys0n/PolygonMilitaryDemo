using UnityEngine;
[CreateAssetMenu(menuName = "Arena Shooter/Class")]
public class ArenaClassDefinition : ScriptableObject
{
    public bool isCustom;
    public int customIndex,grenadeIndex;
    public WeaponAttachmentDefinition[] primaryAttachments=System.Array.Empty<WeaponAttachmentDefinition>(), secondaryAttachments=System.Array.Empty<WeaponAttachmentDefinition>();
    public string id;
    public string displayName;
    public SoldierWeaponDefinition primary, secondary;
    public WeaponSightDefinition primarySight, secondarySight;
    public int[] ResolveSlots(SoldierWeaponLoadout loadout)
    {
        int Find(SoldierWeaponDefinition weapon)
        {
            int slot = System.Array.FindIndex(loadout.slots, s => s.definition == weapon);
            if (slot < 0) throw new System.InvalidOperationException($"Class {displayName}: {weapon.name} is missing from {loadout.name}'s catalog.");
            return slot;
        }
        int[] indices = { Find(primary), Find(secondary) };
        WeaponSightDefinition[] sights = { primarySight, secondarySight };
        for (int i=0;i<indices.Length;i++)
        {
            if (!sights[i]) continue;
            if (!loadout.slots[indices[i]].Supports(sights[i]))
                throw new System.InvalidOperationException($"Class {displayName}: {sights[i].name} does not fit the selected weapon.");
        }
        WeaponAttachmentDefinition[][] attachments={primaryAttachments,secondaryAttachments};
        for(int i=0;i<indices.Length;i++)foreach(var attachment in attachments[i])
            if(!loadout.slots[indices[i]].Supports(attachment))
                throw new System.InvalidOperationException("Attachment does not fit the selected weapon.");
        return indices;
    }
}
