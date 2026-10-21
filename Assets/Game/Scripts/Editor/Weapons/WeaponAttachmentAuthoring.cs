using System;
using System.Linq;
using UnityEngine;
using UnityEditor;

// Base prefabs are the authoring source; animated views retain their bone-bound transforms.
public static class WeaponAttachmentAuthoring
{
    [MenuItem("Tools/Game/Sync Weapon Attachment Mounts")]
    public static void SyncMenu()=>Sync();
    static Mesh Mesh(Renderer renderer)=>renderer is SkinnedMeshRenderer skin?skin.sharedMesh:renderer.GetComponent<MeshFilter>()?.sharedMesh;
    static Matrix4x4 MeshSpace(Renderer renderer)
    {
        if(renderer is SkinnedMeshRenderer skin&&skin.bones.Length>0)
        {
            var scores=new float[skin.bones.Length];
            foreach(var weight in skin.sharedMesh.boneWeights){scores[weight.boneIndex0]+=weight.weight0;scores[weight.boneIndex1]+=weight.weight1;scores[weight.boneIndex2]+=weight.weight2;scores[weight.boneIndex3]+=weight.weight3;}
            int index=Array.IndexOf(scores,scores.Max());return skin.bones[index].localToWorldMatrix*skin.sharedMesh.bindposes[index];
        }
        return renderer.localToWorldMatrix;
    }
    public static int Sync()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit Mode required");
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        int count=0;
        foreach(var entry in catalog.entries.Where(e=>e.basePrefab&&e.attachments.Length>0))
        {
            foreach(var prefab in new[]{entry.firstPersonPrefab,entry.thirdPersonPrefab}.Where(v=>v))
            {
                string path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var originals=entry.basePrefab.GetComponentsInChildren<Renderer>(true).Where(r=>Mesh(r)).ToArray();
                    var pair=root.GetComponentsInChildren<Renderer>(true).Where(r=>Mesh(r)).Select(r=>(target:r,source:originals.FirstOrDefault(o=>Mesh(o)==Mesh(r)))).Where(p=>p.source).OrderByDescending(p=>Mesh(p.target).vertexCount).FirstOrDefault();
                    if(!pair.target)continue;var map=MeshSpace(pair.target)*MeshSpace(pair.source).inverse;var view=root.GetComponent<SoldierWeaponView>();var renderers=root.GetComponentsInChildren<Renderer>(true);
                    foreach(var source in entry.basePrefab.GetComponentsInChildren<Transform>(true).Where(t=>t.name.EndsWith("Mount")))
                    {
                        var target=source.name=="OpticMount"?view.opticMount:view.attachments.FirstOrDefault(a=>a.socket&&a.socket.name==source.name)?.socket;if(!target)continue;
                        var pose=map*source.localToWorldMatrix;target.SetPositionAndRotation(pose.GetColumn(3),pose.rotation);PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                        var calibration=source.GetComponent<WeaponAttachmentSocket>();if(!calibration)continue;
                        var copy=target.GetComponent<WeaponAttachmentSocket>();if(!copy)copy=target.gameObject.AddComponent<WeaponAttachmentSocket>();copy.placements=(WeaponAttachmentSocket.Placement[])calibration.placements.Clone();copy.requiredSightSupports=(WeaponAttachmentSocket.SightSupport[])calibration.requiredSightSupports.Clone();copy.builtInSight=calibration.builtInSight;copy.builtInSightRenderers=calibration.builtInSightRenderers.Where(r=>r).Select(r=>renderers.FirstOrDefault(t=>t.name==r.name)).Where(r=>r).ToArray();copy.opticMountRenderers=calibration.opticMountRenderers.Where(r=>r).Select(r=>renderers.FirstOrDefault(t=>t.name==r.name)).Where(r=>r).ToArray();copy.sideMountSights=(WeaponSightDefinition[])calibration.sideMountSights.Clone();PrefabUtility.RecordPrefabInstancePropertyModifications(copy);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);count++;
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        SyncSightCompatibility();
        return count;
    }
    [MenuItem("Tools/Game/Sync Optic Compatibility")]
    public static void SyncSightCompatibilityMenu()=>SyncSightCompatibility();
    public static int SyncSightCompatibility()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit Mode required");
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        var all=AssetDatabase.FindAssets("t:WeaponSightDefinition",new[]{"Assets/Game"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<WeaponSightDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(s=>s).ToArray();
        int count=0;
        foreach(var entry in catalog.entries.Where(e=>e.basePrefab&&e.sights.Length>0))
        {
            var source=entry.basePrefab.GetComponentsInChildren<WeaponAttachmentSocket>(true).FirstOrDefault(s=>s.name=="OpticMount");
            var bare=entry.sights.FirstOrDefault(s=>s&&!s.prefab);
            if(!bare)throw new InvalidOperationException(entry.weapon.name+" has no bare sight.");
            entry.sights=new[]{bare}.Concat(all.Where(s=>s.prefab&&source&&source.SupportsSight(s))).Distinct().ToArray();
            if(!entry.defaultSight||!entry.sights.Contains(entry.defaultSight))entry.defaultSight=bare;
            foreach(var prefab in new[]{entry.firstPersonPrefab,entry.thirdPersonPrefab}.Where(p=>p).Distinct())
            {
                var path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var sights=root.GetComponent<SoldierWeaponSights>();
                    if(sights){sights.compatibleSights=entry.sights;sights.defaultSight=entry.defaultSight;}
                    var view=root.GetComponent<SoldierWeaponView>();
                    if(source&&view.opticMount)
                    {
                        var target=view.opticMount.GetComponent<WeaponAttachmentSocket>();
                        if(!target)target=view.opticMount.gameObject.AddComponent<WeaponAttachmentSocket>();
                        target.supportedSightMounts=source.supportedSightMounts;
                        target.maximumSightFootprint=source.maximumSightFootprint;
                        target.builtInSight=source.builtInSight;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);count++;
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        EditorUtility.SetDirty(catalog);
        var menu=AssetDatabase.LoadAssetAtPath<ArenaClassMenuCatalog>(ClassMenuPreviewBuilder.CatalogAsset);
        if(menu){menu.RefreshWeaponFacts();EditorUtility.SetDirty(menu);}
        AssetDatabase.SaveAssets();
        return count;
    }
}
