using System.Collections;
using UnityEngine;

// Body and arms stay alive. Slots contain only equipment and its grip settings.
public class SoldierWeaponLoadout : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public string name;
        public SoldierWeapon weapon;
        public Transform firstPersonWeapon;
        public Transform thirdPersonWeapon;
        public Animator weaponAnimator;
        public Animator partsAnimator;
    }

    public SoldierMotionGrip firstPersonGripRig;
    public Transform sprintSocket;
    public Slot[] slots;
    public SoldierWeaponPresentation presentation;
    public SoldierActions actions;
    public int selectedSlot;
    public bool IsSwitching { get; private set; }

    void Start()
    {
        for (int i = 0; i < slots.Length; i++) SetVisible(slots[i], i == selectedSlot);
        Bind(slots[selectedSlot]);
        presentation.view.RefreshVisuals();
        presentation.view.SetFirstPerson(presentation.view.firstPerson);
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

    // Input and future network commands use the same action admission.
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
        Play("Unequip");
        yield return new WaitForSeconds(presentation.weapon.definition.unequipDuration);
        SetVisible(slots[selectedSlot], false);
        selectedSlot = index;
        Bind(slots[index]);
        Play("Equip");
        yield return new WaitForSeconds(presentation.weapon.definition.equipDuration);
        IsSwitching = false;
        presentation.view.IsEquipping = false;
    }

    void Play(string state)
    {
        var arms = presentation.view.armsAnimator;
        arms.Play(state, arms.GetLayerIndex("WeaponSwitch"), 0f);
        var body = presentation.characterAnimator;
        body.Play(state, body.GetLayerIndex("WeaponSwitch"), 0f);
    }

    static void SetVisible(Slot slot, bool visible)
    {
        slot.firstPersonWeapon.gameObject.SetActive(visible);
        slot.thirdPersonWeapon.gameObject.SetActive(visible);
    }

    void Bind(Slot slot)
    {
        SetVisible(slot, true);
        slot.weapon.firstPersonHands.Apply(firstPersonGripRig.leftHand, firstPersonGripRig.rightHand);
        slot.weapon.thirdPersonHands.Apply(presentation.supportHand, presentation.firingHand);
        slot.weapon.sprintSocket.Apply(sprintSocket);
        presentation.view.weaponAnimator = slot.weaponAnimator;
        presentation.partsAnimator = slot.partsAnimator;
        presentation.ApplyWeapon(slot.weapon);
    }
}
