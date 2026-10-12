using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class SoldierMotionGrip : MonoBehaviour
{
    public Animator animator;
    public TwoBoneIKConstraint leftHand;
    public TwoBoneIKConstraint rightHand;
    public bool gripEnabled = true;
    SoldierGrenades grenades;
    Rig aimRig;
    RigBuilder rigBuilder;
    RigLayer aimLayer;
    void Awake(){grenades=GetComponentInParent<SoldierGrenades>();aimRig=rightHand.GetComponentInParent<Rig>(true);rigBuilder=animator.GetComponent<RigBuilder>();aimLayer=rigBuilder.layers.Find(layer=>layer.rig==aimRig);}
    void Update()
    {
        bool throwing=grenades && grenades.IsThrowing;
        bool active = gripEnabled && !throwing;
        aimRig.weight=active?1f:0f;
        bool rigStateChanged=aimLayer.active!=active;
        aimLayer.active=active;
        float weight = active?1f - animator.GetFloat("WeaponReloadWeight"):0f;
        leftHand.weight = weight;
        rightHand.weight = weight;
        // This driver runs after RigBuilder.Update; sync the new weights before evaluation.
        if(rigStateChanged)rigBuilder.SyncLayers();
    }
}
