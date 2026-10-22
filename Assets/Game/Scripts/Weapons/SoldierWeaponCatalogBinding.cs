using System;
using System.Collections.Generic;
using UnityEngine;

// A character owns weapon state. Shared metadata and view prefabs come from one catalog.
[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(SoldierWeaponLoadout))]
public sealed class SoldierWeaponCatalogBinding : MonoBehaviour
{
    public ArenaWeaponCatalog catalog;
    void Awake()
    {
        var actor=GetComponent<ArenaParticipant>();
        if(actor){var match=actor.match?actor.match:ArenaMatch.FindSceneMatch(gameObject.scene);if(match && match.settings && match.settings.weaponCatalog)catalog=match.settings.weaponCatalog;}
        Initialize(catalog);
    }
    // Configure inactive actors before activation; does not spawn presentation or reset ammo.
    public void Initialize(ArenaWeaponCatalog source)
    {
        catalog=source;
        var loadout=GetComponent<SoldierWeaponLoadout>();
        var previous=loadout.slots.Length>loadout.selectedSlot?loadout.slots[loadout.selectedSlot].definition:null;
        if(!catalog)throw new InvalidOperationException("Assign the shared weapon catalog to "+name);
        var slots=new List<SoldierWeaponLoadout.Slot>();
        foreach(var entry in catalog.Weapons())
        {
            if(!entry.firstPersonPrefab||!entry.thirdPersonPrefab)throw new InvalidOperationException("Missing weapon views for "+entry.weapon.displayName);
            slots.Add(new SoldierWeaponLoadout.Slot{name=entry.weapon.displayName,definition=entry.weapon,firstPersonPrefab=entry.firstPersonPrefab,thirdPersonPrefab=entry.thirdPersonPrefab});
        }
        loadout.slots=slots.ToArray();int selected=Array.FindIndex(loadout.slots,s=>s.definition==previous);loadout.selectedSlot=Mathf.Max(0,selected);
    }
}
