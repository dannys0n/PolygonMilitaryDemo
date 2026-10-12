using System;
using System.Collections.Generic;
using UnityEngine;

// Scene-owned prototype policy. Definitions and saved custom classes remain untouched.
public class ArenaRandomLoadouts : MonoBehaviour
{
    public ArenaWeaponCatalog catalog;
    [Range(0f, 1f)] public float opticChance = .75f;
    [Range(0f, 1f)] public float attachmentChance = .5f;

    public void Apply(ArenaParticipant actor)
    {
        var primary = new List<ArenaWeaponCatalog.Entry>();
        var secondary = new List<ArenaWeaponCatalog.Entry>();
        foreach (var entry in catalog.entries)
        {
            if (Array.FindIndex(actor.player.loadout.slots, slot => slot.definition == entry.weapon) < 0) continue;
            if (ArenaCustomClassStore.IsPrimary(entry.weapon)) primary.Add(entry);
            else if (entry.weapon.category == SoldierWeaponDefinition.WeaponCategory.Pistol) secondary.Add(entry);
        }
        if (primary.Count == 0 || secondary.Count == 0)
            throw new InvalidOperationException("Random equipment needs a primary and pistol in the character catalog.");
        var first = primary[UnityEngine.Random.Range(0, primary.Count)];
        var second = secondary[UnityEngine.Random.Range(0, secondary.Count)];
        var choice = ScriptableObject.CreateInstance<ArenaClassDefinition>();
        choice.isCustom = true; choice.id = "random-equipment"; choice.displayName = "RANDOM EQUIPMENT";
        choice.primary = first.weapon; choice.secondary = second.weapon;
        choice.primarySight = Sight(first, actor.player.loadout); choice.secondarySight = Sight(second, actor.player.loadout);
        choice.primaryAttachments = Attachments(first, actor.player.loadout); choice.secondaryAttachments = Attachments(second, actor.player.loadout);
        choice.grenadeIndex = UnityEngine.Random.Range(0, 3);
        actor.SelectClass(choice);
        Destroy(choice); // SelectClass owns its runtime copy.
    }

    WeaponSightDefinition Sight(ArenaWeaponCatalog.Entry entry, SoldierWeaponLoadout loadout)
    {
        var slot = Array.Find(loadout.slots, s => s.definition == entry.weapon);
        var options = Array.FindAll(entry.sights, sight => sight && slot.Supports(sight));
        return options.Length > 0 && UnityEngine.Random.value < opticChance
            ? options[UnityEngine.Random.Range(0, options.Length)] : null;
    }

    WeaponAttachmentDefinition[] Attachments(ArenaWeaponCatalog.Entry entry, SoldierWeaponLoadout loadout)
    {
        var slot = Array.Find(loadout.slots, s => s.definition == entry.weapon);
        var groups = new Dictionary<WeaponAttachmentDefinition.MountKind, List<WeaponAttachmentDefinition>>();
        foreach (var part in entry.attachments)
        {
            if (!part || !slot.Supports(part) || part.mount == WeaponAttachmentDefinition.MountKind.SightRiser) continue;
            if (!groups.TryGetValue(part.mount, out var options)) groups[part.mount] = options = new List<WeaponAttachmentDefinition>();
            options.Add(part);
        }
        var selected = new List<WeaponAttachmentDefinition>();
        foreach (var options in groups.Values)
            if (UnityEngine.Random.value < attachmentChance) selected.Add(options[UnityEngine.Random.Range(0, options.Count)]);
        return selected.ToArray();
    }
}
