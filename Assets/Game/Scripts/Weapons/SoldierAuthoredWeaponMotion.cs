using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// Share the established player motion inputs with a separately authored FPS rig.
// Each animator evaluates its own baked clips; no retargeting or IK runs here.
[DefaultExecutionOrder(150)]
public sealed class SoldierAuthoredWeaponMotion : MonoBehaviour
{
    SoldierWeaponView view;
    SoldierFirstPersonView owner;
    RuntimeAnimatorController sourceController, targetController;
    AnimatorControllerParameter[] parameters;
    (int source,int target)[] layers;
    void OnEnable()
    {
        // Shared rig roots have no weapon component; keep their explicit binding
        // when a grenade or perspective change temporarily hides the rig.
        Bind(view ? view : GetComponent<SoldierWeaponView>());
    }
    public void Bind(SoldierWeaponView equippedView)
    {
        view=equippedView;
        owner=GetComponentInParent<SoldierFirstPersonView>();
        sourceController=targetController=null;
        parameters=null; layers=null;
    }
    void Update()
    {
        if(!owner||!view||!view.animator||!owner.NeedsCameraAnimation)return;
        var source=owner.armsAnimator;var target=view.animator;
        if(!source.isInitialized||!target.isInitialized)return;
        if(sourceController!=source.runtimeAnimatorController||targetController!=target.runtimeAnimatorController)
        {
            sourceController=source.runtimeAnimatorController;targetController=target.runtimeAnimatorController;
            var available=source.parameters.ToDictionary(p=>p.nameHash,p=>p.type);
            parameters=target.parameters.Where(p=>p.type!=AnimatorControllerParameterType.Trigger
                &&available.TryGetValue(p.nameHash,out var type)&&type==p.type).ToArray();
            layers=Enumerable.Range(1,Mathf.Max(0,target.layerCount-1)).Select(i=>(source.GetLayerIndex(target.GetLayerName(i)),i))
                .Where(p=>p.Item1>=0).ToArray();
        }
        foreach(var p in parameters)
            switch(p.type)
            {
                case AnimatorControllerParameterType.Float:target.SetFloat(p.nameHash,source.GetFloat(p.nameHash));break;
                case AnimatorControllerParameterType.Bool:target.SetBool(p.nameHash,source.GetBool(p.nameHash));break;
                case AnimatorControllerParameterType.Int:target.SetInteger(p.nameHash,source.GetInteger(p.nameHash));break;
            }
        foreach(var layer in layers)target.SetLayerWeight(layer.target,source.GetLayerWeight(layer.source));
    }
}
