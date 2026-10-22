using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Converts the review FPS prefabs to weapon-only equipment using one persistent
/// arms/skeleton prefab per reviewed proportions variant. Motion paths stay intact.
/// </summary>
public static class SharedWeaponRigBuilder
{
    public const string Folder = "Assets/Game/AnimationTests/UE4RigProof/Shared";
    public const string ScaledRigPath = "Assets/Game/Prefabs/Players/Visuals/ScaledFPSRig.prefab";
    public const string AdjustedRigPath = Folder + "/MeshAdjustedFPSRig.prefab";

    public sealed class Report
    {
        public int converted;
        public int alreadyShared;
        public readonly List<string> changedPaths = new List<string>();
    }

    public static Report Configure(IEnumerable<WeaponAnimationConversionProof> source)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Shared rig authoring requires Edit Mode after compilation");
        var proofs = source.Where(p => p && p.converted != null && p.converted.firstPersonPrefab).Distinct().ToArray();
        var catalogs = AssetDatabase.FindAssets("t:WeaponCatalog", new[] { "Assets/Game" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponCatalog>).ToArray();
        var report = new Report();
        EnsureFolder(Folder);
        foreach (var group in proofs.GroupBy(p => p.originalProportions))
        {
            string rigPath = group.Key ? ScaledRigPath : AdjustedRigPath;
            var shared = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
            var complete = group.FirstOrDefault(p => !p.converted.firstPersonPrefab.sharedFirstPersonRig);
            if (!shared)
            {
                if (!complete) throw new InvalidOperationException("No complete FPS rig remains to create " + rigPath);
                shared = CreateRig(complete.converted.firstPersonPrefab, rigPath);
                Changed(report, rigPath);
            }
            var configuration = shared.GetComponent<SoldierSharedFirstPersonRig>();
            if (!configuration || !configuration.animator || !configuration.weaponMount)
                throw new InvalidOperationException("Incomplete shared FPS rig: " + rigPath);
            foreach (var proof in group)
            {
                var existing = proof.converted.firstPersonPrefab;
                if (existing.sharedFirstPersonRig)
                {
                    // Do not re-extract an already weapon-only prefab on a warm refresh.
                    if (existing.sharedFirstPersonRig != shared)
                        throw new InvalidOperationException("Unexpected shared rig on " + AssetDatabase.GetAssetPath(existing));
                    if (proof.sharedFirstPersonRig != shared)
                    {
                        proof.sharedFirstPersonRig = shared; EditorUtility.SetDirty(proof);
                        Changed(report, AssetDatabase.GetAssetPath(proof));
                    }
                    report.alreadyShared++;
                    continue;
                }
                if (Vector3.Distance(existing.mountPosition, configuration.mountPosition) > 0.000001f ||
                    Quaternion.Angle(existing.mountRotation, configuration.mountRotation) > 0.0001f ||
                    Vector3.Distance(existing.mountScale, configuration.mountScale) > 0.000001f)
                    throw new InvalidOperationException("FPS frame differs from shared rig on " + AssetDatabase.GetAssetPath(existing));
                ConvertWeapon(proof, shared, proofs, catalogs, report);
                report.converted++;
            }
        }
        AssetDatabase.SaveAssets();
        return report;
    }

