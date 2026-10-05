using UnityEngine;
using UnityEngine.Animations.Rigging;

public class SoldierMagazineReload : MonoBehaviour
{
    public Animator animator;
    public Transform magazine;
    public Transform magazineSeat;
    public Transform leftHandMagazineSocket;
    public TwoBoneIKConstraint leftHandIK;
    public Transform forwardGrip;
    public Transform magazineGrabTarget;
    public Transform blendedHandTarget;

    private int reloadLayer;
    private bool inHand;

    void Awake()
    {
        reloadLayer = animator.GetLayerIndex("Reload");
    }

    void Update()
    {
        float grabWeight = Mathf.Clamp01(animator.GetFloat("MagazineGrabIKWeight"));
        float gripWeight = Mathf.Clamp01(animator.GetFloat("LeftHandIKWeight"));

        // The grab target stays on the weapon, even while the magazine follows the hand.
        // All three targets are rifle children. Blend in weapon space so pelvis
        // and aim corrections move the weapon and hand target together this frame.
        blendedHandTarget.localPosition = Vector3.Lerp(
            forwardGrip.localPosition, magazineGrabTarget.localPosition, grabWeight);
        blendedHandTarget.localRotation = Quaternion.Slerp(
            forwardGrip.localRotation, magazineGrabTarget.localRotation, grabWeight);
        leftHandIK.weight = Mathf.Max(gripWeight, grabWeight);
    }

    public void GrabMagazine()
    {
        // Keep its current pose when the hand takes ownership, avoiding a snap.
        magazine.SetParent(leftHandMagazineSocket, true);
        inHand = true;
    }

    public void InsertMagazine()
    {
        magazine.SetParent(magazineSeat, false);
        magazine.localPosition = Vector3.zero;
        magazine.localRotation = Quaternion.identity;
        magazine.localScale = Vector3.one;
        inHand = false;
    }

    void LateUpdate()
    {
        if (!inHand)
            return;

        bool reloading = animator.GetCurrentAnimatorStateInfo(reloadLayer).IsName("Reload");
        if (animator.IsInTransition(reloadLayer))
            reloading |= animator.GetNextAnimatorStateInfo(reloadLayer).IsName("Reload");

        // Restore the magazine if another action interrupts the reload.
        if (!reloading)
            InsertMagazine();
    }

    void OnDisable()
    {
        if (inHand)
            InsertMagazine();
    }
}
