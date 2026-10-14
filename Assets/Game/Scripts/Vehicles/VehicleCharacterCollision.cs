using System.Collections.Generic;
using UnityEngine;

// CharacterControllers are kinematic bodies: penetrating a dynamic chassis can impart
// a solver impulse even without an explicit push script. Keep movement blocking on a
// kinematic shell, while the original chassis handles terrain, cargo and other vehicles.
public sealed class VehicleCharacterCollision
{
    struct Shape
    {
        public Collider source, proxy;
    }
    readonly List<Shape> shapes = new List<Shape>();

    public VehicleCharacterCollision(Vehicle vehicle)
    {
        int characters = LayerMask.NameToLayer("Characters");
        if (characters < 0) throw new System.InvalidOperationException("Characters collision layer is required.");
        int characterMask = 1 << characters;
        // Gather before creating the shell, so proxies never become sources.
        var sources = vehicle.GetComponentsInChildren<Collider>(true);
        var shell = new GameObject("Character movement shell");
        shell.layer = Physics.IgnoreRaycastLayer;
        shell.transform.SetParent(vehicle.transform, false);
        var body = shell.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        foreach (var source in sources)
        {
            if (source.isTrigger || source.attachedRigidbody != vehicle.Body || source is WheelCollider) continue;
            var shapeObject = new GameObject(source.name + " contact");
            shapeObject.layer = Physics.IgnoreRaycastLayer;
            shapeObject.transform.SetParent(shell.transform, false);
            Collider proxy = null;
            if (source is BoxCollider box)
            {
                var copy = shapeObject.AddComponent<BoxCollider>();
                copy.center = box.center; copy.size = box.size; proxy = copy;
            }
            else if (source is SphereCollider sphere)
            {
                var copy = shapeObject.AddComponent<SphereCollider>();
                copy.center = sphere.center; copy.radius = sphere.radius; proxy = copy;
            }
            else if (source is CapsuleCollider capsule)
            {
                var copy = shapeObject.AddComponent<CapsuleCollider>();
                copy.center = capsule.center; copy.radius = capsule.radius;
                copy.height = capsule.height; copy.direction = capsule.direction; proxy = copy;
            }
            else if (source is MeshCollider mesh)
            {
                var copy = shapeObject.AddComponent<MeshCollider>();
                copy.sharedMesh = mesh.sharedMesh; copy.convex = mesh.convex; proxy = copy;
            }
            if (!proxy) { Object.Destroy(shapeObject); continue; }
            source.excludeLayers |= characterMask;
            proxy.includeLayers = characterMask;
            proxy.excludeLayers = ~characterMask;
            proxy.layerOverridePriority = 1;
            proxy.sharedMaterial = source.sharedMaterial;
            shapes.Add(new Shape { source = source, proxy = proxy });
        }
        Sync();
    }

    public void Sync()
    {
        foreach (var shape in shapes)
        {
            if (!shape.source || !shape.proxy) continue;
            var source = shape.source.transform;
            var proxy = shape.proxy.transform;
            proxy.SetPositionAndRotation(source.position, source.rotation);
            var parentScale = proxy.parent.lossyScale;
            var scale = source.lossyScale;
            proxy.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
            shape.proxy.enabled = shape.source.enabled && shape.source.gameObject.activeInHierarchy;
        }
    }
}