using UnityEngine;

// Only the initial equipped rig is loaded before the ordinary loadout Awake.
[DefaultExecutionOrder(-250)]
public sealed class ArmoryReviewLoadout : MonoBehaviour
{
    public ArmoryDisplayCache library;
    public SoldierWeaponLoadout loadout;
    void Awake()
    {
        var e=library.LoadWeapon(library.defaultWeapon);
        loadout.selectedSlot=0;
        loadout.slots=new[]{Slot(e)};
    }
    public static SoldierWeaponLoadout.Slot Slot(ArenaWeaponCatalog.Entry e)=>new SoldierWeaponLoadout.Slot
    {name=e.weapon.displayName,definition=e.weapon,firstPersonPrefab=e.firstPersonPrefab,thirdPersonPrefab=e.thirdPersonPrefab};
}
