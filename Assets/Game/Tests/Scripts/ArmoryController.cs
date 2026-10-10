using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

// Scene-only equipment sandbox: selection does not edit saved classes or definitions.
[DefaultExecutionOrder(100)]
public class ArmoryController : MonoBehaviour
{
    public SoldierWeaponLoadout loadout;
    public SoldierHitscan shooting;
    public SoldierHealth health;
    public TMP_Text status, equipment;
    public CanvasGroup crosshair;
    public ArmoryTarget[] targets;
    readonly Dictionary<int,List<WeaponAttachmentDefinition>> selections = new();
    ArmoryTarget pending;
    ArmoryTarget heldTarget;
    readonly List<SoldierWeaponDefinition> runtimeDefinitions = new();
    float refreshAt;
    int lastSlot = -1;

    void Start()
    {
        health.DamageFilter = _ => false;
        foreach(var slot in loadout.slots)
        {
            var definition=Instantiate(slot.weapon.definition);
            definition.startingReserveAmmo=1000000;
            slot.weapon.definition=definition;
            runtimeDefinitions.Add(definition);
        }
        loadout.ResetAmmunition();
        loadout.ConfigureAvailableSlots(Enumerable.Range(0,loadout.slots.Length).ToArray());
        shooting.ShotInteraction += Interact;
        status.text = "Shoot a weapon to equip it. Shoot an attachment again to remove it.";
    }

    // Runs before projectile spawning; RPGs and grenades can also select displays safely.
    bool Interact(Ray ray)
    {
        foreach(var hit in Physics.RaycastAll(ray,100,shooting.hitLayers,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
        {
            if(hit.transform.IsChildOf(loadout.transform)) continue;
            var target=hit.collider.GetComponentInParent<ArmoryTarget>();
            if(!target) return false;
            if(heldTarget!=target)pending=target;
            heldTarget=target;
            return true;
        }
        return false;
    }

    void LateUpdate()
    {
        crosshair.alpha=1f-loadout.presentation.view.AimBlend;
        if(!Input.GetMouseButton(0))heldTarget=null;
        // Present the outgoing shot before switching equipment or replacing attachments.
        if(pending && !loadout.IsSwitching)
        {
            var target=pending;pending=null;Activate(target);
        }
        // Unlimited supply without resetting controllers, interrupting reloads, or editing assets.
        var state=loadout.EquippedState;
        if(state.MagazineAmmo==0 && !loadout.actions.IsReloading && !loadout.IsSwitching)
            state.CompleteReload(loadout.EquippedWeapon.definition);
        if(Time.unscaledTime<refreshAt && lastSlot==loadout.selectedSlot)return;
        refreshAt=Time.unscaledTime+.1f;lastSlot=loadout.selectedSlot;
        RefreshDisplays();
    }

    public void Activate(ArmoryTarget target)
    {
        if(target.weapon)
        {
            int index=Array.FindIndex(loadout.slots,s=>s.weapon.definition.catalogId==target.weapon.catalogId);
            if(index<0){status.text="Weapon is not in this loadout.";return;}
            if(index!=loadout.selectedSlot)loadout.TrySelectWeapon(index);
            status.text="Equipping "+target.title;
        }
        else if(target.sight || target.resetSight)
        {
            var module=loadout.EquippedSights;
            if(target.resetSight)
            {
                if(module){module.SetSight(module.defaultSight);status.text="Restored default sights.";}
                else status.text="This weapon uses built-in sights.";
                refreshAt=0;return;
            }
            if(!module || !module.Supports(target.sight)){status.text="This sight does not fit the equipped weapon.";return;}
            module.SetSight(module.SelectedSight==target.sight?module.defaultSight:target.sight);
            status.text="Sight: "+module.SelectedSight.displayName;
        }
        else if(target.attachment)
        {
            var module=loadout.slots[loadout.selectedSlot].attachments;
            if(!module || !module.Supports(target.attachment)){status.text="This attachment does not fit the equipped weapon.";return;}
            if(!selections.TryGetValue(loadout.selectedSlot,out var list))selections[loadout.selectedSlot]=list=new();
            bool removing=list.Contains(target.attachment);
            list.RemoveAll(a=>a.mount==target.attachment.mount);
            if(!removing)list.Add(target.attachment);
            module.Apply(list.ToArray(),loadout.presentation.view);
            status.text=(removing?"Removed ":"Equipped ")+target.title;
        }
        refreshAt=0;
    }

    void RefreshDisplays()
    {
        var slot=loadout.slots[loadout.selectedSlot];
        selections.TryGetValue(loadout.selectedSlot,out var current);
        foreach(var target in targets)
        {
            bool compatible=target.weapon || (target.sight && slot.sights && slot.sights.Supports(target.sight))
                || (target.resetSight && slot.sights)
                || (target.attachment && slot.attachments && slot.attachments.Supports(target.attachment));
            bool selected=target.weapon?target.weapon.catalogId==slot.weapon.definition.catalogId:target.resetSight
                ?slot.sights && slot.sights.SelectedSight==slot.sights.defaultSight:target.sight
                ?slot.sights && slot.sights.SelectedSight==target.sight:current!=null&&current.Contains(target.attachment);
            target.Show(compatible,selected);
        }
        equipment.text=slot.weapon.definition.displayName+"   "+loadout.EquippedState.MagazineAmmo+" / ∞"
            +"\n"+(slot.sights?slot.sights.SelectedSight.displayName:"Built-in sights");
    }

    public void ReturnToMenu() => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    void OnDestroy()
    {
        shooting.ShotInteraction-=Interact;
        health.DamageFilter=null;
        foreach(var definition in runtimeDefinitions)Destroy(definition);
    }
}
