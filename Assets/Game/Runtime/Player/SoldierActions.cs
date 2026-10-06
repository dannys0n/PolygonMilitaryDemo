using UnityEngine;

public class SoldierActions : MonoBehaviour
{
    public enum FireMode
    {
        SemiAutomatic,
        Automatic
    }

    public Animator animator;
    public SoldierFirstPersonView firstPersonView;
    public SoldierMovement movement;
    private SoldierWeaponPresentation presentation;
    public FireMode fireMode => presentation.weapon.definition.fireMode;
    public float roundsPerMinute => presentation.weapon.definition.roundsPerMinute;
    public event System.Action FireStarted;
    public event System.Action ReloadStarted;

    private float nextFireTime;
    private int reloadLayer;

    public bool IsReloading =>
        animator.GetCurrentAnimatorStateInfo(reloadLayer).IsName("Reload") ||
        animator.IsInTransition(reloadLayer) || firstPersonView.IsReloading;

    void Awake()
    {
        presentation = GetComponent<SoldierWeaponPresentation>();
        reloadLayer = animator.GetLayerIndex("Reload");
        animator.ResetTrigger("Fire");
    }

    void Update()
    {
        bool reloadPressed = Cursor.lockState == CursorLockMode.Locked
                          && Input.GetKeyDown(KeyCode.R);

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        // Temporary test input until damage calls PlayHitReaction().
        if (Input.GetKeyDown(KeyCode.H))
            PlayHitReaction();

        if (Input.GetKeyDown(KeyCode.Space))
            movement.RequestJump();

        if (reloadPressed)
        {
            TryReload();
            animator.ResetTrigger("Fire");
            return;
        }

        if (IsReloading)
            return;

        bool wantsToFire = fireMode == FireMode.Automatic
            ? Input.GetMouseButton(0)
            : Input.GetMouseButtonDown(0);

        if (wantsToFire) TryFire();
    }

    // These admission methods can also be called by an input or network command driver.
    public bool TryFire()
    {
        if (IsReloading || Time.time < nextFireTime) return false;
        nextFireTime = Time.time + 60f / Mathf.Max(roundsPerMinute, 1f);
        PlayFire();
        return true;
    }

    public bool TryReload()
    {
        if (IsReloading) return false;
        PlayReload();
        return true;
    }

    public void PlayHitReaction()
    {
        animator.ResetTrigger("HitReaction");
        animator.SetTrigger("HitReaction");
    }

    public void PlayFire()
    {
        animator.ResetTrigger("Fire");
        animator.SetTrigger("Fire");
        firstPersonView.PlayFire();
        FireStarted?.Invoke();
    }

    public void PlayReload()
    {
        animator.SetTrigger("Reload");
        animator.ResetTrigger("Fire");
        firstPersonView.PlayReload();
        ReloadStarted?.Invoke();
    }
}
