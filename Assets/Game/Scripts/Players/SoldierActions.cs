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
    public event System.Action ShotFired;
    public event System.Action ReloadStarted;
    public event System.Action ReloadCompleted;
    public event System.Action ReloadCanceled;
    private SoldierWeaponLoadout.WeaponState reloadingWeapon;
    private SoldierWeaponDefinition reloadDefinition;
    private float reloadEndTime, reloadDuration;
    private bool reloadEquipStarted;
    public bool IsSwapReload => IsReloading && reloadDefinition.UsesSwapReload;
    public bool ReloadHasRound => IsSwapReload && reloadEquipStarted;
    public bool IsReloading { get; private set; }
    public float ReloadProgress => IsReloading
        ? Mathf.Clamp01(1f - (reloadEndTime - Time.time) / reloadDuration)
        : 0f;

    void Awake() { loadout=GetComponent<SoldierWeaponLoadout>(); grenades=GetComponent<SoldierGrenades>(); launchers=GetComponent<SoldierUtilitySlots>(); }

    void Update() => Tick(Time.time);

    // Gameplay clock can be supplied by the simulation owner; UI/animation stay frame driven.
    public void Tick(float now)
    {
        if (!IsReloading) return;
        if (IsSwapReload && !reloadEquipStarted && now >= reloadEndTime-reloadDefinition.equipDuration)
        {
            reloadEquipStarted=true;
            loadout.RefreshLoadedProjectiles();
            loadout.PlayWeaponSwitch("Equip");
        }
        if (now < reloadEndTime) return;
        IsReloading = false;
        reloadingWeapon.CompleteReload(reloadDefinition);
        ReloadCompleted?.Invoke();
    }

    public bool TryFire() => TryFire(Time.time);
    bool CanFire => enabled && !IsThrowing && !IsReloading && !loadout.IsSwitching && !movement.IsRunning;
    public bool TryFire(float now)
    {
        if(!CanFire)return false;
        return TryFire(now,shooting.AimRay,shooting.AimUp);
    }
    public bool TryFire(float now, Ray aim, Vector3 up)
    {
        if (!CanFire || aim.direction.sqrMagnitude<.000001f) return false;
        if (!loadout.EquippedState.TryConsumeRound(now, loadout.EquippedWeapon.definition)) return false;
        shooting.Fire(loadout.EquippedWeapon.definition,loadout.EquippedMuzzle,aim,up,movement.Velocity);
        loadout.RefreshLoadedProjectiles();
        ShotFired?.Invoke();
        PresentFire();
        return true;
    }

    public bool TryReload() => TryReload(Time.time);
    public bool TryReload(float now)
    {
        if (!enabled ||  IsThrowing || IsReloading || loadout.IsSwitching) return false;
        if (!loadout.EquippedState.CanReload(loadout.EquippedWeapon.definition)) return false;
        IsReloading = true;
        reloadingWeapon = loadout.EquippedState;
        reloadDefinition = loadout.EquippedWeapon.definition;
        reloadDuration = IsSwapReload ? Mathf.Max(reloadDefinition.reloadDuration,
            reloadDefinition.unequipDuration+reloadDefinition.equipDuration) : reloadDefinition.reloadDuration;
        reloadEndTime = now + reloadDuration;
        reloadEquipStarted=false;
        animator.ResetTrigger("Fire");
        if(IsSwapReload)
        {
            firstPersonView.CancelAim();
            loadout.PlayWeaponSwitch("Unequip");
        }
        else
        {
            animator.SetTrigger("Reload");
            firstPersonView.PlayReload();
        }
        ReloadStarted?.Invoke();
        return true;
    }

    public void CancelReload()
    {
        if (!IsReloading) return;
        bool swapReload=IsSwapReload;
        IsReloading = false;
        if(swapReload)loadout.PlayWeaponSwitch("Empty");
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

    // Cosmetic replay never consumes ammunition, resolves hits, or spawns projectiles.
    public void PresentFire()
    {
        animator.ResetTrigger("Fire");
        animator.SetTrigger("Fire");
        firstPersonView.PlayFire();
        FireStarted?.Invoke();
    }

    public void PlayReload() => TryReload();
    void OnDisable() => CancelActions();
}
