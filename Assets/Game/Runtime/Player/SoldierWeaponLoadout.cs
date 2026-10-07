using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// Each slot keeps the weapon's paired visual rigs and configuration together.
// The player, camera, movement, and action components remain on one root.
public class SoldierWeaponLoadout : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public string name;
        public SoldierWeapon weapon;
        public Transform firstPersonRoot;
        public Transform thirdPersonRoot;
        public Animator armsAnimator;
        public Animator weaponAnimator;
        public Animator characterAnimator;
        public Animator partsAnimator;
        public Transform aimPivot;
        public Transform weaponRoot;
        public MultiAimConstraint chestAim;
        public TwoBoneIKConstraint supportHand;
        public TwoBoneIKConstraint firingHand;
        public MultiParentConstraint runWeaponFollow;
        public Transform animatedCamera;
    }

    public Slot[] slots;
    public SoldierWeaponPresentation presentation;
    public SoldierFirstPersonCamera cameraMotion;
    public SoldierLook look;
    public SoldierMovement movement;
    public SoldierActions actions;
    public int selectedSlot;
    public bool IsSwitching { get; private set; }

    void Start()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].firstPersonRoot.gameObject.SetActive(i == selectedSlot);
            slots[i].thirdPersonRoot.gameObject.SetActive(i == selectedSlot);
        }
        Bind(slots[selectedSlot]);
    }

    void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        for (int i = 0; i < Mathf.Min(slots.Length, 9); i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) TrySelectWeapon(i);
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel != 0f)
            TrySelectWeapon((selectedSlot + (wheel > 0f ? 1 : slots.Length - 1)) % slots.Length);
    }

    // Call this from an input or network command driver instead of duplicating swapping logic.
    public bool TrySelectWeapon(int index)
    {
        if (index < 0 || index >= slots.Length || index == selectedSlot ||
            IsSwitching || actions.IsReloading) return false;
        StartCoroutine(SwitchWeapon(index));
        return true;
    }

    IEnumerator SwitchWeapon(int index)
    {
        IsSwitching = true;
        presentation.view.IsEquipping = true;
        presentation.view.CancelAim();
        SoldierWeaponDefinition old = presentation.weapon.definition;
        Play("Unequip");
        yield return new WaitForSeconds(old.unequipDuration);
        slots[selectedSlot].firstPersonRoot.gameObject.SetActive(false);
        slots[selectedSlot].thirdPersonRoot.gameObject.SetActive(false);
        selectedSlot = index;
        Bind(slots[index]);
        Play("Equip");
        yield return new WaitForSeconds(presentation.weapon.definition.equipDuration);
        IsSwitching = false;
        presentation.view.IsEquipping = false;
    }

    void Play(string state)
    {
        var view = presentation.view;
        view.armsAnimator.Play(state, view.armsAnimator.GetLayerIndex("WeaponSwitch"), 0f);
        presentation.characterAnimator.Play(state,
            presentation.characterAnimator.GetLayerIndex("WeaponSwitch"), 0f);
    }

    void Bind(Slot slot)
    {
        slot.firstPersonRoot.gameObject.SetActive(true);
        slot.thirdPersonRoot.gameObject.SetActive(true);
        var view = presentation.view;
        view.firstPersonRoot = slot.firstPersonRoot;
        view.thirdPersonRoot = slot.thirdPersonRoot;
        view.armsAnimator = slot.armsAnimator;
        view.weaponAnimator = slot.weaponAnimator;
        presentation.characterAnimator = slot.characterAnimator;
        presentation.partsAnimator = slot.partsAnimator;
        presentation.aimPivot = slot.aimPivot;
        presentation.weaponRoot = slot.weaponRoot;
        presentation.chestAim = slot.chestAim;
        presentation.supportHand = slot.supportHand;
        presentation.firingHand = slot.firingHand;
        presentation.runWeaponFollow = slot.runWeaponFollow;
        cameraMotion.animatedCamera = slot.animatedCamera;
        look.animator = movement.animator = actions.animator = slot.characterAnimator;
        view.RefreshVisuals();
        presentation.ApplyWeapon(slot.weapon);
        view.SetFirstPerson(view.firstPerson);
    }
}
