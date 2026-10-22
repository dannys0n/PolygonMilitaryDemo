using System;
using System.Collections.Generic;
using UnityEngine;

// One spawn policy: select compatible catalog weapons and configure the existing loadout directly.
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaRandomLoadouts")]
public class RandomLoadouts : MonoBehaviour
{
    [Range(0f, 1f)] public float opticChance = .75f;
    [Range(0f, 1f)] public float attachmentChance = .5f;

    public void Apply(MatchParticipant actor)
    {
        var primary = new List<WeaponCatalog.Entry>();
        var secondary = new List<WeaponCatalog.Entry>();
        var source=actor.match.settings.weaponCatalog;
        if(!source)throw new InvalidOperationException("Assign a weapon catalog in the match settings.");
        foreach (var entry in source.Weapons(actor.match.settings.polishedWeaponsOnly))
        {
            if (Array.FindIndex(actor.player.loadout.slots, slot => slot.definition == entry.weapon) < 0) continue;
            if (IsPrimary(entry.weapon)) primary.Add(entry);
            else if (!entry.weapon.isUtility && entry.weapon.category == SoldierWeaponDefinition.WeaponCategory.Pistol) secondary.Add(entry);
        }
        if (primary.Count == 0 || secondary.Count == 0)
            throw new InvalidOperationException("Random equipment needs a primary and pistol in the character catalog.");
        var first = primary[UnityEngine.Random.Range(0, primary.Count)];
        var second = secondary[UnityEngine.Random.Range(0, secondary.Count)];
        var loadout=actor.player.loadout;
        loadout.ConfigureAvailableSlots(new[]{Array.FindIndex(loadout.slots,s=>s.definition==first.weapon),Array.FindIndex(loadout.slots,s=>s.definition==second.weapon)});
        loadout.SetSpawnSights(Sight(first,loadout),Sight(second,loadout));
        loadout.SetSpawnAttachments(Attachments(first,loadout),Attachments(second,loadout));
        actor.GetComponent<SoldierGrenades>().SelectType(UnityEngine.Random.Range(0,3));
    }

    static bool IsPrimary(SoldierWeaponDefinition weapon)=>!weapon.isUtility
        && weapon.category!=SoldierWeaponDefinition.WeaponCategory.Pistol
        && weapon.category!=SoldierWeaponDefinition.WeaponCategory.Launcher
        && weapon.category!=SoldierWeaponDefinition.WeaponCategory.Melee
        && weapon.category!=SoldierWeaponDefinition.WeaponCategory.Explosive;

    WeaponSightDefinition Sight(WeaponCatalog.Entry entry, SoldierWeaponLoadout loadout)
    {
        var slot = Array.Find(loadout.slots, s => s.definition == entry.weapon);
        var options = Array.FindAll(entry.sights, sight => sight && slot.Supports(sight));
        return options.Length > 0 && UnityEngine.Random.value < opticChance
            ? options[UnityEngine.Random.Range(0, options.Length)] : null;
    }

    WeaponAttachmentDefinition[] Attachments(WeaponCatalog.Entry entry, SoldierWeaponLoadout loadout)
    {
        var slot = Array.Find(loadout.slots, s => s.definition == entry.weapon);
        var groups = new Dictionary<WeaponAttachmentDefinition.MountKind, List<WeaponAttachmentDefinition>>();
        foreach (var part in entry.attachments)
        {
            if (!part || !slot.Supports(part) || part.mount == WeaponAttachmentDefinition.MountKind.SightRiser) continue;
            if (!groups.TryGetValue(part.mount, out var options)) groups[part.mount] = options = new List<WeaponAttachmentDefinition>();
            options.Add(part);
        }
        var selected = new List<WeaponAttachmentDefinition>(entry.weapon.defaultAttachments);
        foreach (var options in groups.Values)
            if (UnityEngine.Random.value < attachmentChance)
            {
                var part=options[UnityEngine.Random.Range(0, options.Count)];
                selected.RemoveAll(a=>a.mount==part.mount);selected.Add(part);
            }
        return selected.ToArray();
    }
}
