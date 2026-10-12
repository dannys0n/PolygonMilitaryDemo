using UnityEngine;

public class InfantryLauncherVisual : MonoBehaviour
{
    public Transform muzzle;
    [Header("Sight line (owned by this launcher)")]
    public Transform rearSight, frontSight;
    public GameObject loadedRocket;
}
