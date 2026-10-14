using UnityEngine;

// CharacterControllers do not receive dynamic-body impulses. Displace overlapping controllers
// before the vehicle's solver step instead of letting a trapped capsule lever the chassis upward.
public sealed class VehicleCharacterClearance
{
    readonly Vehicle vehicle;
    readonly Collider[] shell;
    readonly Collider[] nearby=new Collider[32];
    public VehicleCharacterClearance(Vehicle owner)
    {
        vehicle=owner;
        shell=System.Array.FindAll(owner.GetComponentsInChildren<Collider>(true),
            c=>!c.isTrigger && !(c is WheelCollider) && c.attachedRigidbody==owner.Body);
    }
    public void Resolve()
    {
        int layer=LayerMask.NameToLayer("Characters");
        if(layer<0)return;
        var bounds=vehicle.NavigationBounds;
        var scale=vehicle.transform.lossyScale;
        var half=Vector3.Scale(bounds.extents,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)))+Vector3.one*.25f;
        int count=Physics.OverlapBoxNonAlloc(vehicle.transform.TransformPoint(bounds.center),half,
            nearby,vehicle.transform.rotation,1<<layer,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var controller=nearby[i] as CharacterController;
            if(!controller || !controller.enabled || controller.transform.IsChildOf(vehicle.transform))continue;
            bool displaced=false;
            foreach(var collider in shell)
            {
                if(!collider || !collider.enabled || !collider.gameObject.activeInHierarchy)continue;
                for(int step=0;step<3;step++)
                {
                    if(!Physics.ComputePenetration(controller,controller.transform.position,controller.transform.rotation,
                        collider,collider.transform.position,collider.transform.rotation,out var direction,out float depth)
                        || depth<=Mathf.Max(.025f,controller.skinWidth+.01f))break;
                    if(direction.y<-.25f)
                    {
                        var outward=Vector3.ProjectOnPlane(controller.transform.position-
                            vehicle.transform.TransformPoint(bounds.center),Vector3.up);
                        if(outward.sqrMagnitude>.001f)direction=outward.normalized;
                    }
                    controller.Move(direction*Mathf.Min(.5f,depth+.03f));
                    displaced=true;
                }
            }
            if(displaced)controller.GetComponent<SoldierVehiclePlatform>()?.DetachFromSupport();
        }
    }
}
