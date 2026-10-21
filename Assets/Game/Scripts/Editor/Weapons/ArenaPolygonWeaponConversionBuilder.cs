using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using Object=UnityEngine.Object;

// Convert the evaluated current animations, keeping originals and production catalogs intact.
public static class ArenaPolygonWeaponConversionBuilder
{
 public const string Root="Assets/Game/AnimationTests/PolygonUE4Conversions";
 public static string[] Ids=>AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset").entries.Where(e=>e.polished).Select(e=>e.weapon.catalogId).ToArray();
 public const string BodyTemplate=Root+"/Shared/PolygonUE4BodyRig.prefab";
 static Transform Find(GameObject go,string name)=>go.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
 static GameObject Clone(GameObject prefab){var go=Object.Instantiate(prefab);if(PrefabUtility.IsPartOfPrefabInstance(go))PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);return go;}
 static T Save<T>(T asset,string path)where T:Object{asset.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(asset,path);return asset;}
 static void ClearAnimators(GameObject go){foreach(var animator in go.GetComponentsInChildren<Animator>(true))Object.DestroyImmediate(animator);}
 static ArenaWeaponCatalog.Entry Source(string id)=>AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset").entries.First(e=>e.weapon.catalogId==id);
 public static string Folder(string id)=>Root+"/"+id.Replace("legacy-","").Replace("polygon-","");
 public static object Build(string id,bool rebuildReload=true)
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)throw new InvalidOperationException("Use Edit Mode after compilation completes.");
  var entry=Source(id);if(entry.firstPersonPrefab.sharedFirstPersonRig)throw new InvalidOperationException("This weapon is already promoted. Edit its production prefab and definition directly.");string folder=Folder(id);Directory.CreateDirectory(folder);AssetDatabase.Refresh();
  using var evaluated=new ArenaMk18PoseFit.Source(entry);var source=evaluated.animator.gameObject;
  var arms=Clone(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSAnimationPack/Meshes/Characters/FPS_Arms_UE4/SK_Arms_Mono.fbx"));var fps=new GameObject(entry.weapon.displayName+"_Polygon_UE4_FPS");Avatar sourceAvatar=null,targetAvatar=null;
  try
  {
   foreach(Transform child in arms.transform){child.localPosition=Quaternion.Euler(0,180,0)*child.localPosition;child.localRotation=Quaternion.Euler(0,180,0)*child.localRotation;}
   ArenaMk18Ue4StageBuilder.RestoreBindPose(source);sourceAvatar=ArenaMk18Ue4StageBuilder.Human(source,false);targetAvatar=ArenaMk18Ue4StageBuilder.Human(arms,true);
   var gun=Clone(entry.firstPersonPrefab.gameObject);gun.name="SourceWeapon";gun.transform.SetParent(fps.transform,false);var view=fps.AddComponent<SoldierWeaponView>();EditorUtility.CopySerialized(gun.GetComponent<SoldierWeaponView>(),view);Object.DestroyImmediate(gun.GetComponent<SoldierWeaponView>());ClearAnimators(gun);
   arms.name="SourceArms";arms.transform.SetParent(fps.transform,false);ClearAnimators(arms);
   foreach(var skin in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))Object.DestroyImmediate(skin);
   var accepted=AssetDatabase.LoadAssetAtPath<GameObject>(ArenaUe4RigProofBuilder.PreviewRigPath).GetComponentInChildren<SkinnedMeshRenderer>(true);
   var node=Object.Instantiate(accepted.gameObject,arms.transform,false);node.name="PolygonArms_UE4Skin";node.transform.localRotation=Quaternion.Euler(0,180,0)*accepted.transform.localRotation;node.transform.localPosition=Quaternion.Euler(0,180,0)*accepted.transform.localPosition;
   var targetSkin=node.GetComponent<SkinnedMeshRenderer>();targetSkin.bones=accepted.bones.Select(b=>Find(arms,b.name)).ToArray();targetSkin.rootBone=Find(arms,accepted.rootBone.name);targetSkin.updateWhenOffscreen=true;
   var fitting=new ArenaMk18PoseFit.Fit(source,arms,source.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.bones.Any(b=>b&&b.name=="Hand_L")),targetSkin);
   AnimationClip idle,reload,fire,aimFire;
   using(var sh=new HumanPoseHandler(sourceAvatar,source.transform))using(var th=new HumanPoseHandler(targetAvatar,arms.transform))
   {
    var baseline=new HumanPose();th.GetHumanPose(ref baseline);
    AnimationClip Bake(string state,int child,AnimationClip rest){var input=ArenaWeaponAnimationSource.State(entry.weapon.firstPerson,"Base Layer",state,child);var parts=ArenaWeaponAnimationSource.State(entry.weapon.firstPersonParts,"Base Layer",state);if(!input||!parts)throw new Exception(entry.weapon.displayName+" missing "+state+" source animation");return ArenaMk18Ue4StageBuilder.Bake(source,arms,fps,gun,sh,th,baseline,input,parts,new Vector3(0,1.55f,0),state=="Fire"&&child==1?"AimFire_UE4":state+"_UE4",state=="Idle",evaluated,fitting,folder,rest,state=="Fire"&&child==1);}
    idle=Bake("Idle",0,null);reload=rebuildReload?Bake("Reload",0,idle):AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Reload_UE4.anim");fire=Bake("Fire",0,idle);aimFire=Bake("Fire",1,idle);
   }
   idle.SampleAnimation(fps,0);
   var controller=ArenaMk18GameplayBuilder.Controller(entry.weapon,idle,reload,fire,aimFire,folder);
   view.animator=fps.AddComponent<Animator>();view.animator.runtimeAnimatorController=controller;view.animationOverride=controller;view.independentFirstPersonFrame=true;view.mountPosition=new Vector3(0,-1.55f,0);view.mountRotation=Quaternion.identity;view.mountScale=Vector3.one;view.grenadeView=null;
   ArenaMk18GameplayBuilder.PrepareView(view);
   var definition=Object.Instantiate(entry.weapon);definition.catalogId=id+"-polygon-ue4";definition.displayName=entry.weapon.displayName+" (Polygon UE4)";definition.showFirstPersonHands=false;definition.reloadDuration=reload.length;definition=Save(definition,folder+"/Weapon.asset");foreach(var weapon in gun.GetComponentsInChildren<SoldierWeapon>(true))weapon.definition=definition;
   var firstPerson=PrefabUtility.SaveAsPrefabAsset(fps,folder+"/"+entry.weapon.displayName.Replace(" ","_")+"_UE4_FPS.prefab").GetComponent<SoldierWeaponView>();
   var tps=Clone(entry.thirdPersonPrefab.gameObject);SoldierWeaponView thirdPerson;
   try{tps.GetComponent<SoldierWeapon>().definition=definition;ArenaMk18GameplayBuilder.CalibrateSights(tps,view);thirdPerson=PrefabUtility.SaveAsPrefabAsset(tps,folder+"/"+entry.weapon.displayName.Replace(" ","_")+"_UE4_TPS.prefab").GetComponent<SoldierWeaponView>();}finally{Object.DestroyImmediate(tps);}
   var previous=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(folder+"/Conversion.asset");var profile=previous?Object.Instantiate(previous):ScriptableObject.CreateInstance<WeaponAnimationConversionProof>();profile.source=entry;profile.converted=new ArenaWeaponCatalog.Entry{weapon=definition,basePrefab=entry.basePrefab,firstPersonPrefab=firstPerson,thirdPersonPrefab=thirdPerson,polished=false,sights=entry.sights,attachments=entry.attachments,defaultSight=entry.defaultSight,animationStyle="Existing evaluated animation and IK baked to fitted Polygon UE4 rig"};
   profile.idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Gameplay_"+idle.name+".anim");profile.reload=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Gameplay_"+reload.name+".anim");profile.fire=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Gameplay_"+fire.name+".anim");profile.aimFire=ArenaWeaponAnimationSource.State(controller,"Base Layer","Fire",1);profile=Save(profile,folder+"/Conversion.asset");AssetDatabase.SaveAssets();return new{entry.weapon.displayName,folder,reload=reload.length,maxSurfaceError=fitting.WorstRms};
  }
  finally{Object.DestroyImmediate(fps);if(sourceAvatar)Object.DestroyImmediate(sourceAvatar);if(targetAvatar)Object.DestroyImmediate(targetAvatar);}
 }
 public static object BuildBody(string id)
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)throw new InvalidOperationException("Use Edit Mode after compilation.");
  string folder=Folder(id);var proof=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(folder+"/Conversion.asset");if(!proof)throw new Exception("Build FPS first");var entry=proof.source;
  var body=Clone(AssetDatabase.LoadAssetAtPath<GameObject>(BodyTemplate));body.name=entry.weapon.displayName+"_Polygon_UE4_Body";body.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);ClearAnimators(body);
  using var evaluated=new ArenaMk18PoseFit.Source(entry,true);var source=evaluated.animator.gameObject;var arms=Find(body,"SourceArms").gameObject;Avatar targetAvatar=null;GameObject proxy=null;
  try
  {
   var oldGun=body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="SourceWeapon");if(oldGun)Object.DestroyImmediate(oldGun.gameObject);var gun=Clone(entry.thirdPersonPrefab.gameObject);gun.name="SourceWeapon";gun.transform.SetParent(body.transform,false);ClearAnimators(gun);
   var skin=arms.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s=>s.bones.Any(b=>b.name=="hand_l"));var bind=skin.sharedMesh.bindposes;
   foreach(int i in Enumerable.Range(0,skin.bones.Length).OrderBy(i=>AnimationUtility.CalculateTransformPath(skin.bones[i],arms.transform).Count(c=>c=='/'))){var frame=skin.transform.localToWorldMatrix*bind[i].inverse;skin.bones[i].SetPositionAndRotation(frame.GetColumn(3),frame.rotation);}
   targetAvatar=ArenaMk18Ue4StageBuilder.Human(arms,true);
   var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Players/Player_Polygon_Local.prefab");var original=player.GetComponent<SoldierFirstPersonView>().armsAnimator.GetComponentInChildren<SkinnedMeshRenderer>(true);proxy=Object.Instantiate(original.gameObject,source.transform,false);var sourceSkin=proxy.GetComponent<SkinnedMeshRenderer>();sourceSkin.bones=original.bones.Select(b=>Find(source,b.name)).ToArray();sourceSkin.rootBone=Find(source,original.rootBone.name);
   var originalBody=player.GetComponent<SoldierActions>().animator.GetComponentInChildren<SkinnedMeshRenderer>(true);var fitting=new ArenaMk18PoseFit.Fit(source,arms,sourceSkin,skin,originalBody);
   using(var sh=new HumanPoseHandler(evaluated.animator.avatar,source.transform))using(var th=new HumanPoseHandler(targetAvatar,arms.transform))
   {
    var baseline=new HumanPose();th.GetHumanPose(ref baseline);
    AnimationClip Bake(string state,AnimationClip rest){var input=ArenaWeaponAnimationSource.Body(entry.weapon.thirdPerson,state);var parts=ArenaWeaponAnimationSource.State(entry.weapon.firstPersonParts,"Base Layer",state);return ArenaMk18Ue4StageBuilder.Bake(source,arms,body,gun,sh,th,baseline,input,parts,Vector3.zero,"TPS_"+state+"_UE4",state=="Idle",evaluated,fitting,folder,rest);}
    proof.bodyIdle=Bake("Idle",null);proof.bodyReload=Bake("Reload",proof.bodyIdle);
   }
   proof.bodyIdle.SampleAnimation(body,0);foreach(var component in body.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(component);foreach(var c in body.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);foreach(var r in body.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(r);
   float floor=arms.GetComponentsInChildren<Renderer>(true).Min(r=>r.bounds.min.y);if(float.IsFinite(floor))body.transform.position+=Vector3.up*(-.03f-floor);
   proof.sharedBodyPrefab=null;proof.sharedBodyWeaponPrefab=null;proof.thirdPersonProof=PrefabUtility.SaveAsPrefabAsset(body,folder+"/"+entry.weapon.displayName.Replace(" ","_")+"_UE4_Body.prefab");EditorUtility.SetDirty(proof);AssetDatabase.SaveAssets();return new{entry.weapon.displayName,body=AssetDatabase.GetAssetPath(proof.thirdPersonProof),maxSurfaceError=fitting.WorstRms};
  }
  finally{Object.DestroyImmediate(body);if(proxy)Object.DestroyImmediate(proxy);if(targetAvatar)Object.DestroyImmediate(targetAvatar);}
 }
 public const string ScaledReviewScene="Assets/Game/Scenes/Tests/Weapons/Armory_UE4OriginalProportions.unity";
 public static object Integrate()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Use Edit Mode");
  var proofs=AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{Root}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>).OrderBy(p=>p.converted.weapon.displayName).ToArray();
  if(proofs.Any(p=>!p||(!p.thirdPersonProof&&!(p.sharedBodyPrefab&&p.sharedBodyWeaponPrefab))))throw new Exception("Finish all conversions first");
  BuildReview(proofs.Where(p=>p.originalProportions && p.source.weapon!=p.converted.weapon).ToArray(),"Assets/Game/AnimationTests/UE4RigProof/OriginalProportionsCatalog.asset",ScaledReviewScene);
  AssetDatabase.SaveAssets();return new{scaled=proofs.Count(p=>p.originalProportions),scene=ScaledReviewScene};
 }
 static void BuildReview(WeaponAnimationConversionProof[] proofs,string catalogPath,string scenePath)
 {
  var catalog=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>(catalogPath);
  if(!catalog){catalog=ScriptableObject.CreateInstance<ArenaWeaponCatalog>();AssetDatabase.CreateAsset(catalog,catalogPath);}
  catalog.entries=ArenaArmoryCatalogs.ReviewEntries(proofs);EditorUtility.SetDirty(catalog);
  var scene=EditorSceneManager.OpenScene(scenePath);
  foreach(var wall in Object.FindObjectsByType<ArmoryWall>()){wall.catalog=catalog;EditorUtility.SetDirty(wall);}
  foreach(var binding in Object.FindObjectsByType<SoldierWeaponCatalogBinding>())
  {
   binding.catalog=catalog;EditorUtility.SetDirty(binding);var loadout=binding.GetComponent<SoldierWeaponLoadout>();
   var entry=catalog.entries.FirstOrDefault(e=>(e.weapon.catalogId.Contains("mg5")||e.weapon.catalogId.EndsWith("mgx5_ue4")))??catalog.entries.First();
   loadout.slots=new[]{new SoldierWeaponLoadout.Slot{name=entry.weapon.displayName,definition=entry.weapon,firstPersonPrefab=entry.firstPersonPrefab,thirdPersonPrefab=entry.thirdPersonPrefab}};
   loadout.selectedSlot=0;EditorUtility.SetDirty(loadout);
  }
  var obsolete=Object.FindAnyObjectByType<Mk18RigVerification>();if(obsolete)Object.DestroyImmediate(obsolete.gameObject);
  var old=Object.FindAnyObjectByType<WeaponConversionPreview>();if(old)Object.DestroyImmediate(old.gameObject);
  AddPreview(proofs);
  ArmoryEditorCache.ConfigureReview(scene,catalog);
  EditorSceneManager.SaveScene(scene);
 }
 public static WeaponConversionPreview AddPreview(WeaponAnimationConversionProof[] proofs)
 {
  var root=new GameObject("Scaled Polygon UE4 / F9 preview");var preview=root.AddComponent<WeaponConversionPreview>();preview.proofs=Array.Empty<WeaponAnimationConversionProof>();preview.proofPaths=proofs.Select(AssetDatabase.GetAssetPath).ToArray();preview.proofLabels=proofs.Select(p=>p.converted.weapon.displayName).ToArray();
  var observer=new GameObject("Conversion camera").AddComponent<Camera>();observer.transform.SetParent(root.transform,false);observer.nearClipPlane=.01f;observer.farClipPlane=100;observer.clearFlags=CameraClearFlags.SolidColor;observer.backgroundColor=new Color(.12f,.14f,.16f);preview.observer=observer;observer.gameObject.SetActive(false);
  var light=new GameObject("Conversion key").AddComponent<Light>();light.transform.SetParent(root.transform,false);light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(40,-35,0);light.gameObject.SetActive(false);
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Conversion grid";floor.transform.SetParent(root.transform,false);floor.transform.position=new Vector3(0,-.08f,0);floor.transform.localScale=new Vector3(20,.1f,20);floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Tests/PerformanceGrid.mat");floor.SetActive(false);
  return preview;
 }

}

