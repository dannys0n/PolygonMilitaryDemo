using System;
using UnityEngine;

// Articulated model parts and their primitive colliders remain on the vehicle's Rigidbody.
[RequireComponent(typeof(Vehicle))]
public class VehicleDoors : MonoBehaviour
{
    [Serializable]
    public class Leaf
    {
        public Transform part;
        public Vector3 openTranslation;
        public Vector3 openEuler;
        [NonSerialized] public Vector3 closedPosition;
        [NonSerialized] public Quaternion closedRotation;
    }

    [Serializable]
    public class Door
    {
        public string name;
        public Transform interaction;
        public Leaf[] leaves;
        [Min(.1f)] public float duration = 1.1f;
        public bool open;
        [NonSerialized] public float progress;
    }

    public Door[] doors = Array.Empty<Door>();
    public bool operateTogether;
    [Min(.5f)] public float interactionRange = 3f;
    Vehicle vehicle;
    readonly Collider[] overlaps = new Collider[24];

    void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        foreach (var door in doors)
        {
            if (operateTogether && doors.Length > 0) door.open = doors[0].open;
            door.progress = door.open ? 1 : 0;
            foreach (var leaf in door.leaves)
            {
                leaf.closedPosition = leaf.part.localPosition;
                leaf.closedRotation = leaf.part.localRotation;
            }
            Apply(door);
        }
    }

    void FixedUpdate()
    {
        if (vehicle.IsDestroyed) return;
        if (operateTogether && doors.Length > 0)
        {
            var first = doors[0];
            float previous = first.progress;
            float next = Mathf.MoveTowards(previous, first.open ? 1 : 0,
                Time.fixedDeltaTime / Mathf.Max(.1f, first.duration));
            if (Mathf.Approximately(next, previous)) return;
            foreach (var door in doors) { door.progress = next; door.open = first.open; Apply(door); }
            bool blocked=false;
            foreach(var door in doors)if(!ClearCharacterContacts(door))blocked=true;
            if(blocked)
            {
                bool reopening=!first.open;
                foreach (var door in doors) { door.progress = previous; if(reopening)door.open = true; Apply(door); }
            }
            return;
        }
        foreach (var door in doors)
        {
            float next = Mathf.MoveTowards(door.progress, door.open ? 1 : 0,
                Time.fixedDeltaTime / Mathf.Max(.1f, door.duration));
            if (Mathf.Approximately(next, door.progress)) continue;
            float previous = door.progress;
            door.progress = next;
            Apply(door);
            // Displace controllers before PhysX solves the articulated chassis contact.
            // If scenery traps the player, stop the door instead of levering the vehicle upward.
            if (!ClearCharacterContacts(door))
            {
                door.progress = previous;
                if(!door.open)door.open = true;
                Apply(door);
            }
        }
    }

    bool ClearCharacterContacts(Door door)
    {
        bool clear=true;
        foreach (var leaf in door.leaves)
        {
            foreach(var box in leaf.part.GetComponentsInChildren<BoxCollider>())
            {
                if(!box.enabled || box.isTrigger)continue;
                Vector3 half=Vector3.Scale(box.size*.5f,box.transform.lossyScale);
                int count=Physics.OverlapBoxNonAlloc(box.transform.TransformPoint(box.center),
                    half+Vector3.one*.06f,overlaps,box.transform.rotation,~0,QueryTriggerInteraction.Ignore);
                if(count==overlaps.Length)clear=false;
                for(int i=0;i<count;i++)
                {
                    var controller=overlaps[i] as CharacterController;
                    if(!controller || !controller.enabled)continue;
                    for(int attempt=0;attempt<4;attempt++)
                    {
                        if(!Physics.ComputePenetration(controller,controller.transform.position,controller.transform.rotation,
                            box,box.transform.position,box.transform.rotation,out var direction,out var depth))break;
                        if(direction.y<-.25f)
                        {
                            // A lowered cargo ramp should push a grounded player away, never into the ground.
                            var outward=Vector3.ProjectOnPlane(controller.transform.position-
                                transform.TransformPoint(vehicle.NavigationBounds.center),Vector3.up);
                            if(outward.sqrMagnitude>.001f)direction=outward.normalized;
                        }
                        controller.GetComponent<SoldierVehiclePlatform>()?.DetachFromSupport();
                        controller.Move(direction*Mathf.Min(.5f,depth+.03f));
                    }
                    if(Physics.ComputePenetration(controller,controller.transform.position,controller.transform.rotation,
                        box,box.transform.position,box.transform.rotation,out _,out float remaining) && remaining>.005f)clear=false;
                }
            }
        }
        return clear;
    }

    static void Apply(Door door)
    {
        float blend = door.progress * door.progress * (3 - 2 * door.progress);
        foreach (var leaf in door.leaves)
        {
            leaf.part.localPosition = leaf.closedPosition + leaf.openTranslation * blend;
            leaf.part.localRotation = leaf.closedRotation * Quaternion.Euler(leaf.openEuler * blend);
        }
    }

    public int ClosestDoor(Vector3 position, float range = -1)
    {
        float best = range > 0 ? range : interactionRange;
        int index = -1;
        for (int i = 0; i < doors.Length; i++)
        {
            float distance = Vector3.Distance(position, doors[i].interaction.position);
            if (distance > best) continue;
            best = distance;
            index = i;
        }
        return index;
    }

    public bool TryToggle(Vector3 position)
    {
        int index = ClosestDoor(position);
        return index >= 0 && Toggle(index);
    }

    public bool Toggle(int index)
    {
        if (vehicle.IsDestroyed || index < 0 || index >= doors.Length) return false;
        if (operateTogether) return ToggleAll();
        doors[index].open = !doors[index].open;
        vehicle.Body.WakeUp();
        return true;
    }

    public bool ToggleAll()
    {
        if (vehicle.IsDestroyed || doors.Length == 0) return false;
        bool open = !Array.Exists(doors, door => door.open);
        foreach (var door in doors) door.open = open;
        vehicle.Body.WakeUp();
        return true;
    }
}
