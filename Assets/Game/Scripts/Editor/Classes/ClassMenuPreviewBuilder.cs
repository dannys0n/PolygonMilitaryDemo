using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using Object=UnityEngine.Object;

// Explicit authoring refresh: derive preview art and metadata from the gameplay source assets.
public static class ClassMenuPreviewBuilder
{
    public static string EditorPrefab="Assets/Game/Prefabs/UI/Resources/ArenaClassEditor.prefab";
    public static string Output="Assets/Game/Prefabs/Weapons/Previews";
    public static string CatalogAsset="Assets/Game/Definitions/UI/ClassMenuCatalog.asset";
    static AddressableAssetSettings settings;
    static AddressableAssetGroup group;
    static readonly Dictionary<string,string> previews=new Dictionary<string,string>();
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var split=path.LastIndexOf('/');Folder(path.Substring(0,split));AssetDatabase.CreateFolder(path.Substring(0,split),path.Substring(split+1));}
    [MenuItem("Tools/Game/Refresh Class Menu Previews")]
    public static void RefreshPreviews(){Generate();Wire();}
    public static object Generate()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit Mode required");
        previews.Clear();
        Folder(Output);Folder(System.IO.Path.GetDirectoryName(CatalogAsset).Replace('\\','/'));
        settings=AddressableAssetSettingsDefaultObject.Settings;if(!settings)settings=AddressableAssetSettings.Create("Assets/AddressableAssetsData","AddressableAssetSettings",true,true);
        AddressableAssetSettingsDefaultObject.Settings=settings;
        group=settings.FindGroup("Class Preview Models")??settings.CreateGroup("Class Preview Models",false,false,true,null,typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
        group.GetSchema<BundledAssetGroupSchema>().BundleMode=BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        var root=PrefabUtility.LoadPrefabContents(EditorPrefab);
        try
        {
            var inputs=AssetDatabase.LoadAssetAtPath<ClassMenuSources>("Assets/Game/Definitions/Editor/ClassMenuSources.asset");
            if(!inputs||!inputs.catalog)throw new InvalidOperationException("Configure ClassMenuSources catalog first");
            var gameplay=inputs.catalog;
            var icons=inputs.icons.Where(i=>i.asset).ToDictionary(i=>i.asset,i=>i.icon);
            var views=inputs.views.Where(v=>v.weapon).ToDictionary(v=>v.weapon,v=>v.view);
            var catalog=AssetDatabase.LoadAssetAtPath<ArenaClassMenuCatalog>(CatalogAsset);
            if(!catalog){catalog=ScriptableObject.CreateInstance<ArenaClassMenuCatalog>();AssetDatabase.CreateAsset(catalog,CatalogAsset);}
            var allParts=new List<ArenaClassMenuCatalog.Part>();
            foreach(var sight in gameplay.entries.SelectMany(e=>e.sights).Where(s=>s).Distinct())
                allParts.Add(new ArenaClassMenuCatalog.Part{id=sight.id,name=sight.displayName,optic=true,icon=icons.TryGetValue(sight,out var icon)?icon:null,previewAddress=sight.prefab?Preview(sight.prefab.gameObject,"optic-"+sight.id):null});
            var attachments=gameplay.entries.SelectMany(e=>e.attachments).Where(a=>a).Distinct().ToList();
            foreach(var entry in gameplay.entries)foreach(var support in entry.basePrefab.GetComponentsInChildren<WeaponAttachmentSocket>(true).SelectMany(s=>s.requiredSightSupports))if(support.riser&&!attachments.Contains(support.riser))attachments.Add(support.riser);
            foreach(var a in attachments)allParts.Add(new ArenaClassMenuCatalog.Part{id=a.id,name=a.displayName,mount=a.mount,icon=icons.TryGetValue(a,out var icon)?icon:null,previewAddress=Preview(a.prefab,"part-"+a.id),position=a.positionOffset,rotation=a.rotationOffset,scale=a.scale,sightLift=a.sightLift});
            catalog.parts=allParts.ToArray();
            var weapons=new List<ArenaClassMenuCatalog.Weapon>();
            foreach(var entry in gameplay.entries)
            {
                var weapon=entry.weapon;var sockets=new List<ArenaClassMenuCatalog.Socket>();
                foreach(var mapping in new[]{("MuzzleMount",WeaponAttachmentDefinition.MountKind.Muzzle),("RailMount",WeaponAttachmentDefinition.MountKind.Rail),("LeftRailMount",WeaponAttachmentDefinition.MountKind.RailLeft),("GripMount",WeaponAttachmentDefinition.MountKind.Grip),("RearIronMount",WeaponAttachmentDefinition.MountKind.RearIron),("FrontIronMount",WeaponAttachmentDefinition.MountKind.FrontIron),("OpticMount",WeaponAttachmentDefinition.MountKind.SightRiser),("HandguardMount",WeaponAttachmentDefinition.MountKind.Handguard)})
                {
                    var transform=entry.basePrefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==mapping.Item1);if(!transform)continue;
                    var socket=new ArenaClassMenuCatalog.Socket{path=AnimationUtility.CalculateTransformPath(transform,entry.basePrefab.transform),mount=mapping.Item2};
                    if(transform.TryGetComponent<WeaponAttachmentSocket>(out var calibration))
                    {
                        socket.placements=calibration.placements.Where(p=>p.attachment).Select(p=>new ArenaClassMenuCatalog.Placement{part=p.attachment.id,position=p.position,rotation=p.rotation,scale=p.scale}).ToArray();
                        socket.supports=calibration.requiredSightSupports.Where(s=>s.sight).Select(s=>new ArenaClassMenuCatalog.Support{sight=s.sight.id,riser=s.riser?s.riser.id:null,contactPoint=s.contactPoint,scale=s.supportScale}).ToArray();
                    }
                    sockets.Add(socket);
                }
                views.TryGetValue(weapon,out var view);var source=view?view.GetComponent<SoldierWeaponAttachments>():null;
                var built=source?source.mounts.Select(m=>new ArenaClassMenuCatalog.BuiltIn{mount=m.kind,rendererNames=m.builtInThirdPerson.Where(r=>r).Select(r=>r.name).ToArray()}).ToArray():Array.Empty<ArenaClassMenuCatalog.BuiltIn>();
                weapons.Add(new ArenaClassMenuCatalog.Weapon{id=weapon.catalogId,name=weapon.displayName,category=weapon.category,damage=weapon.damage,rpm=weapon.roundsPerMinute,rounds=weapon.magazineCapacity,icon=weapon.icon,previewAddress=Preview(entry.basePrefab,"weapon-"+weapon.catalogId),defaultSight=entry.defaultSight?entry.defaultSight.id:null,sights=entry.sights.Where(s=>s).Select(s=>s.id).ToArray(),attachments=entry.attachments.Where(a=>a).Select(a=>a.id).ToArray(),sockets=sockets.ToArray(),builtIns=built});
            }
            foreach(var entry in group.entries.ToArray())
                if(entry.address.StartsWith("class-preview/")&&!previews.ContainsKey(entry.address))settings.RemoveAssetEntry(entry.guid);
            catalog.weapons=weapons.ToArray();EditorUtility.SetDirty(catalog);EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
            return new{catalog=CatalogAsset,weapons=weapons.Count,parts=allParts.Count,previews=previews.Count};
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static string Preview(GameObject source,string id)
    {
        if(!source)return null;var address="class-preview/"+id;
        if(previews.ContainsKey(address))return address;
        var clone=Object.Instantiate(source);clone.name=source.name;
        try
        {
            foreach(var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(behaviour);
            foreach(var t in clone.GetComponentsInChildren<Transform>(true))
                foreach(var c in t.GetComponents<Component>().Reverse())
                    if(c&&!(c is Transform)&&!(c is MeshFilter)&&!(c is MeshRenderer)&&!(c is SkinnedMeshRenderer))Object.DestroyImmediate(c);
            var directory=id.StartsWith("weapon-")?Output:Output+"/Attachments";Folder(directory);
            // Keep the existing GUID/address when a preview is renamed for authoring clarity.
            var existing=group.entries.FirstOrDefault(e=>e.address==address);
            var path=existing==null?null:AssetDatabase.GUIDToAssetPath(existing.guid);
            if(string.IsNullOrEmpty(path))
            {
                var label=id.StartsWith("weapon-")?source.name.Replace("_Scaled_Base","").Replace("_Base","")+"_Preview":id;
                path=directory+"/"+string.Concat(label.Select(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'?c:'_'))+".prefab";
            }
            clone.name=System.IO.Path.GetFileNameWithoutExtension(path);
            PrefabUtility.SaveAsPrefabAsset(clone,path);
            var asset=settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path),group);asset.address=address;previews[address]=path;return address;
        }
        finally{Object.DestroyImmediate(clone);}
    }
    public static object Wire()
    {
        var root=PrefabUtility.LoadPrefabContents(EditorPrefab);
        try{var editor=new SerializedObject(root.GetComponent<ArenaClassEditor>());editor.FindProperty("menuCatalog").objectReferenceValue=AssetDatabase.LoadAssetAtPath<ArenaClassMenuCatalog>(CatalogAsset);editor.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,EditorPrefab);return EditorPrefab;}
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}
