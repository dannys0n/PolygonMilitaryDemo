using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Save static display geometry once. Warm Play checks compare dependency hashes only.
[InitializeOnLoad]
public static class ArmoryEditorCache
{
    const string Root="Assets/Game/AnimationTests/ArmoryDisplayCache";
    const string Version="display-v1";
    public static int LastRebuilt { get; private set; }
    public static int LastChecked { get; private set; }
    static ArmoryEditorCache()
    {
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorSceneManager.sceneSaving+=OnSave;
    }
    static void OnSave(Scene scene,string path)
    {
        // The saved scene owns its catalog path; generated cache GUIDs belong to this machine.
        var roots=scene.GetRootGameObjects();
        var walls=roots.SelectMany(r=>r.GetComponentsInChildren<ArmoryWall>(true)).Where(w=>w.lazyReview).ToArray();
        if(walls.Length==0)return;
        foreach(var wall in walls){if(string.IsNullOrEmpty(wall.sourceCatalogPath)&&wall.displayCache)wall.sourceCatalogPath=wall.displayCache.catalogPath;wall.displayCache=null;EditorUtility.SetDirty(wall);}
        foreach(var loader in roots.SelectMany(r=>r.GetComponentsInChildren<ArmoryReviewLoadout>(true))){loader.library=null;EditorUtility.SetDirty(loader);}
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.ExitingEditMode)return;
        try
        {
            LastRebuilt=LastChecked=0;
            foreach(var wall in Object.FindObjectsByType<ArmoryWall>(FindObjectsInactive.Include))
                if(wall.gameObject.scene.IsValid()&&wall.gameObject.scene.isLoaded)
                {
                    var cache=Ensure(wall);
                    if(wall.lazyReview)
                    {
                        foreach(var loader in wall.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ArmoryReviewLoadout>(true)))loader.library=cache;
                        var preview=wall.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<WeaponConversionPreview>(true)).FirstOrDefault();
                        if(preview){var items=cache.items.Where(i=>i.kind==ArmoryDisplayCache.Kind.Weapon&&!string.IsNullOrEmpty(i.proofPath)).ToArray();preview.proofPaths=items.Select(i=>i.proofPath).ToArray();preview.proofLabels=items.Select(i=>i.title).ToArray();}
                    }
                }
        }
        catch(Exception error){EditorApplication.isPlaying=false;Debug.LogException(error);}
    }
    static T Save<T>(T value,string path)where T:Object
    {
        var existing=AssetDatabase.LoadAssetAtPath<T>(path);
        if(existing){if(existing is Mesh mesh)mesh.Clear(false);EditorUtility.CopySerialized(value,existing);Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;}
        AssetDatabase.CreateAsset(value,path);return value;
    }
    static string[] DisplayFiles(string prefab)=>AssetDatabase.GetDependencies(prefab,true).Where(p=>new[]{".prefab",".asset",".fbx",".obj",".blend"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Append(prefab).Distinct().OrderBy(p=>p).ToArray();
    internal static string Stamp(IEnumerable<string> paths,Dictionary<string,string> memo)
    {
        var text=new System.Text.StringBuilder();
        foreach(string path in paths)
        {
            if(!memo.TryGetValue(path,out string value))
            {
                var file=new FileInfo(path);var meta=new FileInfo(path+".meta");
                value=(file.Exists?file.Length+":"+file.LastWriteTimeUtc.Ticks:"missing")+"|"+(meta.Exists?meta.LastWriteTimeUtc.Ticks.ToString():"missing");memo[path]=value;
            }
            text.Append(path).Append('|').Append(value).Append(';');
        }
        return Hash128.Compute(text.ToString()).ToString();
    }
    public static ArmoryDisplayCache Ensure(ArmoryWall wall,ArenaWeaponCatalog source=null)
    {
        if(wall.lazyReview)ArenaArmorySourceSights.Refresh();
        string path=source?AssetDatabase.GetAssetPath(source):wall.catalog?AssetDatabase.GetAssetPath(wall.catalog):!string.IsNullOrEmpty(wall.sourceCatalogPath)?wall.sourceCatalogPath:wall.displayCache?wall.displayCache.catalogPath:null;
        if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Armory has no source catalog: "+wall.name);
        wall.sourceCatalogPath=path;
        string folder=Root+"/"+AssetDatabase.AssetPathToGUID(path);if(!AssetDatabase.IsValidFolder(folder)){Directory.CreateDirectory(folder);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);}
        string output=folder+"/Index.asset";
        var cache=wall.displayCache?wall.displayCache:AssetDatabase.LoadAssetAtPath<ArmoryDisplayCache>(output);
        var memo=new Dictionary<string,string>();
        string signature=cache&&cache.sourceFiles!=null&&cache.sourceFiles.Length>0?Version+"|"+Stamp(cache.sourceFiles,memo)+"|"+wall.modelSize:null;
        LastChecked++;
        if(signature!=null&&cache&&cache.signature==signature&&cache.items.All(i=>i.display)&&(!wall.lazyReview||cache.items.Where(i=>i.kind==ArmoryDisplayCache.Kind.Weapon).All(i=>!string.IsNullOrEmpty(i.firstPersonPath)))){wall.displayCache=cache;return cache;}
        var catalog=source?source:AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>(path);
        var previous=cache?cache.items:Array.Empty<ArmoryDisplayCache.Item>();
        if(!cache){cache=ScriptableObject.CreateInstance<ArmoryDisplayCache>();AssetDatabase.CreateAsset(cache,output);}
        var entries=catalog.entries;
        var proofPaths=new Dictionary<string,string>();
        if(wall.lazyReview||path.Contains("OriginalProportionsCatalog"))
            foreach(string guid in AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{ArenaPolygonWeaponConversionBuilder.Root}))
            {
                string proofPath=AssetDatabase.GUIDToAssetPath(guid);var proof=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(proofPath);
                if(proof&&proof.converted?.weapon)proofPaths[proof.converted.weapon.catalogId]=proofPath;
            }
        // Snapshot view metadata before saving display prefabs can reimport their dependencies.
        var viewPaths=entries.ToDictionary(e=>e.weapon.catalogId,e=>(first:AssetDatabase.GetAssetPath(e.firstPersonPrefab),third:AssetDatabase.GetAssetPath(e.thirdPersonPrefab)));
        var sightRows=entries.SelectMany(e=>e.sights).Distinct().Where(s=>s&&s.prefab).OrderBy(s=>s.displayName)
            .Select(s=>(sight:s,compatible:entries.Where(e=>e.thirdPersonPrefab.GetComponent<SoldierWeaponSights>().Supports(s)).Select(e=>e.weapon.catalogId).ToArray())).ToArray();
        var attachmentRows=entries.SelectMany(e=>e.attachments).Distinct().Where(a=>a&&a.prefab).OrderBy(a=>a.mount).ThenBy(a=>a.displayName)
            .Select(a=>(attachment:a,compatible:entries.Where(e=>e.thirdPersonPrefab.GetComponent<SoldierWeaponAttachments>().Supports(a)).Select(e=>e.weapon.catalogId).ToArray())).ToArray();
        var items=new List<ArmoryDisplayCache.Item>();
        var holder=new GameObject("Armory cache baking");holder.SetActive(false);
        try
        {
            void Add(ArmoryDisplayCache.Kind kind,string key,string title,Object asset,GameObject prefab,string[] compatible)
            {
                var files=DisplayFiles(AssetDatabase.GetAssetPath(prefab));
                string hash=Version+"|"+Stamp(files,memo)+"|"+wall.modelSize;
                var old=Array.Find(previous,i=>i.key==key);
                var item=new ArmoryDisplayCache.Item{kind=kind,key=key,title=title,assetPath=AssetDatabase.GetAssetPath(asset),compatibleWeapons=compatible,sourceFiles=files,signature=hash,display=old?.display};
                if(proofPaths.TryGetValue(key,out string proof))item.proofPath=proof;
                if(kind==ArmoryDisplayCache.Kind.Weapon)
                {
                    var paths=viewPaths[key];
                    item.firstPersonPath=paths.first;item.thirdPersonPath=paths.third;
                }
                if(old==null||(old.sourceFiles!=null&&old.sourceFiles.Length>0&&old.signature!=hash)||!old.display)
                {
                    var model=wall.BakeModel(prefab,holder.transform,holder.transform);
                    string id=Hash128.Compute(key).ToString();int index=0;
                    foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mesh=filter.sharedMesh;
                        if(!mesh||AssetDatabase.Contains(mesh))continue;
                        filter.sharedMesh=Save(Object.Instantiate(mesh),folder+"/"+id+"_"+(index++)+".asset");
                    }
                    item.display=PrefabUtility.SaveAsPrefabAsset(model,folder+"/"+id+".prefab");Object.DestroyImmediate(model);LastRebuilt++;
                }
                items.Add(item);
            }
            foreach(var e in entries.OrderBy(e=>e.weapon.displayName))Add(ArmoryDisplayCache.Kind.Weapon,e.weapon.catalogId,e.weapon.displayName,e.weapon,e.basePrefab,Array.Empty<string>());
            foreach(var row in sightRows){var sight=row.sight;
                Add(ArmoryDisplayCache.Kind.Sight,"sight:"+sight.name,sight.displayName,sight,sight.prefab.gameObject,row.compatible);}
            foreach(var row in attachmentRows){var attachment=row.attachment;
                Add(ArmoryDisplayCache.Kind.Attachment,"attachment:"+attachment.name,attachment.displayName,attachment,attachment.prefab,row.compatible);}
        }
        finally{Object.DestroyImmediate(holder);}
        cache.items=items.ToArray();cache.catalogPath=path;
        cache.sourceFiles=items.SelectMany(i=>i.sourceFiles).Concat(items.Select(i=>i.assetPath)).Append(path).Distinct().OrderBy(p=>p).ToArray();
        cache.signature=Version+"|"+Stamp(cache.sourceFiles,memo)+"|"+wall.modelSize;
        cache.defaultWeapon=entries.FirstOrDefault(e=>e.weapon.catalogId.Contains("mg5")||e.weapon.catalogId.EndsWith("mgx5_ue4"))?.weapon.catalogId??entries[0].weapon.catalogId;
        wall.displayCache=cache;EditorUtility.SetDirty(cache);EditorUtility.SetDirty(wall);AssetDatabase.SaveAssets();return cache;
    }
    public static void ConfigureReview(Scene scene,ArenaWeaponCatalog catalog)
    {
        var roots=scene.GetRootGameObjects();var wall=roots.SelectMany(r=>r.GetComponentsInChildren<ArmoryWall>(true)).Single();
        wall.lazyReview=true;wall.sourceCatalogPath=AssetDatabase.GetAssetPath(catalog);var cache=Ensure(wall,catalog);wall.catalog=null;
        var armory=roots.SelectMany(r=>r.GetComponentsInChildren<ArmoryController>(true)).Single();
        var loadout=armory.loadout;
        // Review players own their scene setup; inherited catalog overrides pull every rig into memory.
        var instance=PrefabUtility.GetOutermostPrefabInstanceRoot(loadout.gameObject);
        if(instance)PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        if(loadout.TryGetComponent<SoldierWeaponCatalogBinding>(out var binding))Object.DestroyImmediate(binding);
        var loader=loadout.GetComponent<ArmoryReviewLoadout>();if(!loader)loader=loadout.gameObject.AddComponent<ArmoryReviewLoadout>();
        loader.library=cache;loader.loadout=loadout;loadout.slots=Array.Empty<SoldierWeaponLoadout.Slot>();loadout.selectedSlot=0;if(PrefabUtility.IsPartOfPrefabInstance(loadout))PrefabUtility.RecordPrefabInstancePropertyModifications(loadout);EditorUtility.SetDirty(loadout);EditorUtility.SetDirty(loader);
        var preview=roots.SelectMany(r=>r.GetComponentsInChildren<WeaponConversionPreview>(true)).Single();
        var weapons=cache.items.Where(i=>i.kind==ArmoryDisplayCache.Kind.Weapon&&!string.IsNullOrEmpty(i.proofPath)).ToArray();
        preview.proofs=Array.Empty<WeaponAnimationConversionProof>();preview.proofPaths=weapons.Select(i=>i.proofPath).ToArray();preview.proofLabels=weapons.Select(i=>i.title).ToArray();
        EditorSceneManager.MarkSceneDirty(scene);
    }
    [MenuItem("Tools/Arena Shooter/Animation Tests/Refresh armory displays")]
    public static void RefreshOpen()
    {ArenaArmoryCatalogs.Refresh();LastRebuilt=LastChecked=0;foreach(var wall in Object.FindObjectsByType<ArmoryWall>(FindObjectsInactive.Include))Ensure(wall);Debug.Log("Armory cache: "+LastRebuilt+" changed displays rebuilt.");}
}
