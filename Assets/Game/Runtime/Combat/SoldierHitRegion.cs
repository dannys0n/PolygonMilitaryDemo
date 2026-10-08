using UnityEngine;

// Put this on each damage collider; all regions refer to the same health owner.
public class SoldierHitRegion : MonoBehaviour
{
    public SoldierHealth health;
    public bool head;

    public bool ApplyHit(SoldierWeaponDefinition weapon, GameObject source) =>
        health.ApplyDamage(weapon.damage * (head ? weapon.headshotMultiplier : 1f), source);
}
