using UnityEngine;

[DefaultExecutionOrder(-30)]
public class SoldierActions : MonoBehaviour
{
    public enum FireMode { SemiAutomatic, Automatic }
    public Animator animator;
    public SoldierFirstPersonView firstPersonView;
    public SoldierMovement movement;
    public SoldierHitscan shooting;
    private SoldierWeaponLoadout loadout;
    private SoldierGrenades grenades;
    private SoldierUtilitySlots launchers;
    public bool IsThrowing => grenades && grenades.IsThrowing;
    public FireMode fireMode => loadout.EquippedWeapon.definition.fireMode;
    public float roundsPerMinute => loadout.EquippedWeapon.definition.roundsPerMinute;
    public event System.Action FireStarted;
    public event System.Action ReloadStarted;
    public event System.Action ReloadCompleted;
    public event System.Action ReloadCanceled;
    private SoldierWeaponLoadout.WeaponState reloadingWeapon;
    private SoldierWeaponDefinition reloadDefinition;
    private float reloadEndTime;
    public bool IsReloading { get; private set; }
    public float ReloadProgress => IsReloading
        ? Mathf.Clamp01(1f - (reloadEndTime - Time.time) / reloadDefinition.reloadDuration)
        : 0f;

    void Awake() { loadout=GetComponent<SoldierWeaponLoadout>(); grenades=GetComponent<SoldierGrenades>(); launchers=GetComponent<SoldierUtilitySlots>(); }

    void Update()
    {
        if (!IsReloading || Time.time < reloadEndTime) return;
        IsReloading = false;
        reloadingWeapon.CompleteReload(reloadDefinition);
        ReloadCompleted?.Invoke();
    }

    public bool TryFire()
    {
        if (!enabled ||  IsThrowing || IsReloading || loadout.IsSwitching || movement.IsRunning) return false;
        if (!loadout.EquippedState.TryConsumeRound(Time.time, loadout.EquippedWeapon.definition)) return false;
        shooting.Fire(loadout.EquippedWeapon.definition, loadout.EquippedMuzzle);
        PresentFire();
        return true;
    }

    public bool TryReload()
    {
        if (!enabled ||  IsThrowing || IsReloading || loadout.IsSwitching) return false;
        if (!loadout.EquippedState.CanReload(loadout.EquippedWeapon.definition)) return false;
        IsReloading = true;
        reloadingWeapon = loadout.EquippedState;
        reloadDefinition = loadout.EquippedWeapon.definition;
        reloadEndTime = Time.time + reloadDefinition.reloadDuration;
        animator.SetTrigger("Reload");
        animator.ResetTrigger("Fire");
        firstPersonView.PlayReload();
        ReloadStarted?.Invoke();
        return true;
    }

    public void CancelReload()
    {
        if (!IsReloading) return;
        IsReloading = false;
        animator.ResetTrigger("Reload");
        animator.Play("Empty", animator.GetLayerIndex("Reload"), 0f);
        firstPersonView.CancelReload();
        ReloadCanceled?.Invoke();
    }

    // Reset pending actions before death, despawn, or a later respawn reset.
    public void CancelActions()
    {
        if(launchers)launchers.Unequip();
        if(grenades)grenades.CancelThrow();
        CancelReload();
        loadout.CancelSwitch();
        firstPersonView.CancelAim();
        firstPersonView.SetAimRequested(false);
        animator.ResetTrigger("Fire");
        animator.ResetTrigger("HitReaction");
    }

    public void PlayHitReaction()
    {
        animator.ResetTrigger("HitReaction");
        animator.SetTrigger("HitReaction");
    }

    public void PlayFire() => TryFire();

    private void PresentFire()
    {
        animator.ResetTrigger("Fire");
        animator.SetTrigger("Fire");
        firstPersonView.PlayFire();
        FireStarted?.Invoke();
    }

    public void PlayReload() => TryReload();
    void OnDisable() => CancelActions();
}