    static GameObject CreateRig(SoldierWeaponView source, string output)
    {
        var root = Clone(source.gameObject);
        try
        {
            var view = root.GetComponent<SoldierWeaponView>();
            var gun = FindWeapon(root.transform);
            var animator = view.animator;
            if (!animator || animator.gameObject != root)
                throw new InvalidOperationException("Expected root authored Animator on " + source.name);
            var mount = gun.parent;
            var position = view.mountPosition; var rotation = view.mountRotation; var scale = view.mountScale;
            Object.DestroyImmediate(gun.gameObject);
            // The persistent rig carries animation only. Muzzle, attachment, sight and
            // weapon configuration components belong to the equipped weapon object.
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is SoldierAuthoredWeaponMotion)) Object.DestroyImmediate(behaviour);
            foreach (var extra in root.GetComponentsInChildren<Animator>(true))
                if (extra != animator) Object.DestroyImmediate(extra);
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            var shared = root.AddComponent<SoldierSharedFirstPersonRig>();
            shared.animator = animator; shared.weaponMount = mount;
            shared.mountPosition = position; shared.mountRotation = rotation; shared.mountScale = scale;
            if (!root.GetComponent<SoldierAuthoredWeaponMotion>()) root.AddComponent<SoldierAuthoredWeaponMotion>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.bones.Any(b => !b)) throw new InvalidOperationException("Arms skin depends on removed gun bones: " + skin.name);
            root.name = Path.GetFileNameWithoutExtension(output);
            return PrefabUtility.SaveAsPrefabAsset(root, output);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void ConvertWeapon(WeaponAnimationConversionProof proof, GameObject shared,
        WeaponAnimationConversionProof[] proofs, WeaponCatalog[] catalogs, Report report)
    {
        var oldAssetView = proof.converted.firstPersonPrefab;
        string path = AssetDatabase.GetAssetPath(oldAssetView);
        // Capture references before replacing the prefab root/file IDs.
        var entries = new List<(WeaponCatalog catalog, WeaponCatalog.Entry entry)>();
        foreach (var catalog in catalogs)
            foreach (var entry in catalog.entries ?? Array.Empty<WeaponCatalog.Entry>())
                if (entry != null && entry.firstPersonPrefab == oldAssetView) entries.Add((catalog, entry));
        var related = new List<(WeaponAnimationConversionProof proof, WeaponCatalog.Entry entry)>();
        foreach (var other in proofs)
            foreach (var entry in new[] { other.source, other.converted })
                if (entry != null && entry.firstPersonPrefab == oldAssetView) related.Add((other, entry));
        var original = Clone(oldAssetView.gameObject);
        GameObject weapon = null;
        try
        {
            var oldView = original.GetComponent<SoldierWeaponView>();
            var sourceGun = FindWeapon(original.transform);
            weapon = Clone(sourceGun.gameObject); weapon.name = "SourceWeapon";
            var references = MapHierarchy(sourceGun, weapon.transform);
            // Root components such as SoldierWeaponSockets were outside SourceWeapon
            // in the old presentation. Keep them on the weapon, with remapped sockets.
            references[original] = weapon; references[original.transform] = weapon.transform;
            var pairs = new List<(Component source, Component target)>();
            foreach (var component in original.GetComponents<Component>())
            {
                if (!component || component is Transform || component is Animator ||
                    component is SoldierAuthoredWeaponMotion || component is SoldierSharedFirstPersonRig) continue;
                var target = weapon.AddComponent(component.GetType());
                references[component] = target; pairs.Add((component, target));
            }
            foreach (var pair in pairs) EditorUtility.CopySerialized(pair.source, pair.target);
            var view = weapon.GetComponent<SoldierWeaponView>();
            if (!view) throw new InvalidOperationException("Missing extracted weapon view on " + path);
            view.animator = null; view.sharedFirstPersonRig = shared;
            view.mountName = "SourceWeapon";
            view.sharedWeaponMountPath = AnimationUtility.CalculateTransformPath(sourceGun.parent, original.transform);
            view.mountPosition = sourceGun.localPosition; view.mountRotation = sourceGun.localRotation; view.mountScale = sourceGun.localScale;
            foreach (var component in weapon.GetComponentsInChildren<Component>(true))
            {
                if (!component || component is Transform) continue;
                Remap(component, references, original.transform);
            }
            var saved = PrefabUtility.SaveAsPrefabAsset(weapon, path).GetComponent<SoldierWeaponView>();
            if (!saved || !saved.sharedFirstPersonRig || !saved.effects || !saved.sightPlacementRoot)
                throw new InvalidOperationException("Extracted FPS weapon has missing references: " + path);
            proof.converted.firstPersonPrefab = saved; proof.sharedFirstPersonRig = shared; EditorUtility.SetDirty(proof);
            foreach (var item in entries)
            {
                item.entry.firstPersonPrefab = saved; EditorUtility.SetDirty(item.catalog);
                Changed(report, AssetDatabase.GetAssetPath(item.catalog));
            }
            foreach (var item in related)
            {
                item.entry.firstPersonPrefab = saved; EditorUtility.SetDirty(item.proof);
                Changed(report, AssetDatabase.GetAssetPath(item.proof));
            }
            Changed(report, path); Changed(report, AssetDatabase.GetAssetPath(proof));
        }
        finally
        {
            Object.DestroyImmediate(original);
            if (weapon) Object.DestroyImmediate(weapon);
        }
    }

    // Child names are not unique in imported models (for example duplicate RearSight
    // siblings). Cloning preserves sibling order, so structural indices are unambiguous.
    static Dictionary<Object, Object> MapHierarchy(Transform source, Transform target)
    {
        var result = new Dictionary<Object, Object>();
        void Map(Transform from, Transform to)
        {
            if ((from != source && from.name != to.name) || from.childCount != to.childCount)
                throw new InvalidOperationException("Extracted gun structure differs at " + from.name);
            result[from] = to; result[from.gameObject] = to.gameObject;
            foreach (var component in from.GetComponents<Component>())
            {
                if (!component) throw new InvalidOperationException("Missing script on gun child " + from.name);
                var sameType = from.GetComponents(component.GetType());
                int index = Array.IndexOf(sameType, component);
                var matches = to.GetComponents(component.GetType());
                if (index < 0 || index >= matches.Length) throw new InvalidOperationException("Component remap failed: " + from.name);
                result[component] = matches[index];
            }
            for (int i = 0; i < from.childCount; i++) Map(from.GetChild(i), to.GetChild(i));
        }
        Map(source, target);
        return result;
    }

    /// <summary>
    /// Repairs only references to the original gun/root components. Used for existing
    /// extracted prefabs when source meshes contain duplicate-named siblings. Scalar
    /// configuration, mount placement and the shared-rig binding are left untouched.
    /// Returns the number of corrected object references; saves only when changed.
    /// </summary>
    public static int RepairReferences(SoldierWeaponView originalComplete, SoldierWeaponView extracted)
    {
        if (!originalComplete || !extracted) throw new ArgumentNullException("Both weapon views are required");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Reference repair requires Edit Mode after compilation");
        string path = AssetDatabase.GetAssetPath(extracted);
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Extracted weapon must be a saved prefab");
        var current = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var originalGun = FindWeapon(originalComplete.transform);
            var map = MapHierarchy(originalGun, current.transform);
            map[originalComplete.gameObject] = current;
            map[originalComplete.transform] = current.transform;
            foreach (var component in originalComplete.GetComponents<Component>())
            {
                if (!component || component is Transform || component is Animator ||
                    component is SoldierAuthoredWeaponMotion || component is SoldierSharedFirstPersonRig) continue;
                var originals = originalComplete.GetComponents(component.GetType());
                int index = Array.IndexOf(originals, component);
                // Extracted root keeps original gun components before copied FPS-root components.
                int gunCount = originalGun.GetComponents(component.GetType()).Length;
                var targets = current.GetComponents(component.GetType());
                if (index + gunCount >= targets.Length)
                    throw new InvalidOperationException("Missing extracted root component " + component.GetType().Name);
                map[component] = targets[index + gunCount];
            }
            int changed = 0;
            var currentView = current.GetComponent<SoldierWeaponView>();
            string mountPath = AnimationUtility.CalculateTransformPath(originalGun.parent, originalComplete.transform);
            if (currentView.sharedWeaponMountPath != mountPath)
            {
                currentView.sharedWeaponMountPath = mountPath;
                changed++;
            }
            foreach (var item in map)
            {
                if (!(item.Key is Component sourceComponent) || sourceComponent is Transform ||
                    !(item.Value is Component targetComponent)) continue;
                var before = new SerializedObject(sourceComponent);
                var after = new SerializedObject(targetComponent);
                var property = before.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        property.propertyPath == "m_Script" || property.propertyPath == "m_GameObject" ||
                        (sourceComponent is SoldierWeaponView &&
                         (property.propertyPath == "animator" || property.propertyPath == "sharedFirstPersonRig"))) continue;
                    var value = property.objectReferenceValue;
                    if (!value || !map.TryGetValue(value, out var corrected)) continue;
                    var target = after.FindProperty(property.propertyPath);
                    if (target == null || target.propertyType != SerializedPropertyType.ObjectReference)
                        throw new InvalidOperationException("Missing extracted reference property " + property.propertyPath);
                    if (target.objectReferenceValue == corrected) continue;
                    target.objectReferenceValue = corrected; changed++;
                }
                after.ApplyModifiedPropertiesWithoutUndo();
            }
            if (changed > 0) PrefabUtility.SaveAsPrefabAsset(current, path);
            return changed;
        }
        finally { PrefabUtility.UnloadPrefabContents(current); }
    }

    static void Remap(Component component, Dictionary<Object, Object> references, Transform oldRoot)
    {
        var serialized = new SerializedObject(component);
        var property = serialized.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference || property.propertyPath == "m_Script" || property.propertyPath == "m_GameObject") continue;
            var value = property.objectReferenceValue;
            if (!value) continue;
            if (references.TryGetValue(value, out var replacement)) { property.objectReferenceValue = replacement; continue; }
            var transform = value is GameObject gameObject ? gameObject.transform : value is Component c ? c.transform : null;
            if (transform && (transform == oldRoot || transform.IsChildOf(oldRoot)))
                throw new InvalidOperationException(component.GetType().Name + "." + property.propertyPath + " still references removed FPS rig node " + transform.name);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static Transform FindWeapon(Transform root)
    {
        var matches = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "SourceWeapon").ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected one SourceWeapon under " + root.name + ", found " + matches.Length);
        return matches[0];
    }
    static GameObject Clone(GameObject source)
    {
        var clone = Object.Instantiate(source);
        if (PrefabUtility.IsPartOfPrefabInstance(clone)) PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        return clone;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static void Changed(Report report, string path) { if (!report.changedPaths.Contains(path)) report.changedPaths.Add(path); }
}

