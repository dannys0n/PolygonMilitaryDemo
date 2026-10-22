using UnityEngine;
[CreateAssetMenu(menuName="Game/Weapon catalog")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaWeaponCatalog")]
public class WeaponCatalog : ScriptableObject
{
    [System.Serializable] public class Entry
    {
        public SoldierWeaponDefinition weapon;
        public GameObject basePrefab;
        public SoldierWeaponView firstPersonPrefab, thirdPersonPrefab;
        [Tooltip("Verified FPS and TPS animations and placement, not just a placeholder controller.")]
        public bool polished;
        public WeaponSightDefinition[] sights;
        [Tooltip("Initial optic for a newly selected weapon; spawn equipment may override it.")]
        public WeaponSightDefinition defaultSight;
        public WeaponAttachmentDefinition[] attachments;
        public string animationStyle;
    }
    public Entry[] entries;
    public System.Collections.Generic.IEnumerable<Entry> Weapons(bool polishedOnly=false)
    {foreach(var entry in entries)if(entry.weapon&&(!polishedOnly||entry.polished))yield return entry;}
    public Entry Find(SoldierWeaponDefinition definition)=>System.Array.Find(entries,e=>e.weapon==definition);
    public SoldierWeaponDefinition FindWeapon(string id)=>System.Array.Find(entries,e=>e.weapon.catalogId==id)?.weapon;
}
