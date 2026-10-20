using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Source reference edits update generated sockets only, without rebaking weapon motion.
public static class ArenaArmorySourceSights
{
    const string Root = ArenaPolygonWeaponConversionBuilder.Root;
    static string CachePath => Path.GetFullPath("Library/ArmorySourceSights.json");
    [Serializable] sealed class Record { public string proof, fingerprint; public string[] files; }
    [Serializable] sealed class Records { public List<Record> items = new(); }
    struct Pose { public Vector3 position; public Quaternion rotation; }
    sealed class Source
    {
        public Pose rear, front, optic;
        public WeaponAttachmentSocket socket;
    }
    static bool refreshing;
    public static int LastUpdated { get; private set; }

    static Transform Gun(GameObject go)
    {
        var view=go.GetComponent<SoldierWeaponView>();
        return view&&view.sharedFirstPersonRig?go.transform:go.GetComponentsInChildren<Transform>(true).First(t=>t.name=="SourceWeapon");
    }
    static Pose Read(Transform gun, Transform marker) => new Pose
    { position = gun.InverseTransformPoint(marker.position), rotation = Quaternion.Inverse(gun.rotation) * marker.rotation };
    static bool Set(Transform gun, Transform marker, Pose pose)
    {
        if (!marker) return false;
        var position = gun.TransformPoint(pose.position); var rotation = gun.rotation * pose.rotation;
        if (Vector3.Distance(marker.position, position) < .000001f && Quaternion.Angle(marker.rotation, rotation) < .001f) return false;
        marker.SetPositionAndRotation(position, rotation); return true;
    }
    static bool CopyMount(WeaponAttachmentSocket from, WeaponAttachmentSocket to)
    {
        if (!from || !to) return false;
        // Renderer references belong to each generated rig, so retain those locally.
        var before = JsonUtility.ToJson(new MountData(to));
        var data = new MountData(from);
        if (before == JsonUtility.ToJson(data)) return false;
        to.supportedSightMounts = data.supported; to.maximumSightFootprint = data.footprint;
        to.placements = data.placements; to.requiredSightSupports = data.supports;
        return true;
    }
    [Serializable] sealed class MountData
    {
        public WeaponSightDefinition.MountStandard supported;
        public WeaponSightDefinition.RailFootprint footprint;
        public WeaponAttachmentSocket.Placement[] placements;
        public WeaponAttachmentSocket.SightSupport[] supports;
        public MountData(WeaponAttachmentSocket socket)
        { supported = socket.supportedSightMounts; footprint = socket.maximumSightFootprint; placements = socket.placements; supports = socket.requiredSightSupports; }
    }
    static bool UpdateView(SoldierWeaponView prefab, AnimationClip idle, Source source)
    {
        string path = AssetDatabase.GetAssetPath(prefab); var go = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = go.GetComponent<SoldierWeaponView>();
            if (idle && !view.sharedFirstPersonRig) idle.SampleAnimation(go, 0);
            var gun = Gun(go);
            bool changed = Set(gun, view.rearSight, source.rear) | Set(gun, view.frontSight, source.front) | Set(gun, view.opticMount, source.optic);
            changed |= CopyMount(source.socket, view.opticMount.GetComponent<WeaponAttachmentSocket>());
            if (changed) PrefabUtility.SaveAsPrefabAsset(go, path);
            return changed;
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
    static void UpdateBody(WeaponAnimationConversionProof proof, Source source)
    {
        if (!proof.sharedBodyWeaponPrefab && !proof.thirdPersonProof) return;
        var fp = proof.converted.firstPersonPrefab; var fpGun = Gun(fp.gameObject);
        var relative = new[] { fp.rearSight, fp.frontSight, fp.opticMount }.Select(t => AnimationUtility.CalculateTransformPath(t, fpGun)).ToArray();
        bool weaponOnly=proof.sharedBodyWeaponPrefab;
        string path = AssetDatabase.GetAssetPath(weaponOnly?proof.sharedBodyWeaponPrefab:proof.thirdPersonProof); var go = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var gun = weaponOnly?go.transform:Gun(go); var markers = relative.Select(gun.Find).ToArray();
            if (markers.Any(t => !t)) throw new InvalidOperationException("Missing body sight references: " + path);
            var markerPaths = markers.Select(t => weaponOnly?"SourceWeapon/"+AnimationUtility.CalculateTransformPath(t,gun):AnimationUtility.CalculateTransformPath(t,go.transform)).ToHashSet();
            // Mechanisms animate their parents. Socket calibration must remain authored prefab data.
            foreach (var clip in new[] { proof.bodyIdle, proof.bodyReload }.Where(c => c))
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform) && markerPaths.Contains(b.path)))
                { AnimationUtility.SetEditorCurve(clip, binding, null); EditorUtility.SetDirty(clip); }
            if (proof.bodyIdle && !weaponOnly) proof.bodyIdle.SampleAnimation(go, 0);
            bool changed = Set(gun, markers[0], source.rear) | Set(gun, markers[1], source.front) | Set(gun, markers[2], source.optic);
            changed |= CopyMount(source.socket, markers[2].GetComponent<WeaponAttachmentSocket>());
            if (changed) PrefabUtility.SaveAsPrefabAsset(go, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
    public static int Refresh()
    {
        if (refreshing || EditorApplication.isPlaying) return 0;
        refreshing = true; LastUpdated = 0;
        try
        {
            Records state;
            try { state = File.Exists(CachePath) ? JsonUtility.FromJson<Records>(File.ReadAllText(CachePath)) : new Records(); }
            catch { state = new Records(); }
            var memo = new Dictionary<string, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponAnimationConversionProof", new[] { Root + "/Native" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var record = state.items.FirstOrDefault(r => r.proof == path);
                string Fingerprint(string[] files) => "native-sights-v1|" + ArmoryEditorCache.Stamp(files, memo);
                if (record != null && record.fingerprint == Fingerprint(record.files)) continue;
                var proof = AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(path);
                if (!proof.source.weapon.catalogId.StartsWith("animation-test-", StringComparison.Ordinal)) continue;
                var authored = proof.source.firstPersonPrefab; var gun = Gun(authored.gameObject);
                var references = gun.Find("SightReferences");
                var rear = authored.rearSight ? authored.rearSight : references ? references.Find("RearSight") : null;
                var front = authored.frontSight ? authored.frontSight : references ? references.Find("FrontSight") : null;
                var optic = authored.opticMount ? authored.opticMount : references ? references.Find("OpticMount") : null;
                if (!rear || !front || !optic) throw new InvalidOperationException("Incomplete source sight references: " + AssetDatabase.GetAssetPath(authored));
                var source = new Source { rear = Read(gun, rear), front = Read(gun, front), optic = Read(gun, optic), socket = optic.GetComponent<WeaponAttachmentSocket>() };
                bool changed = UpdateView(proof.converted.firstPersonPrefab, proof.idle, source);
                changed |= UpdateView(proof.converted.thirdPersonPrefab, null, source);
                var paired = AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(Root + "/OriginalProportions/" + proof.converted.weapon.catalogId + "/Conversion.asset");
                if (paired)
                {
                    changed |= UpdateView(paired.converted.firstPersonPrefab, paired.idle, source);
                    changed |= UpdateView(paired.converted.thirdPersonPrefab, null, source);
                }
                if (changed) { UpdateBody(proof, source); LastUpdated++; }
                var files = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(authored), true).Where(p => p.EndsWith(".prefab", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (record == null) { record = new Record { proof = path }; state.items.Add(record); }
                record.files = files; record.fingerprint = Fingerprint(files);
            }
            if (LastUpdated > 0) AssetDatabase.SaveAssets();
            File.WriteAllText(CachePath, JsonUtility.ToJson(state));
            return LastUpdated;
        }
        finally { refreshing = false; }
    }
}
