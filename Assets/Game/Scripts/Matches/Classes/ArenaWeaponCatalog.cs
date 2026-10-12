using UnityEngine;
[CreateAssetMenu(menuName="Arena Shooter/Weapon catalog")]
public class ArenaWeaponCatalog : ScriptableObject
{
    [System.Serializable] public class Entry
    {
        public SoldierWeaponDefinition weapon;
        public GameObject basePrefab;
        public WeaponSightDefinition[] sights;
        [Tooltip("Initial optic for a newly selected weapon; custom classes can override it.")]
        public WeaponSightDefinition defaultSight;
        public WeaponAttachmentDefinition[] attachments;
        public string animationStyle;
    }
    public Entry[] entries;
    public Entry Find(SoldierWeaponDefinition definition)=>System.Array.Find(entries,e=>e.weapon==definition);
    public SoldierWeaponDefinition FindWeapon(string id)=>System.Array.Find(entries,e=>e.weapon.catalogId==id)?.weapon;
}
