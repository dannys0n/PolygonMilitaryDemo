using UnityEngine;

// Shared character lifecycle. Game modes decide when and where to respawn.
[DefaultExecutionOrder(10)]
public class SoldierLife : MonoBehaviour
{
    public SoldierHealth health;
    public SoldierActions actions;
    public SoldierMovement movement;
    public SoldierLook look;
    public SoldierWeaponLoadout loadout;
    public SoldierFirstPersonView view;
    public CharacterController controller;
    public event System.Action Respawned;

    void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    void Start() => SetGameplayAllowed(!health.IsDead);

    void OnDamaged(float amount, GameObject source)
    {
        if (!health.IsDead) actions.PlayHitReaction();
    }

    void OnDied(GameObject source) => SetGameplayAllowed(false);

    public void SetGameplayAllowed(bool allowed)
    {
        allowed &= !health.IsDead;
        if (!allowed)
        {
            actions.CancelActions();
            movement.ResetMovement();
            view.SetAimRequested(false);
        }
        actions.enabled = movement.enabled = look.enabled = loadout.enabled = allowed;
    }

    public void Respawn(Vector3 position, Quaternion rotation)
    {
        SetGameplayAllowed(false);
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        movement.ResetMovement();
        look.ResetLook();
        loadout.ResetAmmunition();
        view.CancelReload();
        health.ResetHealth();
        controller.enabled = true;
        SetGameplayAllowed(true);
        Respawned?.Invoke();
    }
}
