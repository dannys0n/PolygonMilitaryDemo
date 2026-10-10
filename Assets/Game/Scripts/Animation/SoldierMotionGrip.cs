using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class SoldierMotionGrip : MonoBehaviour
{
    public Animator animator;
    public TwoBoneIKConstraint leftHand;
    public TwoBoneIKConstraint rightHand;
    SoldierGrenades grenades;
    Rig aimRig;
    RigBuilder rigBuilder;
    RigLayer aimLayer;
    void Awake(){grenades=GetComponentInParent<SoldierGrenades>();aimRig=rightHand.GetComponentInParent<Rig>(true);rigBuilder=animator.GetComponent<RigBuilder>();aimLayer=rigBuilder.layers.Find(layer=>layer.rig==aimRig);}
    void Update()
    {
        bool throwing=grenades && grenades.IsThrowing;
        aimRig.weight=throwing?0f:1f;
        bool rigStateChanged=aimLayer.active==throwing;
        aimLayer.active=!throwing;
        float weight = throwing?0f:1f - animator.GetFloat("WeaponReloadWeight");
        leftHand.weight = weight;
        rightHand.weight = weight;
        // This driver runs after RigBuilder.Update; sync the new weights before evaluation.
        if(rigStateChanged)rigBuilder.SyncLayers();
    }
}
