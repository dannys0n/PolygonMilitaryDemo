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
    public bool Supports(WeaponAttachmentDefinition attachment)=>System.Array.Exists(mounts,m=>m.kind==attachment.mount&&System.Array.IndexOf(m.compatible,attachment)>=0);
    public void Apply(WeaponAttachmentDefinition[] selections,SoldierFirstPersonView view)
    {
        foreach(var go in visuals){go.SetActive(false);go.transform.SetParent(null);Destroy(go);}visuals.Clear();
        foreach(var mount in mounts)
        {
            var selected=System.Array.Find(selections,a=>a&&a.mount==mount.kind);
            bool replacement=selected && Supports(selected);
            foreach(var r in mount.builtInFirstPerson)r.forceRenderingOff=replacement;
            foreach(var r in mount.builtInThirdPerson)r.forceRenderingOff=replacement;
            if(!replacement)continue;
            Add(selected,mount.firstPerson,view.firstPersonLayer,view.firstPerson);
            Add(selected,mount.thirdPerson,view.thirdPersonLayer,true);
        }
        view.RefreshVisuals();
    }
    void Add(WeaponAttachmentDefinition definition,Transform mount,int layer,bool visible)
    {
        var go=Instantiate(definition.prefab,mount,false);
        go.transform.SetLocalPositionAndRotation(definition.positionOffset,Quaternion.Euler(definition.rotationOffset));go.transform.localScale=definition.scale;
        foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
        foreach(var r in go.GetComponentsInChildren<Renderer>(true))r.enabled=visible;
        visuals.Add(go);
    }
}
