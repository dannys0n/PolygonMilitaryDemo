using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

// Scene-only equipment sandbox: selection does not edit saved definitions.
[DefaultExecutionOrder(100)]
public class ArmoryController : MonoBehaviour
{
    public SoldierWeaponLoadout loadout;
    public SoldierHitscan shooting;
    public SoldierHealth health;
    public TMP_Text status, equipment;
    public CanvasGroup crosshair, hitMarker;
    float hitUntil;
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
        var reticle = crosshair.GetComponent<WeaponCrosshair>();
        if (reticle) reticle.actor = loadout.GetComponent<SoldierLife>();
        shooting.ShotInteraction += Interact;
        CombatEvents.DamageConfirmed += OnDamageConfirmed;
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

    void OnDamageConfirmed(CombatEvents.ConfirmedDamage hit)
    {
        if(hit.attacker==loadout.gameObject && hit.target!=loadout.gameObject)hitUntil=Time.time+.18f;
    }

    void LateUpdate()
    {
        crosshair.alpha=1f-loadout.presentation.view.AimBlend;
        if(hitMarker)hitMarker.alpha=Mathf.Clamp01((hitUntil-Time.time)/.18f);
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
        if(target.IsWeapon)
        {
            if(target.WeaponId!=loadout.EquippedWeapon.definition.catalogId)
            {
                var entry=wall.Index.LoadWeapon(target.WeaponId);
                var slot=ArmoryReviewLoadout.Slot(entry);
                var definition=Instantiate(slot.definition);definition.startingReserveAmmo=1000000;
                slot.definition=definition;
                selections.Clear();loadout.ReplaceEquipped(slot);
                foreach(var previous in runtimeDefinitions)Destroy(previous);
                runtimeDefinitions.Clear();runtimeDefinitions.Add(definition);
            }
            status.text="Equipping "+target.title;refreshAt=0;return;
        }
        if(target.sight || target.resetSight)
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
            status.text="Sight: "+(module.SelectedSight?module.SelectedSight.displayName:"Built-in sights");
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
            bool compatible=target.IsWeapon || (target.sight && loadout.EquippedSights && loadout.EquippedSights.Supports(target.sight))
                || (target.resetSight && loadout.EquippedSights)
                || (target.attachment && loadout.EquippedAttachments && loadout.EquippedAttachments.Supports(target.attachment));
            bool selected=target.IsWeapon?target.WeaponId==slot.definition.catalogId:target.resetSight
                ?loadout.EquippedSights && loadout.EquippedSights.SelectedSight==loadout.EquippedSights.defaultSight:target.sight
                ?loadout.EquippedSights && loadout.EquippedSights.SelectedSight==target.sight:current!=null&&current.Contains(target.attachment);
            target.Show(compatible,selected);
        }
        equipment.text=slot.definition.displayName+"   "+loadout.EquippedState.MagazineAmmo+" / ∞"
            +"\n"+(loadout.EquippedSights && loadout.EquippedSights.SelectedSight?loadout.EquippedSights.SelectedSight.displayName:"Built-in sights");
    }

    public void ReturnToMenu() => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    void OnDestroy()
    {
        shooting.ShotInteraction-=Interact;
        CombatEvents.DamageConfirmed-=OnDamageConfirmed;
        health.DamageFilter=null;
        foreach(var definition in runtimeDefinitions)Destroy(definition);
    }
}
