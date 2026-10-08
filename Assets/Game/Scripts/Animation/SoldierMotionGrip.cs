using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class SoldierMotionGrip : MonoBehaviour
{
    public Animator animator;
    public TwoBoneIKConstraint leftHand;
    public TwoBoneIKConstraint rightHand;
    void Update()
    {
        float weight = 1f - animator.GetFloat("WeaponReloadWeight");
        leftHand.weight = weight;
        rightHand.weight = weight;
    }
}
