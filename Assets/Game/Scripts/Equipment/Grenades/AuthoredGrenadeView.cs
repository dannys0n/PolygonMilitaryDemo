using System;
using UnityEngine;

// Optional source-rig presentation; projectile, inventory and throw rules stay on SoldierGrenades.
public sealed class AuthoredGrenadeView : MonoBehaviour
{
    [Serializable] public struct HeldPose
    {
        public GrenadeDefinition grenade;
        public Vector3 position, rotation;
    }
    public Transform hand;
    public float duration=3.2f, releaseTime=1.8f;
    public Vector3 handPosition, handRotation;
    public HeldPose[] heldPoses = Array.Empty<HeldPose>();
    public Transform pinHand;
    public float pinPullTime=.45f, pinHideTime=1.05f;
    Transform heldPin;
    bool pinPulled;

    public void BindHeldVisual(Transform visual, GrenadeDefinition grenade)
    {
        foreach(var pose in heldPoses)
            if(pose.grenade==grenade) {visual.SetLocalPositionAndRotation(pose.position,Quaternion.Euler(pose.rotation));break;}
        if(!pinHand)return;
        foreach(var child in visual.GetComponentsInChildren<Transform>(true))
            if(child.name.Contains("_Pin_")) {heldPin=child;break;}
    }
    public void UpdateHeldVisual(float elapsed)
    {
        if(!heldPin)return;
        if(!pinPulled && elapsed>=pinPullTime)
        {
            heldPin.SetParent(pinHand,true);
            pinPulled=true;
        }
        if(elapsed>=pinHideTime)heldPin.gameObject.SetActive(false);
    }
}
