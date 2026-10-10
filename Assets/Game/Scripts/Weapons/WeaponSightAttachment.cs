using UnityEngine;

public class WeaponSightAttachment : MonoBehaviour
{
    [Tooltip("Position on the sight plane, with +Z along the sight line and +Y up.")]
    public Transform aimPoint;
    public Renderer scopeLens;
}
