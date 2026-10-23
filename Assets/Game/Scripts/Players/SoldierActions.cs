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
    readonly RoundReloadClock roundReload = new();
    public event System.Action<float> ReloadSectionChanged;
    public WeaponReloadDefinition CurrentReload { get; private set; }
    public bool IsFullReload { get; private set; }
    public bool IsRoundReload => IsReloading && CurrentReload.roundReload.IsValid;
    public void RequestReloadStop() { if (IsRoundReload) roundReload.RequestStop(); }
    SoldierWeaponDefinition spreadWeapon;
    float hipBloom, recoverHipBloomAt;

    public float HipSpreadAt(float now)
    {
        if (!loadout || loadout.slots.Length == 0) return 0f;
        var definition = loadout.slots[loadout.selectedSlot].definition;
        if (spreadWeapon != definition) { spreadWeapon = definition; hipBloom = 0f; }
        float baseSpread = definition.UsesSpread ? definition.spreadDegrees : definition.hipFireSpreadDegrees;
        return baseSpread + RecoveredHipBloom(definition, now);
    }
    float RecoveredHipBloom(SoldierWeaponDefinition definition, float now)
        => hipBloom * Mathf.Exp(-Mathf.Max(0f, now - recoverHipBloomAt) / Mathf.Max(.01f, definition.hipBloomRecoveryTime));
    void RecordHipShot(SoldierWeaponDefinition definition, float now, bool aiming)
    {
        hipBloom = aiming ? 0f : Mathf.Min(definition.maximumHipBloom,
            RecoveredHipBloom(definition, now) + definition.hipBloomPerShot);
        recoverHipBloomAt = now + definition.hipBloomRecoveryDelay;
    }
    public bool IsSwapReload => IsReloading && reloadDefinition.UsesSwapReload;
    public bool ReloadHasRound => IsSwapReload && reloadEquipStarted;
    public bool IsReloading { get; private set; }
    public float ReloadProgress => IsReloading
        ? IsRoundReload ? roundReload.Progress(Time.time) : Mathf.Clamp01(1f - (reloadEndTime - Time.time) / reloadDuration)
        : 0f;

    void Awake() { loadout=GetComponent<SoldierWeaponLoadout>(); grenades=GetComponent<SoldierGrenades>(); launchers=GetComponent<SoldierUtilitySlots>(); }

    void Update() => Tick(Time.time);

    // Gameplay clock can be supplied by the simulation owner; UI/animation stay frame driven.
    public void Tick(float now)
    {
        if (firstPersonView && firstPersonView.WantsToAim) hipBloom = 0f;
        if (!IsReloading) return;
        if (IsRoundReload)
        {
            bool changed = roundReload.Tick(now, reloadingWeapon, reloadDefinition);
            if (roundReload.Current != RoundReloadClock.Phase.Complete)
            {
                if (changed) ReloadSectionChanged?.Invoke(roundReload.NormalizedTime);
                return;
            }
            IsReloading = false;
            // Closing finishes the reload; interruption input never replays an action.
            animator.ResetTrigger("Reload");
            animator.Play("Empty", animator.GetLayerIndex("Reload"), 0f);
            firstPersonView.CancelReload();
            ReloadCompleted?.Invoke();
            return;
        }
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
        if (IsRoundReload)
        {
            RequestReloadStop();
            return false;
        }
        if(!CanFire)return false;
        return TryFire(now,shooting.AimRay,shooting.AimUp);
    }
    public bool TryFire(float now, Ray aim, Vector3 up)
    {
        if (IsRoundReload) { RequestReloadStop(); return false; }
        if (!CanFire || aim.direction.sqrMagnitude<.000001f) return false;
        if (!loadout.EquippedState.TryConsumeRound(now, loadout.EquippedWeapon.definition)) return false;
        var definition = loadout.EquippedWeapon.definition;
        bool aiming = firstPersonView && firstPersonView.WantsToAim;
        float hipSpread = HipSpreadAt(now);
        shooting.Fire(definition,loadout.EquippedMuzzle,aim,up,movement.Velocity,
            aiming ? 0f : hipSpread);
        RecordHipShot(definition, now, aiming);
        loadout.RefreshLoadedProjectiles();
        ShotFired?.Invoke();
        PresentFire();
        return true;
    }

    public bool TryReload() => TryReload(Time.time);
    public bool TryReload(float now)
    {
        if (IsRoundReload) { RequestReloadStop(); return false; }
        if (!enabled ||  IsThrowing || IsReloading || loadout.IsSwitching) return false;
        if (!loadout.EquippedState.CanReload(loadout.EquippedWeapon.definition)) return false;
        IsReloading = true;
        reloadingWeapon = loadout.EquippedState;
        reloadDefinition = loadout.EquippedWeapon.definition;
        IsFullReload = reloadingWeapon.MagazineAmmo <= 0;
        CurrentReload = reloadDefinition.ReloadFor(reloadingWeapon.MagazineAmmo);
        reloadDuration = IsSwapReload ? Mathf.Max(CurrentReload.duration,
            reloadDefinition.unequipDuration+reloadDefinition.equipDuration) : Mathf.Max(.01f, CurrentReload.duration);
        reloadEndTime = now + reloadDuration;
        reloadEquipStarted=false;
        if (IsRoundReload)
        {
            int rounds = Mathf.Min(reloadDefinition.magazineCapacity - reloadingWeapon.MagazineAmmo, reloadingWeapon.ReserveAmmo);
            roundReload.Begin(CurrentReload.roundReload, reloadDuration, rounds, now);
        }
        loadout.presentation.ConfigureReload(CurrentReload);
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
        if (IsRoundReload) ReloadSectionChanged?.Invoke(0f);
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
        hipBloom = 0f; spreadWeapon = null;
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
