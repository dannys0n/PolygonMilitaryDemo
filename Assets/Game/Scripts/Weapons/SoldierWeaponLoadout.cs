using System;
using System.Collections;
using UnityEngine;

// Inventory data survives swaps; only the equipped weapon has active presentation.
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
        [NonSerialized] public bool alternateSight;
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
        public bool InfiniteOverride { get; set; }
        public WeaponState(SoldierWeaponDefinition d, int reserveMultiplier=1) { MagazineAmmo=d.magazineCapacity; ReserveAmmo=d.startingReserveAmmo*Mathf.Max(1,reserveMultiplier); }
        public bool TryConsumeRound(float now,SoldierWeaponDefinition d)
        {
            if ((!(d.infiniteAmmunition||InfiniteOverride)&&MagazineAmmo<=0)||now<nextFireTime)return false;
            if(!(d.infiniteAmmunition||InfiniteOverride))MagazineAmmo--;
            nextFireTime=now+60f/Mathf.Max(d.roundsPerMinute,1);return true;
        }
        public bool CanReload(SoldierWeaponDefinition d)=>MagazineAmmo<d.magazineCapacity&&ReserveAmmo>0;
        public void CompleteReload(SoldierWeaponDefinition d){int count=Mathf.Min(d.magazineCapacity-MagazineAmmo,ReserveAmmo);MagazineAmmo+=count;ReserveAmmo-=count;}
    }
    public SoldierMotionGrip firstPersonGripRig;
    public Transform sprintSocket, firstPersonMount, thirdPersonMount;
    public Slot[] slots;
    [Tooltip("Unlimited ammunition for every equipped weapon on this player, including utility weapons.")]
    public bool infiniteAmmunition;
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
    SoldierSharedFirstPersonRig sharedFirstPersonRig;
    GameObject sharedFirstPersonPrefab;
    void Awake()
    {
        var regular = new System.Collections.Generic.List<int>();
        for(int i=0;i<slots.Length;i++){if(!slots[i].definition.isUtility)regular.Add(i);slots[i].state=new WeaponState(slots[i].definition);}
        AvailableSlots=regular.ToArray();
        ResetStates();Spawn();
    }
    void Start(){Bind();presentation.view.RefreshVisuals();presentation.view.SetFirstPerson(presentation.view.firstPerson);}
    void Update(){RefreshFirstPersonPresentation();RefreshLoadedProjectiles();}
    public void RefreshLoadedProjectiles()
    {
        if(!ThirdPersonView)return;
        var definition=slots[selectedSlot].definition;var state=EquippedState;
        bool loaded=(definition.useAuthoredReload && actions.IsReloading) || actions.ReloadHasRound || ((definition.infiniteAmmunition||state.InfiniteOverride)
            ? Time.time>=state.ReadyAt : state.MagazineAmmo>0);
        if(FirstPersonView && FirstPersonView.loadedProjectile)FirstPersonView.loadedProjectile.SetActive(loaded);
        if(ThirdPersonView.loadedProjectile)ThirdPersonView.loadedProjectile.SetActive(loaded);
    }
    public void PlayWeaponSwitch(string state)=>Play(state);
    void Spawn()
    {
        var slot=slots[selectedSlot];
        ThirdPersonView=CreateView(slot.thirdPersonPrefab,thirdPersonMount);
        EquippedWeapon.definition=slot.definition;presentation.weapon=EquippedWeapon;presentation.partsAnimator=ThirdPersonView.animator;
        if(WantsFirstPerson())CreateFirstPerson();
        ConnectViews();
    }
    bool WantsFirstPerson()=>presentation.view.firstPerson&&presentation.view.NeedsCameraAnimation;
    void CreateFirstPerson()
    {
        var prefab=slots[selectedSlot].firstPersonPrefab;
        if(prefab.sharedFirstPersonRig)
        {
            if(!sharedFirstPersonRig || sharedFirstPersonPrefab!=prefab.sharedFirstPersonRig)
            {
                if(sharedFirstPersonRig){sharedFirstPersonRig.gameObject.SetActive(false);Destroy(sharedFirstPersonRig.gameObject);}
                sharedFirstPersonPrefab=prefab.sharedFirstPersonRig;
                sharedFirstPersonRig=Instantiate(sharedFirstPersonPrefab,presentation.view.firstPersonRoot,false).GetComponent<SoldierSharedFirstPersonRig>();
                sharedFirstPersonRig.transform.SetLocalPositionAndRotation(sharedFirstPersonRig.mountPosition,sharedFirstPersonRig.mountRotation);
                sharedFirstPersonRig.transform.localScale=sharedFirstPersonRig.mountScale;
            }
            sharedFirstPersonRig.SetVisible(equippedVisible);
            var mount=string.IsNullOrEmpty(prefab.sharedWeaponMountPath)?sharedFirstPersonRig.weaponMount
                :sharedFirstPersonRig.transform.Find(prefab.sharedWeaponMountPath);
            if(!mount)throw new InvalidOperationException("Missing shared FPS weapon mount '"+prefab.sharedWeaponMountPath+"' for "+prefab.name);
            FirstPersonView=CreateView(prefab,mount);
            FirstPersonView.name="SourceWeapon";
            FirstPersonView.animator=sharedFirstPersonRig.animator;
            sharedFirstPersonRig.Bind(FirstPersonView);
        }
        else
        {
            if(sharedFirstPersonRig)sharedFirstPersonRig.SetVisible(false);
            FirstPersonView=CreateView(prefab,prefab.independentFirstPersonFrame?presentation.view.firstPersonRoot:firstPersonMount);
        }
        presentation.view.weaponAnimator=FirstPersonView.animator;
    }
    static SoldierWeaponView CreateView(SoldierWeaponView prefab,Transform parent)
    {
        var instance=EquipmentVisualPool.Rent(prefab.gameObject,parent).GetComponent<SoldierWeaponView>();instance.name=prefab.mountName;
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
            CreateFirstPerson();FirstPersonView.animator.runtimeAnimatorController=FirstPersonView.animationOverride?FirstPersonView.animationOverride:slots[selectedSlot].definition.firstPersonParts;FirstPersonView.animator.Rebind();
            var source=ThirdPersonView.animator;var target=FirstPersonView.animator;
            foreach(var p in source.parameters)
            {
                if(p.type==AnimatorControllerParameterType.Float)target.SetFloat(p.nameHash,source.GetFloat(p.nameHash));
                else if(p.type==AnimatorControllerParameterType.Bool)target.SetBool(p.nameHash,source.GetBool(p.nameHash));
                else if(p.type==AnimatorControllerParameterType.Int)target.SetInteger(p.nameHash,source.GetInteger(p.nameHash));
            }
            for(int i=0;i<source.layerCount;i++)
            {
                int layer=FirstPersonView.sharedFirstPersonRig?target.GetLayerIndex(source.GetLayerName(i)):i;
                if(layer<0 || layer>=target.layerCount)continue;
                var state=source.GetCurrentAnimatorStateInfo(i);target.SetLayerWeight(layer,source.GetLayerWeight(i));
                if(state.fullPathHash!=0 && target.HasState(layer,state.fullPathHash))target.Play(state.fullPathHash,layer,state.normalizedTime);
            }
            target.Update(0);
        }
        else {ReleaseFirstPerson();presentation.view.weaponAnimator=null;}
        ConnectViews();ApplySelections();
        presentation.view.RefreshVisuals();SetEquippedVisible(equippedVisible);
    }
    void ApplySelections()
    {
        var slot=slots[selectedSlot];EquippedSights.Initialize(presentation.view);
        var sight=slot.selectedSight?slot.selectedSight:EquippedSights.defaultSight;
        if(!EquippedSights.Supports(sight))sight=EquippedSights.BareSight;
        slot.selectedSight=sight;
        EquippedSights.SetSight(sight);
        EquippedSights.SetAlternateSight(slot.alternateSight);
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
    static void ReturnView(SoldierWeaponView view){if(!view)return;EquipmentVisualPool.Return(view.gameObject);}
    void ReleaseFirstPerson()
    {
        if(sharedFirstPersonRig){sharedFirstPersonRig.Bind(null);sharedFirstPersonRig.SetVisible(false);}
        ReturnView(FirstPersonView);FirstPersonView=null;
    }
    void Release()
    {
        PresentationChanging?.Invoke();EquippedSights.ClearPresentation();EquippedAttachments.ClearPresentation();
        ReleaseFirstPerson();ReturnView(ThirdPersonView);ThirdPersonView=null;
    }
    public void ConfigureAvailableSlots(int[] indices)
    {
        if(indices.Length==0)throw new ArgumentException("Loadout needs a slot.");
        for(int i=0;i<indices.Length;i++)if(indices[i]<0||indices[i]>=slots.Length||Array.IndexOf(indices,indices[i])!=i)throw new ArgumentException("Invalid loadout slots.");
        actions.CancelActions();AvailableSlots=(int[])indices.Clone();SetStartingSlot(indices[0]);
    }
    // Replace a runtime equipment slot without rebuilding the player or preloading a catalog.
    public void ReplaceEquipped(Slot slot)
    {
        if(slot==null||!slot.definition||!slot.firstPersonPrefab||!slot.thirdPersonPrefab)throw new ArgumentException("A complete equipment slot is required.");
        actions.CancelActions();CancelSwitch();Release();
        slots=new[]{slot};selectedSlot=0;AvailableSlots=new[]{0};ResetStates();Spawn();Bind();EquipCurrentWeapon();
    }
    public void ResetAmmunition(){actions.CancelActions();ResetStates();}
    void ResetStates()
    {
        int primary=AvailableSlots!=null&&AvailableSlots.Length>0?AvailableSlots[0]:-1;
        for(int i=0;i<slots.Length;i++)slots[i].state=new WeaponState(slots[i].definition,i==primary&&!slots[i].definition.isUtility?3:1) { InfiniteOverride=infiniteAmmunition };
    }
    // Per-life supply override; definitions remain shared and unchanged.
    public void SetInfiniteUtilityAmmo(bool enabled)
    {
        foreach(var slot in slots)if(slot.definition.isUtility&&slot.definition.rocketProjectile)slot.state.InfiniteOverride=infiniteAmmunition||enabled;
    }
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
    bool CanChangeSight => enabled && actions.enabled && !IsSwitching && !actions.IsReloading && !actions.IsThrowing;
    public bool TryToggleSightMode()
    {
        if(!CanChangeSight || !EquippedSights.HasAlternateSight)return false;
        bool changed=EquippedSights.SetAlternateSight(!EquippedSights.AlternateSightActive);
        if(changed)slots[selectedSlot].alternateSight=EquippedSights.AlternateSightActive;
        return changed;
    }
    public bool TryCycleSight(){if(!CanChangeSight)return false;EquippedSights.Cycle();slots[selectedSlot].selectedSight=EquippedSights.SelectedSight;slots[selectedSlot].alternateSight=false;return true;}
    public void SetSight(WeaponSightDefinition sight){EquippedSights.SetSight(sight);slots[selectedSlot].selectedSight=EquippedSights.SelectedSight;slots[selectedSlot].alternateSight=EquippedSights.AlternateSightActive;}
    public void SetAttachments(WeaponAttachmentDefinition[] attachments){slots[selectedSlot].selectedAttachments=(WeaponAttachmentDefinition[])attachments.Clone();EquippedAttachments.Apply(attachments,presentation.view);}
    public void SetClassSights(WeaponSightDefinition primary,WeaponSightDefinition secondary){for(int i=0;i<AvailableSlots.Length;i++){var slot=slots[AvailableSlots[i]];var selected=i==0?primary:secondary;slot.selectedSight=selected?selected:slot.thirdPersonPrefab.GetComponent<SoldierWeaponSights>().BareSight;slot.alternateSight=false;}ApplySelections();}
    public void SetClassAttachments(WeaponAttachmentDefinition[] primary,WeaponAttachmentDefinition[] secondary){for(int i=0;i<AvailableSlots.Length;i++)slots[AvailableSlots[i]].selectedAttachments=(WeaponAttachmentDefinition[])(i==0?primary:secondary).Clone();ApplySelections();}
    public void SetEquippedVisible(bool visible)
    {
        equippedVisible=visible;
        if(FirstPersonView)FirstPersonView.gameObject.SetActive(visible);
        if(sharedFirstPersonRig)sharedFirstPersonRig.SetVisible(visible && FirstPersonView && FirstPersonView.sharedFirstPersonRig);
        if(ThirdPersonView)ThirdPersonView.gameObject.SetActive(visible);
    }
    public void EquipCurrentWeapon(){switchRoutine=StartCoroutine(EquipCurrent());}
    IEnumerator EquipCurrent(){IsSwitching=true;presentation.view.CancelAim();SetEquippedVisible(true);Play("Equip");yield return new WaitForSeconds(EquippedWeapon.definition.equipDuration);IsSwitching=false;switchRoutine=null;}
    public void CancelSwitch(){if(!IsSwitching)return;StopCoroutine(switchRoutine);switchRoutine=null;IsSwitching=false;SetEquippedVisible(true);Play("Empty");}
    void Play(string state)
    {
        var arms=presentation.view.armsAnimator;var body=presentation.characterAnimator;
        var definition=slots[selectedSlot].definition;
        float duration=state=="Equip"?definition.equipDuration:definition.unequipDuration;
        arms.SetFloat("WeaponSwitchSpeed",state=="Empty"?1f:WeaponAnimationTiming.Speed(arms,state+"_FirstPerson",duration));
        body.SetFloat("WeaponSwitchSpeed",state=="Empty"?1f:WeaponAnimationTiming.Speed(body,state+"_ThirdPerson",duration));
        arms.Play(state,arms.GetLayerIndex("WeaponSwitch"),0);
        body.Play(state,body.GetLayerIndex("WeaponSwitch"),0);
        if(FirstPersonView && FirstPersonView.animationOverride)
        {
            var authored=FirstPersonView.animator;
            authored.SetFloat("WeaponSwitchSpeed",state=="Empty"?1f:WeaponAnimationTiming.Speed(authored,state+"_FirstPerson",duration));
            int layer=authored.GetLayerIndex("WeaponSwitch");
            authored.Play(layer>=0?state:state=="Empty"?"Idle":state,Mathf.Max(0,layer),0);
        }
    }
    void OnDisable()=>CancelSwitch();
    void OnDestroy(){if(sharedFirstPersonRig)Destroy(sharedFirstPersonRig.gameObject);}
}
