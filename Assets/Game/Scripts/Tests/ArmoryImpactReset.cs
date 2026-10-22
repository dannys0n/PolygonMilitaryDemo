using UnityEngine;

public sealed class ArmoryImpactReset : MonoBehaviour, ISurfaceImpactReceiver
{
    public ArmoryImpactWall wall;
    public void ReceiveImpact(Vector3 point, Vector3 normal)
    {
        if (wall) wall.ClearMarks();
    }
}
