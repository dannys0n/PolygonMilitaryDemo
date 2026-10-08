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
        public Transform firstPersonMuzzle;
        public Transform thirdPersonMuzzle;
        [System.NonSerialized] public WeaponState state;
    }

    // One runtime state per owned slot, never on the shared weapon definition.
    public class WeaponState
    {
        public int MagazineAmmo { get; private set; }
        public int ReserveAmmo { get; private set; }
        private float nextFireTime;

        public WeaponState(SoldierWeaponDefinition definition)
        {
            MagazineAmmo = definition.magazineCapacity;
            ReserveAmmo = definition.startingReserveAmmo;
        }

        public bool TryConsumeRound(float now, SoldierWeaponDefinition definition)
        {
            if (MagazineAmmo <= 0 || now < nextFireTime) return false;
            MagazineAmmo--;
            nextFireTime = now + 60f / Mathf.Max(definition.roundsPerMinute, 1f);
            return true;
        }

        public bool CanReload(SoldierWeaponDefinition definition) =>
            MagazineAmmo < definition.magazineCapacity && ReserveAmmo > 0;

        public void CompleteReload(SoldierWeaponDefinition definition)
        {
            int count = Mathf.Min(definition.magazineCapacity - MagazineAmmo, ReserveAmmo);
            MagazineAmmo += count;
            ReserveAmmo -= count;
        }
    }

    public SoldierMotionGrip firstPersonGripRig;
    public Transform sprintSocket;
    [Header("Equipment slots on the shared player")]
    [Tooltip("Each slot pairs one weapon configuration with its FPS/TPS objects and moving-parts animators.")]
    public Slot[] slots;
    public SoldierWeaponPresentation presentation;
    public SoldierActions actions;
    public int selectedSlot;
    public bool IsSwitching { get; private set; }
    public SoldierWeapon EquippedWeapon => slots[selectedSlot].weapon;
    public WeaponState EquippedState => slots[selectedSlot].state;
    public Transform EquippedMuzzle => presentation.view.firstPerson
        ? slots[selectedSlot].firstPersonMuzzle : slots[selectedSlot].thirdPersonMuzzle;
    private Coroutine switchRoutine;
    private bool initialized;

    void Awake() => ResetAmmunition();

    public void ResetAmmunition()
    {
        actions.CancelActions();
        foreach (Slot slot in slots) slot.state = new WeaponState(slot.weapon.definition);
    }

    void Start()
    {
        for (int i = 0; i < slots.Length; i++) SetVisible(slots[i], i == selectedSlot);
        Bind(slots[selectedSlot]);
        presentation.view.RefreshVisuals();
        presentation.view.SetFirstPerson(presentation.view.firstPerson);
        initialized = true;
    }

    // Input and future network commands use the same action admission.
    public bool TrySelectWeapon(int index)
    {
        if (index < 0 || index >= slots.Length || index == selectedSlot ||
            IsSwitching || actions.IsReloading) return false;
        switchRoutine = StartCoroutine(SwitchWeapon(index));
        return true;
    }

    IEnumerator SwitchWeapon(int index)
    {
        IsSwitching = true;

        presentation.view.CancelAim();
        Play("Unequip");
        yield return new WaitForSeconds(presentation.weapon.definition.unequipDuration);
        SetVisible(slots[selectedSlot], false);
        selectedSlot = index;
        Bind(slots[index]);
        Play("Equip");
        yield return new WaitForSeconds(presentation.weapon.definition.equipDuration);
        IsSwitching = false;
        switchRoutine = null;
    }

    public bool SetStartingSlot(int index)
    {
        if (index < 0 || index >= slots.Length || IsSwitching || actions.IsReloading) return false;
        if (initialized) SetVisible(slots[selectedSlot], false);
        selectedSlot = index;
        if (initialized)
        {
            Bind(slots[index]);
            presentation.view.CancelAim();
        }
        return true;
    }

    public void CancelSwitch()
    {
        if (!IsSwitching) return;
        StopCoroutine(switchRoutine);
        switchRoutine = null;
        IsSwitching = false;
        Bind(slots[selectedSlot]);
        Play("Empty");
    }

    void OnDisable() => CancelSwitch();

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
        presentation.view.weaponAnimator = slot.weaponAnimator;
        presentation.partsAnimator = slot.partsAnimator;
        presentation.ApplyWeapon(slot.weapon);
        // Changing controllers restores their transform defaults. Apply the
        // selected weapon's grip settings after that reset has completed.
        slot.weapon.firstPersonHands.Apply(firstPersonGripRig.leftHand, firstPersonGripRig.rightHand);
        slot.weapon.thirdPersonHands.Apply(presentation.supportHand, presentation.firingHand);
        slot.weapon.sprintSocket.Apply(sprintSocket);
    }
}
