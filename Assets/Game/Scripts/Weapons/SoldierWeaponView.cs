using UnityEngine;

// Local references inside one spawnable FPS or TPS weapon presentation.
public class SoldierWeaponView : MonoBehaviour
{
    [System.Serializable] public class AttachmentSocket
    {
        public WeaponAttachmentDefinition.MountKind kind;
        public Transform socket;
        public Renderer[] builtIn;
    }
    public Animator animator;
    public string mountName;
    public Vector3 mountPosition, mountScale=Vector3.one;
    public Quaternion mountRotation=Quaternion.identity;
    public SoldierWeaponSockets effects;
    public Transform opticMount, rearSight, frontSight;
    public AttachmentSocket[] attachments;
}
