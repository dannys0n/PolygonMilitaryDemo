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
    public SoldierPhysicsBody controller;
    public event System.Action Respawned;
    public event System.Action Respawning;
    VehicleOccupant vehicleOccupant;
    public const float HumanHealth = 300f;
    public const float AIHealth = 150f;
    void Awake()
    {
        vehicleOccupant=GetComponent<VehicleOccupant>();
        ResetInfantryHealth();
    }
    void ResetInfantryHealth()
    {
        var input=GetComponent<SoldierController>();
        if(input && input.inputSource)
            health.maxHealth=input.inputSource is SoldierLocalInput?HumanHealth:AIHealth;
        health.ResetHealth();
    }

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
        // A scene can seat its opening crew before their first Start. Mounted pose
        // and collision ownership must survive this initialization (and later calls).
        if (vehicleOccupant && vehicleOccupant.IsMounted)
        {
            controller.enabled=false;
            actions.enabled=movement.enabled=look.enabled=loadout.enabled=false;
            return;
        }
        if (!allowed)
        {
            actions.CancelActions();
            movement.ResetMovement();
            view.SetAimRequested(false);
        }
        controller.enabled=allowed;
        actions.enabled = movement.enabled = look.enabled = loadout.enabled = allowed;
    }

    public void Respawn(Vector3 position, Quaternion rotation)
    {
        Respawning?.Invoke();
        SetGameplayAllowed(false);
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        movement.ResetMovement();
        look.ResetLook();
        loadout.ResetAmmunition();
        view.CancelReload();
        ResetInfantryHealth();
        controller.enabled = true;
        SetGameplayAllowed(true);
        Respawned?.Invoke();
    }
}
