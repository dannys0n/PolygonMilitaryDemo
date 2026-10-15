using UnityEngine;

[DefaultExecutionOrder(-20)]
public class SoldierController : MonoBehaviour
{
    [Tooltip("Optional. Without a source the character idles and still simulates gravity.")]
    public SoldierInputSource inputSource;
    public SoldierMovement movement;
    public SoldierLook look;
    public SoldierActions actions;
    public SoldierGrenades grenades;
    public SoldierUtilitySlots launchers;
    public SoldierWeaponLoadout loadout;
    public SoldierFirstPersonView view;

    void Update()
    {
        var input = inputSource && inputSource.isActiveAndEnabled ? inputSource.ReadInput() : default;
        SimulateInput(input, Time.deltaTime);
    }

    public void SimulateInput(SoldierInputFrame input, float deltaTime)
    {
        if (!actions.enabled)
        {
            view.SetAimRequested(false);
            return;
        }

        if (launchers && input.utilitySelection>0) launchers.TryEquip(input.utilitySelection-1);
        if (launchers && input.grenadeThrow) launchers.Unequip();
        if (input.cycleSight && !loadout.TryToggleSightMode()) loadout.TryCycleSight();
        if(input.grenadeCycle)grenades.Cycle();
        if(input.grenadeThrow)grenades.TryThrow();
        view.SetAimRequested(input.aim && !grenades.IsThrowing);
        look.ApplyLookDelta(input.lookDelta);
        if (input.jump) movement.RequestJump();
        movement.SimulateMovement(input.move, deltaTime, input.sprint && !grenades.IsThrowing);

        if (input.toggleView) view.SetFirstPerson(!view.firstPerson);
        if (input.previewHitReaction) actions.PlayHitReaction();
        if (input.reload) actions.TryReload();
        else if (actions.fireMode == SoldierActions.FireMode.Automatic ? input.fireHeld : input.firePressed)
            actions.TryFire();

        int[] slots = loadout.AvailableSlots;
        if (input.weaponSelection > 0 && input.weaponSelection <= slots.Length)
            loadout.TrySelectWeapon(slots[input.weaponSelection - 1]);
        if (input.weaponCycle != 0)
        {
            int current = System.Array.IndexOf(slots, loadout.selectedSlot);
            int next = (current + (input.weaponCycle > 0 ? 1 : slots.Length - 1)) % slots.Length;
            loadout.TrySelectWeapon(slots[next]);
        }
    }

    void OnDisable()
    {
        view.SetAimRequested(false);
        movement.ResetMovement();
    }
}
