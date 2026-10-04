using UnityEngine;

public class SoldierActions : MonoBehaviour
{
    public enum FireMode
    {
        SemiAutomatic,
        Automatic
    }

    public Animator animator;
    public FireMode fireMode = FireMode.SemiAutomatic;
    [Min(1f)] public float roundsPerMinute = 600f;

    private float nextFireTime;
    private int fireLayer;
    private int reloadLayer;

    void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null)
        {
            fireLayer = animator.GetLayerIndex("Fire");
            reloadLayer = animator.GetLayerIndex("Reload");
            animator.ResetTrigger("Fire");
        }
    }

    void Update()
    {
        if (animator == null || Cursor.lockState != CursorLockMode.Locked)
            return;

        bool reloading = reloadLayer >= 0 &&
            (animator.GetCurrentAnimatorStateInfo(reloadLayer).IsName("Reload") ||
             animator.IsInTransition(reloadLayer));

        if (Input.GetKeyDown(KeyCode.R))
        {
            if (!reloading)
                animator.SetTrigger("Reload");
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
            animator.ResetTrigger("Fire");
            animator.CrossFadeInFixedTime("Fire", 0.05f, fireLayer, 0f);
        }
    }
}
