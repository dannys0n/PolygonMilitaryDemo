using System;using System.Linq;using System.IO;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;

// The comparison shares authored poses, controllers, weapon geometry and sockets.
// Only the Polygon arms surface differs; neither entry uses the old player rig.
public static class ArenaWeaponProportionComparisonBuilder
{
 const string Root=ArenaPolygonWeaponConversionBuilder.Root;
 [Serializable] sealed class PairStamp{public string path,fingerprint,output;public string[] sourceFiles;}
 [Serializable] sealed class PairStamps{public System.Collections.Generic.List<PairStamp> items=new();}
 static string StampPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/ArmoryPairCache.json"));
 const string Catalog="Assets/Game/AnimationTests/UE4RigProof/OriginalProportionsCatalog.asset";
 static T Save<T>(T value,string path)where T:Object
 {value.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(value,path);return value;}
 static GameObject Clone(GameObject prefab){var g=Object.Instantiate(prefab);if(PrefabUtility.IsPartOfPrefabInstance(g))PrefabUtility.UnpackPrefabInstance(g,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);return g;}
 public static WeaponAnimationConversionProof[] Fitted()=>AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{Root}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>).Where(p=>!p.originalProportions).ToArray();
 public static string Label(WeaponAnimationConversionProof p)=>p.source.weapon.displayName.Replace(" (UE4)","")
  +(p.source.weapon.catalogId.StartsWith("animation-test-")?" [FPS Pack]":"");
 [MenuItem("Tools/Arena Shooter/Animation Tests/Update paired proportions armory")]
 public static void Rebuild()=>Main();
 public static object Main()=>Build(true);
 public static object RefreshPairs()=>Build(false);
 static object Build(bool regenerateMotion)
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new Exception("Use Edit Mode after compilation");
  ArenaArmorySourceSights.Refresh();
  var catalog=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>(Catalog);var previous=catalog.entries;
  var original=AssetDatabase.LoadAssetAtPath<GameObject>(ArenaUe4RigProofBuilder.OriginalPreviewRigPath).GetComponentInChildren<SkinnedMeshRenderer>(true);
  var fittedMesh=AssetDatabase.LoadAssetAtPath<GameObject>(ArenaUe4RigProofBuilder.PreviewRigPath).GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
  int count=0;
  var paths=AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{Root}).Select(AssetDatabase.GUIDToAssetPath).Where(path=>!path.StartsWith(Root+"/OriginalProportions/",StringComparison.Ordinal)).ToArray();
  PairStamps stamps;
  try{stamps=File.Exists(StampPath)?JsonUtility.FromJson<PairStamps>(File.ReadAllText(StampPath)):new PairStamps();}catch{stamps=new PairStamps();}
  var stampMemo=new System.Collections.Generic.Dictionary<string,string>();
  string meshHash=ArmoryEditorCache.Stamp(AssetDatabase.GetDependencies(ArenaUe4RigProofBuilder.OriginalPreviewRigPath,true).OrderBy(sourcePath=>sourcePath,StringComparer.Ordinal),stampMemo);
  foreach(var profilePath in paths)
  {
   var stamp=stamps.items.FirstOrDefault(i=>i.path==profilePath);
   var files=stamp?.sourceFiles??AssetDatabase.GetDependencies(profilePath,true).OrderBy(sourcePath=>sourcePath,StringComparer.Ordinal).ToArray();
   string Fingerprint(string[] sources)=>"paired-v3|"+ArmoryEditorCache.Stamp(sources,stampMemo)+"|"+meshHash;
   string fingerprint=Fingerprint(files);
   if(!regenerateMotion&&stamp!=null&&stamp.fingerprint==fingerprint&&File.Exists(stamp.output))continue;
   // Saving a source prefab can leave Unity's native dependency hash stale this session.
   // File stamps catch the change; refresh the dependency list when it actually changes.
   files=AssetDatabase.GetDependencies(profilePath,true).OrderBy(sourcePath=>sourcePath,StringComparer.Ordinal).ToArray();fingerprint=Fingerprint(files);
   var p=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(profilePath);
   var productionPair=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(Root+"/OriginalProportions/"+p.converted.weapon.catalogId+"/Conversion.asset");
   if(productionPair && productionPair.source.weapon==productionPair.converted.weapon)continue;
   var entry=p.converted;string folder=Path.GetDirectoryName(AssetDatabase.GetAssetPath(p)).Replace('\\','/');
   // Regenerate the shared motion wrapper without re-solving the accepted reload.
   AnimationClip Raw(string name)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/"+name+"_UE4.anim");
   if(regenerateMotion)ArenaMk18GameplayBuilder.Controller(entry.weapon,Raw("Idle"),Raw("Reload"),Raw("Fire"),Raw("AimFire")?Raw("AimFire"):Raw("Fire"),folder,entry.animationStyle.StartsWith("Native UE4"),ArenaSharedAdsRecoil.Supports(p.source.weapon));

   string id="ue4-original-proportions-"+(p.source.weapon.catalogId.StartsWith("animation-test-")?p.source.weapon.catalogId.Substring("animation-test-".Length).ToLowerInvariant():entry.weapon.catalogId);
   string output=Root+"/OriginalProportions/"+entry.weapon.catalogId;
   var old=previous.FirstOrDefault(e=>e.weapon&&e.weapon.catalogId==id);
   if(stamp==null){stamp=new PairStamp{path=profilePath};stamps.items.Add(stamp);}
   stamp.fingerprint=fingerprint;stamp.sourceFiles=files;stamp.output=output+"/Conversion.asset";
   var existingProof=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(output+"/Conversion.asset");
   // Production assets are now authoritative; review rebuilding must not replace them.
   if(existingProof && existingProof.source.weapon==existingProof.converted.weapon)continue;
   if(!regenerateMotion&&old!=null&&existingProof&&existingProof.comparisonFingerprint==fingerprint)continue;
   string label=Label(p)+" (Mesh Adjusted - Deprecated)";if(entry.weapon.displayName!=label){entry.weapon.displayName=label;EditorUtility.SetDirty(entry.weapon);}
   if(!AssetDatabase.IsValidFolder(output)){Directory.CreateDirectory(output);AssetDatabase.Refresh();}
   var def=Object.Instantiate(entry.weapon);def.catalogId=id;def.displayName=Label(p)+" (Scaled)";def=Save(def,old!=null?AssetDatabase.GetAssetPath(old.weapon):output+"/Weapon.asset");
   var fps=Clone(entry.firstPersonPrefab.gameObject);var tps=Clone(entry.thirdPersonPrefab.gameObject);
   try
   {
    var sourceView=fps.GetComponent<SoldierWeaponView>();
    if(sourceView.sharedFirstPersonRig)
    {
     sourceView.sharedFirstPersonRig=AssetDatabase.LoadAssetAtPath<GameObject>(ArenaSharedWeaponRigBuilder.ScaledRigPath);
     if(!sourceView.sharedFirstPersonRig)throw new InvalidOperationException("Missing shared scaled FPS rig");
    }
    else
    {
    var skin=fps.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s=>s.sharedMesh==fittedMesh);
    var bones=fps.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
    skin.sharedMesh=original.sharedMesh;skin.sharedMaterials=original.sharedMaterials;skin.bones=original.bones.Select(b=>bones[b.name]).ToArray();skin.rootBone=bones[original.rootBone.name];
    }
    var view=fps.GetComponent<SoldierWeaponView>();if(view.grenadeView)view.grenadeView=AssetDatabase.LoadAssetAtPath<AuthoredGrenadeView>("Assets/Game/Prefabs/Players/Visuals/ScaledGrenadeView.prefab");
    foreach(var weapon in fps.GetComponentsInChildren<SoldierWeapon>(true))weapon.definition=def;tps.GetComponent<SoldierWeapon>().definition=def;
    var fp=PrefabUtility.SaveAsPrefabAsset(fps,old!=null?AssetDatabase.GetAssetPath(old.firstPersonPrefab):output+"/FPS.prefab").GetComponent<SoldierWeaponView>();
    var tp=PrefabUtility.SaveAsPrefabAsset(tps,old!=null?AssetDatabase.GetAssetPath(old.thirdPersonPrefab):output+"/TPS.prefab").GetComponent<SoldierWeaponView>();
    var comparison=Object.Instantiate(p);comparison.originalProportions=true;comparison.comparisonFingerprint=fingerprint;comparison.sharedFirstPersonRig=fp.sharedFirstPersonRig;comparison.converted=new ArenaWeaponCatalog.Entry{weapon=def,basePrefab=tp.gameObject,firstPersonPrefab=fp,thirdPersonPrefab=tp,polished=false,sights=entry.sights,attachments=entry.attachments,defaultSight=entry.defaultSight,animationStyle=entry.animationStyle+" / original proportions surface"};
    Save(comparison,output+"/Conversion.asset");count++;
   }finally{Object.DestroyImmediate(fps);Object.DestroyImmediate(tps);}
  }
  if(count>0)
  {
   var allProofs=AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{Root}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>).ToArray();
   ArenaSharedWeaponRigBuilder.Configure(allProofs);
   ArenaSharedBodyPreviewBuilder.Configure(allProofs);
  }
  AssetDatabase.SaveAssets();File.WriteAllText(StampPath,JsonUtility.ToJson(stamps));if(count>0||previous.Length!=paths.Length)ArenaPolygonWeaponConversionBuilder.Integrate();return new{rebuiltPairs=count,totalPairs=paths.Length};
 }
}

