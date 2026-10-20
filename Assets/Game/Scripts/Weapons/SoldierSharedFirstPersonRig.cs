using UnityEngine;

// The player keeps this skeleton between weapon swaps; only weapon geometry is pooled.
public sealed class SoldierSharedFirstPersonRig : MonoBehaviour
{
    public Animator animator;
    public Transform weaponMount;
    public Vector3 mountPosition = new Vector3(0, -1.55f, 0);
    public Quaternion mountRotation = Quaternion.identity;
    public Vector3 mountScale = Vector3.one;
    SoldierAuthoredWeaponMotion motion;
    Renderer[] renderers;

    public void Bind(SoldierWeaponView weapon)
    {
        if (!motion) motion = GetComponent<SoldierAuthoredWeaponMotion>();
        if (!motion) motion = gameObject.AddComponent<SoldierAuthoredWeaponMotion>();
        motion.Bind(weapon);
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        if (!visible) return;
        // Perspective switching can disable the cached renderers individually.
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers) if (renderer) renderer.enabled = true;
    }
}
