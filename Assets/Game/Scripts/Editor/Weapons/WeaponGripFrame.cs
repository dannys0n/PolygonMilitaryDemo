using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Establish matching physical reference frames once. Runtime uses the existing weapon/hand IK pipeline.
public static class WeaponGripFrame
{
    struct Geometry
    {
        public Mesh mesh;
        public Transform reference;
        public Matrix4x4 bind;
        public Matrix4x4 matrix => reference.localToWorldMatrix * bind;
    }
    static IEnumerable<Geometry> GeometryOf(SoldierWeaponView view)
    {
        foreach (var filter in view.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh) yield return new Geometry { mesh = filter.sharedMesh, reference = filter.transform, bind = Matrix4x4.identity };
        foreach (var skin in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = skin.sharedMesh;
            if (!mesh || skin.bones.Length == 0) continue;
            // Register the receiver's rigid skin region, not its renderer object or a moving magazine.
            var weights = mesh.boneWeights;
            int dominant = weights.GroupBy(w => w.boneIndex0).OrderByDescending(g => g.Count()).First().Key;
            yield return new Geometry { mesh = mesh, reference = skin.bones[dominant], bind = mesh.bindposes[dominant] };
        }
    }
    public static Func<Matrix4x4> Receiver(SoldierWeaponView view)
    {
        var receiver = GeometryOf(view).OrderByDescending(g => g.mesh.bounds.size.sqrMagnitude).First();
        return () => receiver.matrix;
    }
    public static void Ensure(SoldierWeaponView source, SoldierWeaponView target, Transform triggerKnuckle)
    {
        if (source.gripFrame && target.gripFrame) return;
        var sourceGeometry = GeometryOf(source).ToArray();
        var targetGeometry = GeometryOf(target).ToArray();
        var matches = sourceGeometry.SelectMany(a => targetGeometry.Where(b => b.mesh == a.mesh)
            .Select(b => (source: a, target: b))).OrderByDescending(p => p.source.mesh.bounds.size.sqrMagnitude).ToArray();
        if (matches.Length == 0) throw new InvalidOperationException("No matching receiver geometry; author GripFrame explicitly for " + target.name);
        var pair = matches[0];
        var registration = pair.target.matrix * pair.source.matrix.inverse;
        if (registration.determinant <= 0) throw new InvalidOperationException("Mirrored receiver frames require explicit calibration: " + target.name);
        var muzzle = source.effects ? source.effects.muzzleFlash : null;
        if (!muzzle) throw new InvalidOperationException("A calibrated muzzle is required: " + source.name);
        if (!source.gripFrame) source.gripFrame = Create(source, triggerKnuckle.position, muzzle.rotation, pair.source.matrix.lossyScale);
        if (!target.gripFrame) target.gripFrame = Create(target, registration.MultiplyPoint3x4(source.gripFrame.position),
            registration.rotation * source.gripFrame.rotation, pair.target.matrix.lossyScale);
    }
    static Transform Create(SoldierWeaponView view, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var frame = new GameObject("GripFrame").transform;
        frame.SetParent(view.transform, false);
        frame.SetPositionAndRotation(position, rotation);
        var parentScale = view.transform.lossyScale;
        frame.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
        return frame;
    }
    public static void Save(SoldierWeaponView instance, string path)
    {
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = prefab.GetComponent<SoldierWeaponView>();
            if (view.gripFrame) return; // Existing authored calibration is the source of truth.
            var frame = new GameObject("GripFrame").transform;
            frame.SetParent(view.transform, false);
            frame.SetLocalPositionAndRotation(instance.gripFrame.localPosition, instance.gripFrame.localRotation);
            frame.localScale = instance.gripFrame.localScale;
            view.gripFrame = frame;
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }
}
