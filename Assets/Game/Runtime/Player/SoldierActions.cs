using UnityEngine;

[DefaultExecutionOrder(-30)]
public class SoldierActions : MonoBehaviour
{
    public enum FireMode { SemiAutomatic, Automatic }
    public Animator animator;
    public SoldierFirstPersonView firstPersonView;
    public SoldierMovement movement;
    private SoldierWeaponLoadout loadout;
    public FireMode fireMode => loadout.EquippedWeapon.definition.fireMode;
    public float roundsPerMinute => loadout.EquippedWeapon.definition.roundsPerMinute;
    public event System.Action FireStarted;
    public event System.Action ReloadStarted;
    public event System.Action ReloadCompleted;
    public event System.Action ReloadCanceled;
    private float nextFireTime;
    private float reloadEndTime;
    public bool IsReloading { get; private set; }
    public float ReloadProgress => IsReloading
        ? Mathf.Clamp01(1f - (reloadEndTime - Time.time) / loadout.EquippedWeapon.definition.reloadDuration)
        : 0f;

    void Awake() => loadout = GetComponent<SoldierWeaponLoadout>();

    void Update()
    {
        if (!IsReloading || Time.time < reloadEndTime) return;
        IsReloading = false;
        ReloadCompleted?.Invoke();
    }

    public bool TryFire()
    {
        if (IsReloading || loadout.IsSwitching || movement.IsRunning || Time.time < nextFireTime) return false;
        nextFireTime = Time.time + 60f / Mathf.Max(roundsPerMinute, 1f);
        PresentFire();
        return true;
    }

    public bool TryReload()
    {
        if (IsReloading || loadout.IsSwitching) return false;
        IsReloading = true;
        reloadEndTime = Time.time + loadout.EquippedWeapon.definition.reloadDuration;
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
        CancelReload();
        loadout.CancelSwitch();
        nextFireTime = Time.time;
        firstPersonView.CancelAim();
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
