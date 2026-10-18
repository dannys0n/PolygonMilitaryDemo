using UnityEngine;
using System.Collections.Generic;
public class SoldierWeaponAttachments : MonoBehaviour
{
    [System.Serializable] public class Mount
    {
        public WeaponAttachmentDefinition.MountKind kind;
        public Transform firstPerson,thirdPerson;
        public WeaponAttachmentDefinition[] compatible;
        public Renderer[] builtInFirstPerson,builtInThirdPerson;
    }
    public Mount[] mounts;
    readonly List<GameObject> visuals=new List<GameObject>();
    WeaponAttachmentDefinition[] selections=System.Array.Empty<WeaponAttachmentDefinition>();
    public bool Supports(WeaponAttachmentDefinition attachment)=>System.Array.Exists(mounts,m=>m.kind==attachment.mount&&System.Array.IndexOf(m.compatible,attachment)>=0);
    public void Apply(WeaponAttachmentDefinition[] selections,SoldierFirstPersonView view)
    {
        this.selections=selections;
        ClearPresentation();
        Transform rear=null,front=null;
        WeaponAttachmentDefinition rearDefinition=null;
        float sightLift=0;
        foreach(var mount in mounts)
        {
            var selected=System.Array.Find(selections,a=>a&&a.mount==mount.kind);
            bool replacement=selected && Supports(selected);
            foreach(var r in mount.builtInFirstPerson)r.forceRenderingOff=replacement;
            foreach(var r in mount.builtInThirdPerson)r.forceRenderingOff=replacement;
            if(!replacement)continue;
            if (mount.kind == WeaponAttachmentDefinition.MountKind.SightRiser)
                sightLift = selected.sightLift;
            var first=mount.firstPerson?Add(selected,mount.firstPerson,view.firstPersonLayer,view.firstPerson):null;
            if(first && first.TryGetComponent<WeaponSightAttachment>(out var iron))
            {
                // An attachment may override either end, or both (for a complete iron pair).
                if (iron.rearSight) { rear = iron.rearSight; rearDefinition = selected; }
                if (iron.frontSight) front = iron.frontSight;
            }
            Add(selected,mount.thirdPerson,view.thirdPersonLayer,true);
        }
        if(TryGetComponent<SoldierWeaponSights>(out var sights))
        {
            sights.SetIronReplacements(rear,front,rearDefinition);
            sights.SetRiser(sightLift);
        }
        RefreshIronVisibility();
        view.RefreshVisuals();
    }
    public void ClearPresentation()
    {
        foreach(var go in visuals)EquipmentVisualPool.Return(go);
        visuals.Clear();
    }
    public void RefreshIronVisibility()
    {
        bool optic=TryGetComponent<SoldierWeaponSights>(out var sights) && sights.SelectedSight && sights.SelectedSight.prefab;
        foreach(var mount in mounts)
        {
            if(mount.kind!=WeaponAttachmentDefinition.MountKind.RearIron && mount.kind!=WeaponAttachmentDefinition.MountKind.FrontIron)continue;
            var replacement=System.Array.Find(selections,a=>a && a.mount==mount.kind && Supports(a));
            foreach(var r in mount.builtInFirstPerson)r.forceRenderingOff=optic || replacement;
            foreach(var r in mount.builtInThirdPerson)r.forceRenderingOff=optic || replacement;
        }
        foreach(var go in visuals)
            if(go.TryGetComponent<WeaponSightAttachment>(out _))go.SetActive(!optic);
    }
    GameObject Add(WeaponAttachmentDefinition definition,Transform mount,int layer,bool visible)
    {
        var go=EquipmentVisualPool.Rent(definition.prefab,mount);
#if UNITY_EDITOR
        if (go.TryGetComponent<WeaponSightAttachment>(out var sight))
            sight.SourcePrefab = definition.prefab.GetComponent<WeaponSightAttachment>();
#endif
        go.transform.SetLocalPositionAndRotation(definition.positionOffset,Quaternion.Euler(definition.rotationOffset));go.transform.localScale=definition.scale;
        if (mount.TryGetComponent<WeaponAttachmentSocket>(out var socket)) socket.Place(definition, go.transform);
        foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
        foreach(var r in go.GetComponentsInChildren<Renderer>(true))r.enabled=visible;
        visuals.Add(go);return go;
    }
}
