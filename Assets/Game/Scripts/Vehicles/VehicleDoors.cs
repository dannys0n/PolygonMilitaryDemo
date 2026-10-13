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
    [Min(.5f)] public float interactionRange = 3f;
    Vehicle vehicle;
    readonly Collider[] overlaps = new Collider[24];

    void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        foreach (var door in doors)
        {
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
        foreach (var door in doors)
        {
            float next = Mathf.MoveTowards(door.progress, door.open ? 1 : 0,
                Time.fixedDeltaTime / Mathf.Max(.1f, door.duration));
            if (Mathf.Approximately(next, door.progress)) continue;
            float previous = door.progress;
            door.progress = next;
            Apply(door);
            // Reopen when a walk-in occupant is in the closing leaf's path.
            if (!door.open && TouchesCharacter(door))
            {
                door.progress = previous;
                door.open = true;
                Apply(door);
            }
        }
    }

    bool TouchesCharacter(Door door)
    {
        foreach (var leaf in door.leaves)
        {
            var box = leaf.part.GetComponentInChildren<BoxCollider>();
            if (!box) continue;
            Vector3 half = Vector3.Scale(box.size * .5f, box.transform.lossyScale);
            int count = Physics.OverlapBoxNonAlloc(box.transform.TransformPoint(box.center),
                half + Vector3.one * .06f, overlaps, box.transform.rotation, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlaps[i].GetComponentInParent<CharacterController>()) return true;
        }
        return false;
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
