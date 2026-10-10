using UnityEngine;

// Utility ammunition belongs to the soldier; shared definitions contain only configuration.
[DefaultExecutionOrder(120)]
public class SoldierLaunchers : MonoBehaviour
{
    public InfantryLauncherDefinition[] types;
    public SoldierLife life;
    public bool IsEquipped { get; private set; }
    public int Selected { get; private set; }
    public InfantryLauncherDefinition Equipped => types[Selected];
    public bool Ready => Time.time>=readyAt[Selected];
    public event System.Action Fired;
    int[] rounds;
    float[] readyAt;
    int previousSlot;
    InfantryLauncherVisual first,third;
    Renderer[] hiddenFirst,hiddenThird;
    public Sprite LastShotIcon { get; private set; }
    public float LastShotTime { get; private set; }=-100;

    void Awake(){rounds=new int[types.Length];readyAt=new float[types.Length];ResetEquipment();}
    void OnEnable()=>life.Respawned+=ResetEquipment;
    void OnDisable(){life.Respawned-=ResetEquipment;Unequip();}
    public int Count(int i)=>rounds[i];
    void ResetEquipment(){Unequip();for(int i=0;i<types.Length;i++){rounds[i]=types[i].startingRounds;readyAt[i]=0;}}

    public bool TryEquip(int index)
    {
        if(index<0||index>=types.Length||!life.actions.enabled||life.health.IsDead||life.actions.IsThrowing)return false;
        if(IsEquipped && Selected==index)return true;
        life.actions.CancelReload();
        life.loadout.CancelSwitch();
        if(IsEquipped)Unequip();
        previousSlot=life.loadout.selectedSlot;
        // Use the class's rifle hold as a deliberately basic launcher pose.
        life.loadout.SetStartingSlot(life.loadout.AvailableSlots[0]);
        Selected=index;life.view.CancelAim();life.view.SetAimRequested(false);
        var slot=life.loadout.slots[life.loadout.selectedSlot];
        hiddenFirst=slot.firstPersonWeapon.GetComponentsInChildren<Renderer>(true);
        hiddenThird=slot.thirdPersonWeapon.GetComponentsInChildren<Renderer>(true);
        first=Held(slot.firstPersonSockets.muzzleFlash,life.view.firstPersonLayer);
        third=Held(slot.thirdPersonSockets.muzzleFlash,life.view.thirdPersonLayer);
        IsEquipped=true;life.view.RefreshVisuals();UpdateVisibility();
        return true;
    }
    InfantryLauncherVisual Held(Transform parent,int layer)
    {
        var model=Instantiate(Equipped.model,parent,false);
        model.transform.localScale=Vector3.one;
        // Imported rig bones may be scaled; preserve the launcher's world dimensions.
        var scale=parent.lossyScale;model.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
        model.transform.position=parent.TransformPoint(Vector3.zero)+parent.rotation*Equipped.holdPosition;
        model.transform.rotation=parent.rotation*Quaternion.Euler(Equipped.holdRotation);
        foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
        return model;
    }
    public void Unequip()
    {
        if(!IsEquipped)return;
        IsEquipped=false;
        // Detach before refreshing visibility; Destroy is deferred until the frame ends.
        first.gameObject.SetActive(false);third.gameObject.SetActive(false);
        first.transform.SetParent(null);third.transform.SetParent(null);
        Destroy(first.gameObject);Destroy(third.gameObject);
        foreach(var r in hiddenFirst){r.forceRenderingOff=false;r.enabled=life.view.firstPerson;}
        foreach(var r in hiddenThird){r.forceRenderingOff=false;r.enabled=true;}
        life.loadout.SetStartingSlot(previousSlot);
        life.view.RefreshVisuals();
    }
    void LateUpdate(){if(IsEquipped)UpdateVisibility();}
    void UpdateVisibility()
    {
        // Animation clips can write Renderer.enabled; this visibility override survives that.
        foreach(var r in hiddenFirst)r.forceRenderingOff=true;
        foreach(var r in hiddenThird)r.forceRenderingOff=true;
        foreach(var r in first.GetComponentsInChildren<Renderer>(true))r.enabled=life.view.firstPerson;
        if(first.loadedRocket)first.loadedRocket.SetActive(Ready&&rounds[Selected]>0);
        if(third.loadedRocket)third.loadedRocket.SetActive(Ready&&rounds[Selected]>0);
    }
    public bool TryFire()
    {
        if(!IsEquipped||!Ready||rounds[Selected]==0||!life.actions.enabled||life.movement.IsRunning)return false;
        var camera=life.look.aimCamera;
        Ray ray=camera.ViewportPointToRay(new Vector3(.5f,.5f,0));
        Vector3 target=ray.GetPoint(500);
        float nearest=500;
        foreach(var hit in Physics.RaycastAll(ray,500,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform)&&hit.distance<nearest){nearest=hit.distance;target=hit.point;}
        Transform muzzle=(life.view.firstPerson?first:third).muzzle;
        // Do not launch through a wall between the soldier and the barrel.
        Vector3 from=transform.position+Vector3.up*1.4f,delta=muzzle.position-from;
        foreach(var hit in Physics.RaycastAll(from,delta.normalized,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform))return false;
        var rocket=Instantiate(Equipped.projectile,muzzle.position,Quaternion.LookRotation((target-muzzle.position).normalized));
        rocket.Launch(gameObject,transform,life.controller.velocity);
        rounds[Selected]--;readyAt[Selected]=Time.time+Equipped.rearmTime;
        LastShotIcon=Equipped.icon;LastShotTime=Time.time;
        var participant=GetComponent<ArenaParticipant>();
        if(participant)participant.RecordShotAt(transform.position);
        Fired?.Invoke();UpdateVisibility();return true;
    }
}
