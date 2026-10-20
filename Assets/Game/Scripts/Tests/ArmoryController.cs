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
    public ArmoryWall wall;
    [NonSerialized] public ArmoryTarget[] targets;
    readonly Dictionary<int,List<WeaponAttachmentDefinition>> selections = new();
    ArmoryTarget pending;
    ArmoryTarget heldTarget;
    readonly List<SoldierWeaponDefinition> runtimeDefinitions = new();
    float refreshAt;
    int lastSlot = -1;

    void Start()
    {
        targets = wall.Build();
        health.DamageFilter = _ => false;
        foreach(var slot in loadout.slots)
        {
            var definition=Instantiate(slot.definition);
            definition.startingReserveAmmo=1000000;
            slot.definition=definition;
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
            loadout.actions.TryReload();
        if(Time.unscaledTime<refreshAt && lastSlot==loadout.selectedSlot)return;
        refreshAt=Time.unscaledTime+.1f;lastSlot=loadout.selectedSlot;
        RefreshDisplays();
    }

    public void Activate(ArmoryTarget target)
    {
        if(wall.lazyReview && target.IsWeapon)
        {
            if(target.WeaponId!=loadout.EquippedWeapon.definition.catalogId)
            {
                var entry=wall.displayCache.LoadWeapon(target.WeaponId);
                var slot=ArmoryReviewLoadout.Slot(entry);
                var definition=Instantiate(slot.definition);definition.startingReserveAmmo=1000000;
                slot.definition=definition;runtimeDefinitions.Add(definition);
                selections.Clear();loadout.ReplaceEquipped(slot);
            }
            status.text="Equipping "+target.title;refreshAt=0;return;
        }
        if(wall.lazyReview)wall.displayCache.Resolve(target);
        if(target.weapon)
        {
            int index=Array.FindIndex(loadout.slots,s=>s.definition.catalogId==target.weapon.catalogId);
            if(index<0){status.text="Weapon is not in this loadout.";return;}
            if(index!=loadout.selectedSlot)loadout.TrySelectWeapon(index);
            status.text="Equipping "+target.title;
        }
        else if(target.sight || target.resetSight)
        {
            var module=loadout.EquippedSights;
            if(target.resetSight)
            {
                if(module){loadout.SetSight(module.defaultSight);status.text="Restored default sights.";}
                else status.text="This weapon uses built-in sights.";
                refreshAt=0;return;
            }
            if(!module || !module.Supports(target.sight)){status.text="This sight does not fit the equipped weapon.";return;}
            loadout.SetSight(module.SelectedSight==target.sight?null:target.sight);
            status.text="Sight: "+module.SelectedSight.displayName;
        }
        else if(target.attachment)
        {
            var module=loadout.EquippedAttachments;
            if(!module || !module.Supports(target.attachment)){status.text="This attachment does not fit the equipped weapon.";return;}
            if(!selections.TryGetValue(loadout.selectedSlot,out var list))selections[loadout.selectedSlot]=list=new();
            bool removing=list.Contains(target.attachment);
            list.RemoveAll(a=>a.mount==target.attachment.mount);
            if(!removing)list.Add(target.attachment);
            loadout.SetAttachments(list.ToArray());
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
            if(target.cachedItem!=null)
            {
                bool chosen=target.IsWeapon?target.WeaponId==slot.definition.catalogId
                    :target.cachedItem.kind==ArmoryDisplayCache.Kind.Sight
                        ?loadout.EquippedSights&&loadout.EquippedSights.SelectedSight&&target.cachedItem.key=="sight:"+loadout.EquippedSights.SelectedSight.name
                        :current!=null&&current.Any(x=>target.cachedItem.key=="attachment:"+x.name);
                target.Show(target.cachedItem.Supports(slot.definition.catalogId),chosen);continue;
            }
            bool compatible=target.weapon || (target.sight && loadout.EquippedSights && loadout.EquippedSights.Supports(target.sight))
                || (target.resetSight && loadout.EquippedSights)
                || (target.attachment && loadout.EquippedAttachments && loadout.EquippedAttachments.Supports(target.attachment));
            bool selected=target.weapon?target.weapon.catalogId==slot.definition.catalogId:target.resetSight
                ?loadout.EquippedSights && loadout.EquippedSights.SelectedSight==loadout.EquippedSights.defaultSight:target.sight
                ?loadout.EquippedSights && loadout.EquippedSights.SelectedSight==target.sight:current!=null&&current.Contains(target.attachment);
            target.Show(compatible,selected);
        }
        equipment.text=slot.definition.displayName+"   "+loadout.EquippedState.MagazineAmmo+" / ∞"
            +"\n"+(loadout.EquippedSights?loadout.EquippedSights.SelectedSight.displayName:"Built-in sights");
    }

    public void ReturnToMenu() => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    void OnDestroy()
    {
        shooting.ShotInteraction-=Interact;
        health.DamageFilter=null;
        foreach(var definition in runtimeDefinitions)Destroy(definition);
    }
}
