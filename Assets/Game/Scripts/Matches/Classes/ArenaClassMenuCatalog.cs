using System;
using System.Linq;
using UnityEngine;

// Menu-only data: models are string addresses, never gameplay prefab references.
[CreateAssetMenu(menuName="Arena Shooter/Class menu catalog")]
public class ArenaClassMenuCatalog : ScriptableObject
{
    [Serializable] public class Weapon
    {
        public string id,name,previewAddress;
        public SoldierWeaponDefinition.WeaponCategory category;
        public bool isUtility;
        public float damage,rpm;
        public int rounds;
        public Sprite icon;
        public string defaultSight;
        public string[] sights=Array.Empty<string>(),attachments=Array.Empty<string>();
        public Socket[] sockets=Array.Empty<Socket>();
        public BuiltIn[] builtIns=Array.Empty<BuiltIn>();
    }
    [Serializable] public class Part
    {
        public string id,name,previewAddress;
        public Sprite icon;
        public WeaponAttachmentDefinition.MountKind mount;
        public bool optic;
        public Vector3 position,rotation,scale=Vector3.one;
        public float sightLift;
    }
    [Serializable] public class Socket
    {
        public string path;
        public WeaponAttachmentDefinition.MountKind mount;
        public Placement[] placements=Array.Empty<Placement>();
        public Support[] supports=Array.Empty<Support>();
    }
    [Serializable] public class Placement
    { public string part; public Vector3 position,scale; public Quaternion rotation; }
    [Serializable] public class Support
    { public string sight,riser; public Vector3 contactPoint,scale; }
    [Serializable] public class BuiltIn
    { public WeaponAttachmentDefinition.MountKind mount; public string sight; public bool opticMountOnly; public string[] excludedSights=Array.Empty<string>(); public string[] rendererNames=Array.Empty<string>(); }
    public ArenaWeaponCatalog source;
    // Preview addresses remain generated; gameplay facts are refreshed from the shared definitions.
    public void RefreshWeaponFacts()
    {
        if(!source)return;
        foreach(var item in weapons){var definition=source.FindWeapon(item.id);if(!definition)continue;item.name=definition.displayName;item.category=definition.category;item.isUtility=definition.isUtility;item.damage=definition.damage;item.rpm=definition.roundsPerMinute;item.rounds=definition.magazineCapacity;item.icon=definition.icon;}
    }
    public Weapon[] weapons=Array.Empty<Weapon>();
    public Part[] parts=Array.Empty<Part>();
    public Weapon FindWeapon(string id)=>Array.Find(weapons,w=>w.id==id);
    public Part FindPart(string id)=>Array.Find(parts,p=>p.id==id);
    public static bool IsPrimary(Weapon weapon)=>!weapon.isUtility&&weapon.category!=SoldierWeaponDefinition.WeaponCategory.Pistol&&weapon.category!=SoldierWeaponDefinition.WeaponCategory.Launcher&&weapon.category!=SoldierWeaponDefinition.WeaponCategory.Melee&&weapon.category!=SoldierWeaponDefinition.WeaponCategory.Explosive;
}
