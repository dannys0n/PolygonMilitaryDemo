using System;
using System.Collections;
using UnityEngine;

// Inventory data survives swaps; only the equipped weapon has scene objects.
[DefaultExecutionOrder(-20)]
public class SoldierWeaponLoadout : MonoBehaviour
{
    [Serializable] public class Slot
    {
        public string name;
        public SoldierWeaponDefinition definition;
        public SoldierWeaponView firstPersonPrefab, thirdPersonPrefab;
        [NonSerialized] public WeaponState state;
        [NonSerialized] public WeaponSightDefinition selectedSight;
        [NonSerialized] public WeaponAttachmentDefinition[] selectedAttachments = Array.Empty<WeaponAttachmentDefinition>();
        public bool Supports(WeaponSightDefinition sight) => thirdPersonPrefab.GetComponent<SoldierWeaponSights>().Supports(sight);
        public bool Supports(WeaponAttachmentDefinition attachment) => thirdPersonPrefab.GetComponent<SoldierWeaponAttachments>().Supports(attachment);
    }
    public class WeaponState
    {
        public int MagazineAmmo { get; private set; }
        public int ReserveAmmo { get; private set; }
        float nextFireTime;
        public float ReadyAt => nextFireTime;
        public WeaponState(SoldierWeaponDefinition d) { MagazineAmmo=d.magazineCapacity; ReserveAmmo=d.startingReserveAmmo; }
        public bool TryConsumeRound(float now,SoldierWeaponDefinition d)
        {
            if ((!d.infiniteAmmunition&&MagazineAmmo<=0)||now<nextFireTime)return false;
            if(!d.infiniteAmmunition)MagazineAmmo--;
            nextFireTime=now+60f/Mathf.Max(d.roundsPerMinute,1);return true;
        }
        public bool CanReload(SoldierWeaponDefinition d)=>MagazineAmmo<d.magazineCapacity&&ReserveAmmo>0;
        public void CompleteReload(SoldierWeaponDefinition d){int count=Mathf.Min(d.magazineCapacity-MagazineAmmo,ReserveAmmo);MagazineAmmo+=count;ReserveAmmo-=count;}
    }
    public SoldierMotionGrip firstPersonGripRig;
    public Transform sprintSocket, firstPersonMount, thirdPersonMount;
    public Slot[] slots;
    public SoldierWeaponPresentation presentation;
    public SoldierActions actions;
    public int selectedSlot;
    public bool IsSwitching { get; private set; }
    public int[] AvailableSlots { get; private set; }
    public SoldierWeaponView FirstPersonView { get; private set; }
    public SoldierWeaponView ThirdPersonView { get; private set; }
    public bool HasFirstPersonPresentation => FirstPersonView;
    public SoldierWeapon EquippedWeapon => ThirdPersonView.GetComponent<SoldierWeapon>();
    public SoldierWeaponSights EquippedSights => ThirdPersonView.GetComponent<SoldierWeaponSights>();
    public SoldierWeaponAttachments EquippedAttachments => ThirdPersonView.GetComponent<SoldierWeaponAttachments>();
    public WeaponState EquippedState => slots[selectedSlot].state;
    public Transform EquippedMuzzle => HasFirstPersonPresentation && presentation.view.firstPerson
        ? FirstPersonView.effects.muzzleFlash : ThirdPersonView.effects.muzzleFlash;
    public event Action PresentationChanging;
    Coroutine switchRoutine;
    bool equippedVisible=true;
    void Awake()
    {
        var regular = new System.Collections.Generic.List<int>();
        for(int i=0;i<slots.Length;i++){if(!slots[i].definition.isUtility)regular.Add(i);slots[i].state=new WeaponState(slots[i].definition);}
        AvailableSlots=regular.ToArray();
        Spawn();
    }
    void Start(){Bind();presentation.view.RefreshVisuals();presentation.view.SetFirstPerson(presentation.view.firstPerson);}
    void Update()=>RefreshFirstPersonPresentation();
    void Spawn()
    {
        var slot=slots[selectedSlot];
        ThirdPersonView=CreateView(slot.thirdPersonPrefab,thirdPersonMount);
        EquippedWeapon.definition=slot.definition;presentation.weapon=EquippedWeapon;presentation.partsAnimator=ThirdPersonView.animator;
        if(WantsFirstPerson())CreateFirstPerson();
        ConnectViews();
    }
    bool WantsFirstPerson()=>presentation.view.firstPerson&&presentation.view.playerCamera.isActiveAndEnabled;
    void CreateFirstPerson()
    {
        var prefab=slots[selectedSlot].firstPersonPrefab;
        FirstPersonView=CreateView(prefab,firstPersonMount);
        presentation.view.weaponAnimator=FirstPersonView.animator;
    }
    static SoldierWeaponView CreateView(SoldierWeaponView prefab,Transform parent)
    {
        var instance=Instantiate(prefab,parent,false);instance.name=prefab.mountName;
        foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var body in instance.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.detectCollisions=false;}
        instance.transform.SetLocalPositionAndRotation(prefab.mountPosition,prefab.mountRotation);
        instance.transform.localScale=prefab.mountScale;return instance;
    }
    void ConnectViews()
    {
        var sights=EquippedSights;
        sights.firstPersonMount=FirstPersonView?FirstPersonView.opticMount:null;
        sights.thirdPersonMount=ThirdPersonView.opticMount;
        sights.rearSight=FirstPersonView?FirstPersonView.rearSight:ThirdPersonView.rearSight;
        sights.frontSight=FirstPersonView?FirstPersonView.frontSight:ThirdPersonView.frontSight;
        foreach(var m in EquippedAttachments.mounts)
        {
            var fp=FirstPersonView?Array.Find(FirstPersonView.attachments,s=>s.kind==m.kind):null;
            m.firstPerson=fp?.socket;m.builtInFirstPerson=fp?.builtIn??Array.Empty<Renderer>();
        }
    }
    public void RefreshFirstPersonPresentation()
    {
        if(!ThirdPersonView)return;
        bool wanted=WantsFirstPerson();if(wanted==HasFirstPersonPresentation)return;
        PresentationChanging?.Invoke();
        EquippedSights.ClearPresentation();
        if(wanted)
        {
            CreateFirstPerson();FirstPersonView.animator.runtimeAnimatorController=slots[selectedSlot].definition.firstPersonParts;FirstPersonView.animator.Rebind();
            var source=ThirdPersonView.animator;var target=FirstPersonView.animator;
            foreach(var p in source.parameters)
            {
                if(p.type==AnimatorControllerParameterType.Float)target.SetFloat(p.nameHash,source.GetFloat(p.nameHash));
                else if(p.type==AnimatorControllerParameterType.Bool)target.SetBool(p.nameHash,source.GetBool(p.nameHash));
                else if(p.type==AnimatorControllerParameterType.Int)target.SetInteger(p.nameHash,source.GetInteger(p.nameHash));
            }
            for(int i=0;i<source.layerCount;i++){var state=source.GetCurrentAnimatorStateInfo(i);target.SetLayerWeight(i,source.GetLayerWeight(i));if(state.fullPathHash!=0)target.Play(state.fullPathHash,i,state.normalizedTime);}
            target.Update(0);
        }
        else {DestroyView(FirstPersonView);FirstPersonView=null;presentation.view.weaponAnimator=null;}
        ConnectViews();ApplySelections();
        presentation.view.RefreshVisuals();SetEquippedVisible(equippedVisible);
    }
    void ApplySelections()
    {
        var slot=slots[selectedSlot];EquippedSights.Initialize(presentation.view);
        EquippedSights.SetSight(slot.selectedSight?slot.selectedSight:EquippedSights.defaultSight);
        EquippedAttachments.Apply(slot.selectedAttachments,presentation.view);
    }
    void Bind()
    {
        EquippedWeapon.definition=slots[selectedSlot].definition;
        presentation.ApplyWeapon(EquippedWeapon);
        EquippedWeapon.firstPersonHands.Apply(firstPersonGripRig.leftHand,firstPersonGripRig.rightHand);
        EquippedWeapon.thirdPersonHands.Apply(presentation.supportHand,presentation.firingHand);
        EquippedWeapon.sprintSocket.Apply(sprintSocket);ApplySelections();
    }
    static void DestroyView(SoldierWeaponView view){if(!view)return;view.gameObject.SetActive(false);view.transform.SetParent(null);Destroy(view.gameObject);}
    void Release()
    {
        PresentationChanging?.Invoke();EquippedSights.ClearPresentation();
        DestroyView(FirstPersonView);DestroyView(ThirdPersonView);FirstPersonView=null;ThirdPersonView=null;
    }
    public void ConfigureAvailableSlots(int[] indices)
    {
        if(indices.Length==0)throw new ArgumentException("Loadout needs a slot.");
        for(int i=0;i<indices.Length;i++)if(indices[i]<0||indices[i]>=slots.Length||Array.IndexOf(indices,indices[i])!=i)throw new ArgumentException("Invalid loadout slots.");
        actions.CancelActions();AvailableSlots=(int[])indices.Clone();SetStartingSlot(indices[0]);
    }
    public void ResetAmmunition(){actions.CancelActions();foreach(var s in slots)s.state=new WeaponState(s.definition);}
    public bool TrySelectWeapon(int index)
    {
        if(index<0||index>=slots.Length)return false;
        if(slots[index].definition.isUtility)
        {
            var utility=GetComponent<SoldierUtilitySlots>();
            if(!utility)return false;
            int equipment=Array.IndexOf(utility.utilityWeapons,slots[index].definition);
            if(equipment<0){if(!utility.SetUtility(0,slots[index].definition))return false;equipment=0;}
            return utility.TryEquip(equipment);
        }
        return TrySwitch(index,false);
    }
    public bool TrySelectUtilityWeapon(int index)=>TrySwitch(index,true);
    bool TrySwitch(int index,bool utility)
    {
        if(!enabled||index<0||index>=slots.Length||index==selectedSlot||IsSwitching||actions.IsReloading||actions.IsThrowing)return false;
        if(utility && !slots[index].definition.isUtility)return false;
        if(!utility && Array.IndexOf(AvailableSlots,index)<0)return false;
        switchRoutine=StartCoroutine(SwitchWeapon(index));return true;
    }
    IEnumerator SwitchWeapon(int index)
    {
        IsSwitching=true;presentation.view.CancelAim();Play("Unequip");
        yield return new WaitForSeconds(EquippedWeapon.definition.unequipDuration);
        Release();selectedSlot=index;Spawn();Bind();Play("Equip");
        yield return new WaitForSeconds(EquippedWeapon.definition.equipDuration);
        IsSwitching=false;switchRoutine=null;
    }
    public bool SetStartingSlot(int index)
    {
        if(index<0||index>=slots.Length||IsSwitching||actions.IsReloading||actions.IsThrowing)return false;
        if(index!=selectedSlot){Release();selectedSlot=index;Spawn();}Bind();presentation.view.CancelAim();return true;
    }
    public bool TryCycleSight(){if(!enabled||!actions.enabled||IsSwitching||actions.IsReloading||actions.IsThrowing)return false;EquippedSights.Cycle();slots[selectedSlot].selectedSight=EquippedSights.SelectedSight;return true;}
    public void SetSight(WeaponSightDefinition sight){EquippedSights.SetSight(sight);slots[selectedSlot].selectedSight=EquippedSights.SelectedSight;}
    public void SetAttachments(WeaponAttachmentDefinition[] attachments){slots[selectedSlot].selectedAttachments=(WeaponAttachmentDefinition[])attachments.Clone();EquippedAttachments.Apply(attachments,presentation.view);}
    public void SetClassSights(WeaponSightDefinition primary,WeaponSightDefinition secondary){for(int i=0;i<AvailableSlots.Length;i++)slots[AvailableSlots[i]].selectedSight=i==0?primary:secondary;ApplySelections();}
    public void SetClassAttachments(WeaponAttachmentDefinition[] primary,WeaponAttachmentDefinition[] secondary){for(int i=0;i<AvailableSlots.Length;i++)slots[AvailableSlots[i]].selectedAttachments=(WeaponAttachmentDefinition[])(i==0?primary:secondary).Clone();ApplySelections();}
    public void SetEquippedVisible(bool visible){equippedVisible=visible;if(FirstPersonView)FirstPersonView.gameObject.SetActive(visible);if(ThirdPersonView)ThirdPersonView.gameObject.SetActive(visible);}
    public void EquipCurrentWeapon(){switchRoutine=StartCoroutine(EquipCurrent());}
    IEnumerator EquipCurrent(){IsSwitching=true;presentation.view.CancelAim();SetEquippedVisible(true);Play("Equip");yield return new WaitForSeconds(EquippedWeapon.definition.equipDuration);IsSwitching=false;switchRoutine=null;}
    public void CancelSwitch(){if(!IsSwitching)return;StopCoroutine(switchRoutine);switchRoutine=null;IsSwitching=false;SetEquippedVisible(true);Play("Empty");}
    void Play(string state){var arms=presentation.view.armsAnimator;arms.Play(state,arms.GetLayerIndex("WeaponSwitch"),0);var body=presentation.characterAnimator;body.Play(state,body.GetLayerIndex("WeaponSwitch"),0);}
    void OnDisable()=>CancelSwitch();
}
