using UnityEngine;

// Only the initial equipped rig is loaded before the ordinary loadout Awake.
[DefaultExecutionOrder(-250)]
public sealed class ArmoryReviewLoadout : MonoBehaviour
{
    public ArmoryWall wall;
    public SoldierWeaponLoadout loadout;
    void Awake()
    {
        wall.ReadAssets();
        var initial = System.Array.Find(wall.Index.weapons, w => w.id.Contains("mg5") || w.id.EndsWith("mgx5_ue4")) ?? wall.Index.weapons[0];
        var e = wall.Index.LoadWeapon(initial.id);
        loadout.selectedSlot=0;
        loadout.slots=new[]{Slot(e)};
    }
    public static SoldierWeaponLoadout.Slot Slot(WeaponCatalog.Entry e)=>new SoldierWeaponLoadout.Slot
    {name=e.weapon.displayName,definition=e.weapon,firstPersonPrefab=e.firstPersonPrefab,thirdPersonPrefab=e.thirdPersonPrefab};
}
