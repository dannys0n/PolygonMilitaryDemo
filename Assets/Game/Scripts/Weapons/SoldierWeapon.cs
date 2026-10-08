using UnityEngine;
using UnityEngine.Animations.Rigging;

// Equipment supplies settings and grip poses; the player keeps its skeletons and constraints.
public class SoldierWeapon : MonoBehaviour
{
    [System.Serializable]
    public struct GripPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public void Apply(Transform target)
        {
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = scale;
        }
    }

    [System.Serializable]
    public struct HandPlacement
    {
        public GripPose leftGrip;
        public GripPose rightGrip;
        public GripPose leftElbow;
        public GripPose rightElbow;
        public void Apply(TwoBoneIKConstraint left, TwoBoneIKConstraint right)
        {
            leftGrip.Apply(left.data.target);
            rightGrip.Apply(right.data.target);
            leftElbow.Apply(left.data.hint);
            rightElbow.Apply(right.data.hint);
        }
    }

    [Header("Shared weapon configuration")]
    public SoldierWeaponDefinition definition;
    [Header("First-person grip targets and elbow hints")]
    public HandPlacement firstPersonHands;
    [Header("Third-person grip targets and elbow hints")]
    public HandPlacement thirdPersonHands;
    [Header("Third-person sprint hand socket")]
    public GripPose sprintSocket;
}
