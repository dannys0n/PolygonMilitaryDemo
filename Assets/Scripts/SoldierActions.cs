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
    public FireMode fireMode = FireMode.SemiAutomatic;
    [Min(1f)] public float roundsPerMinute = 600f;
    public event System.Action FireStarted;
    public event System.Action ReloadStarted;

    private float nextFireTime;
    private int fireLayer;
    private int reloadLayer;
    private int hitReactionLayer;

    void Awake()
    {
        fireLayer = animator.GetLayerIndex("Fire");
        reloadLayer = animator.GetLayerIndex("Reload");
        hitReactionLayer = animator.GetLayerIndex("HitReaction");
        animator.ResetTrigger("Fire");
    }

    void Update()
    {
        bool reloading = reloadLayer >= 0 &&
            (animator.GetCurrentAnimatorStateInfo(reloadLayer).IsName("Reload") ||
             animator.IsInTransition(reloadLayer));
        reloading |= firstPersonView.IsReloading;
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
            if (!reloading)
            {
                PlayReload();
            }
            animator.ResetTrigger("Fire");
            return;
        }

        if (reloading || fireLayer < 0)
            return;

        bool wantsToFire = fireMode == FireMode.Automatic
            ? Input.GetMouseButton(0)
            : Input.GetMouseButtonDown(0);

        // Early semi-auto clicks are discarded; full-auto checks again while held.
        if (wantsToFire && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + 60f / Mathf.Max(roundsPerMinute, 1f);

            // Restart the shot immediately instead of leaving an Animator trigger pending.
            PlayFire();
        }
    }

    public void PlayHitReaction()
    {
        animator.CrossFadeInFixedTime("HitReaction", 0.05f, hitReactionLayer, 0f);
    }

    public void PlayFire()
    {
        animator.ResetTrigger("Fire");
        animator.CrossFadeInFixedTime("Fire", 0.05f, fireLayer, 0f);
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
