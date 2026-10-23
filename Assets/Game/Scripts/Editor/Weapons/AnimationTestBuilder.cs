using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Offline integration only. Source assets and existing production weapons remain untouched.
public static class AnimationTestBuilder
{
 const string Root="Assets/Game/AnimationTests";
 const string Pack="Assets/FPSAnimationPack/";
 const string Report="Library/AnimationTestIntegration.txt";
 static FpsPackPreviewCatalog catalog;
 static WeaponCatalog original;
 static readonly List<WeaponCatalog.Entry> entries=new();
 static readonly List<string> report=new();
 static readonly Dictionary<string,AnimationClip> clips=new();
 static readonly Dictionary<string,string> matches=new(){
 {"AKX","legacy-ak12"},{"ASVal","legacy-ak"},{"Colibri","legacy-mk23"},{"DGL50","legacy-mk23"},
 {"Drake-12","polygon-clustershotgun"},{"G3","legacy-fal"},{"Kar98K","polygon-hunterrifle"},{"KXG12","polygon-clustershotgun"},
 {"L96X","polygon-hunterrifle"},{"M1911","polygon-old_pistol"},{"MGX5","polygon-machinegun"},{"Mk14EBR","legacy-fal"},
 {"MPS5","polygon-submachine5"},{"MX16A4","polygon-mkb16"},{"PDW90","polygon-rp9"},{"RPG7","polygon-rocket_launcher"},
 {"Striker-V","polygon-buzzsub"},{"SVD","legacy-svd"},{"Viper-357","polygon-revolver"},{"X18","legacy-glock"}};
 static readonly Dictionary<string,string> names=new(){
 {"AKX","AK-12"},{"ASVal","AS Val"},{"Colibri","Colibri"},{"DGL50","Desert Eagle"},{"Drake-12","Drake-12"},
 {"G3","G3"},{"Kar98K","Kar98k"},{"KXG12","KSG"},{"L96X","L96"},{"M1911","M1911"},{"MGX5","MG5"},
 {"Mk14EBR","Mk14 EBR"},{"MPS5","MP5"},{"MX16A4","M16A4"},{"PDW90","P90"},{"RPG7","RPG-7"},
 {"Striker-V","KRISS Vector"},{"SVD","SVD"},{"Viper-357","Viper .357"},{"X18","Glock 18"}};
 public static object RebuildActions()
 {
  var layout=FpsPackSourceLayout.Read();
  FpsPackActionBuilder.BuildOnly(new HashSet<string>(layout.actions.Where(a=>a.file.Contains("/UE4/")).Select(a=>a.source)));
  return "UE4 authored actions reconstructed";
 }
 static T Save<T>(T obj,string path) where T:UnityEngine.Object
 {
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  var existing=AssetDatabase.LoadAssetAtPath<T>(path);
  if(existing){EditorUtility.CopySerialized(obj,existing);UnityEngine.Object.DestroyImmediate(obj);EditorUtility.SetDirty(existing);return existing;}
  AssetDatabase.CreateAsset(obj,path);return obj;
 }
 static string Clean(string name)=>string.Concat(name.Select(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'?c:'_'));
 static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.Equals(name,StringComparison.OrdinalIgnoreCase));
 static void RemoveCamera(Camera camera)
 {
  // URP's additional camera data requires Camera; remove dependents first.
  foreach(var component in camera.GetComponents<Component>())
   if(component&&component is not Transform&&component is not Camera)UnityEngine.Object.DestroyImmediate(component);
  UnityEngine.Object.DestroyImmediate(camera);
 }
 static GameObject Model(string path,Transform parent,string name)
 {
  var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),parent,false);obj.name=name;
  if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.UnpackPrefabInstance(obj,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
  foreach(var a in obj.GetComponentsInChildren<Animator>(true))UnityEngine.Object.DestroyImmediate(a);
  foreach(var camera in obj.GetComponentsInChildren<Camera>(true))RemoveCamera(camera);
  foreach(var light in obj.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(light);
  foreach(var r in obj.GetComponentsInChildren<Renderer>(true))
   if(r.name.StartsWith("shape_")||r.name.Equals("crosshair",StringComparison.OrdinalIgnoreCase))r.enabled=false;
  return obj;
 }
 static AnimationClip Clip(string source)
 {
  if(string.IsNullOrEmpty(source))return null;
  if(!clips.TryGetValue(source,out var clip))
  {
   var entry=catalog.Entries.FirstOrDefault(e=>e.source==source);
   if(entry==null)throw new Exception("Missing source "+source);
   clip=FpsPackPreview.LoadClip(entry.Path);if(!clip)throw new Exception("Missing rebuilt clip "+entry.Path);clips[source]=clip;
  }
  return clip;
 }
 static AnimationClip Concat(string name,params AnimationClip[] sources)
 {
  sources=sources.Where(c=>c).ToArray();var clip=new AnimationClip{name=name,frameRate=60};var bindings=sources.SelectMany(AnimationUtility.GetCurveBindings).Distinct().ToArray();
  var curves=new List<AnimationCurve>();foreach(var binding in bindings){var keys=new List<Keyframe>();float offset=0;foreach(var source in sources){var curve=AnimationUtility.GetEditorCurve(source,binding);if(curve!=null)foreach(var original in curve.keys){var k=original;k.time+=offset;if(keys.Count>0&&k.time<=keys[keys.Count-1].time)k.time=keys[keys.Count-1].time+.000001f;keys.Add(k);}offset+=source.length;}curves.Add(new AnimationCurve(keys.ToArray()));}AnimationUtility.SetEditorCurves(clip,bindings,curves.ToArray());return clip;
 }
 static AnimationClip Hold(AnimationClip source)
 {
  if(!source)return null;var clip=new AnimationClip{name=source.name+"_Hold",frameRate=60};var bindings=AnimationUtility.GetCurveBindings(source);AnimationUtility.SetEditorCurves(clip,bindings,bindings.Select(b=>AnimationCurve.Constant(0,.03333334f,AnimationUtility.GetEditorCurve(source,b).Evaluate(0))).ToArray());return clip;
 }
 static AnimationClip Merge(string name,AnimationClip arms,string armsPrefix,AnimationClip weapon,string weaponPrefix,bool loop)
 {
  var clip=new AnimationClip{name=name,frameRate=60};var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
  foreach(var pair in new[]{(arms,armsPrefix),(weapon,weaponPrefix)})
  {
   if(!pair.Item1)continue;
   foreach(var binding in AnimationUtility.GetCurveBindings(pair.Item1))
   {
    if(binding.type==typeof(UnityEngine.Camera)||binding.type==typeof(UnityEngine.Light))continue;
    var b=binding;b.path=pair.Item2+(string.IsNullOrEmpty(binding.path)?"":"/"+binding.path);
    bindings.Add(b);curves.Add(AnimationUtility.GetEditorCurve(pair.Item1,binding));
   }
   foreach(var binding in AnimationUtility.GetObjectReferenceCurveBindings(pair.Item1))
   {var b=binding;b.path=pair.Item2+(string.IsNullOrEmpty(binding.path)?"":"/"+binding.path);AnimationUtility.SetObjectReferenceCurve(clip,b,AnimationUtility.GetObjectReferenceCurve(pair.Item1,binding));}
  }
  AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());
  var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
  if(name.EndsWith("_Equip_FirstPerson"))clip.name="Equip_FirstPerson";
  if(name.EndsWith("_Unequip_FirstPerson"))clip.name="Unequip_FirstPerson";
  return Save(clip,Root+"/Clips/"+Clean(name)+".anim");
 }
 static AnimatorController Controller(string name,AnimationClip idle,AnimationClip fire,AnimationClip reload,AnimationClip equip,AnimationClip unequip)
 {
  string path=Root+"/Controllers/"+Clean(name)+".controller";Directory.CreateDirectory(Path.GetDirectoryName(path));
  var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
  if(!c)c=AnimatorController.CreateAnimatorControllerAtPath(path);
  else
  {
   c.layers=Array.Empty<AnimatorControllerLayer>();c.parameters=Array.Empty<AnimatorControllerParameter>();
   foreach(var subasset in AssetDatabase.LoadAllAssetsAtPath(path).Where(a=>a&&a!=c))UnityEngine.Object.DestroyImmediate(subasset,true);
   c.AddLayer("Base Layer");
  }
  c.AddParameter("Fire",AnimatorControllerParameterType.Trigger);c.AddParameter("Reload",AnimatorControllerParameterType.Trigger);
  c.AddParameter("WeaponSwitchSpeed",AnimatorControllerParameterType.Float);c.parameters=c.parameters.Select(p=>{if(p.name=="WeaponSwitchSpeed")p.defaultFloat=1;return p;}).ToArray();
  var sm=c.layers[0].stateMachine;var i=sm.AddState("Idle");i.motion=idle;sm.defaultState=i;
  var f=sm.AddState("Fire");f.motion=fire?fire:idle;var r=sm.AddState("Reload");r.motion=reload?reload:idle;
  var t=sm.AddAnyStateTransition(r);t.hasExitTime=false;t.duration=.04f;t.AddCondition(AnimatorConditionMode.If,0,"Reload");t.canTransitionToSelf=false;
  t=f.AddTransition(i);t.hasExitTime=true;t.exitTime=1;t.duration=.03f;
  t=r.AddTransition(i);t.hasExitTime=true;t.exitTime=1;t.duration=.04f;
  if(equip||unequip)
  {
   // Switch clips share the base layer. An empty override layer would keep the
   // last equip pose and mask subsequent reload/fire animation.
   foreach(var pair in new[]{("Equip",equip),("Unequip",unequip)})
   {
    var state=sm.AddState(pair.Item1);state.motion=pair.Item2?pair.Item2:idle;
    state.speedParameter="WeaponSwitchSpeed";state.speedParameterActive=true;
    t=state.AddTransition(i);t.hasExitTime=true;t.exitTime=1;t.duration=.04f;
   }
  }
  return c;
 }
 static Transform Marker(Transform parent,string name,Vector3 position)
 {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
 static Bounds Bounds(GameObject obj)
 {var rs=obj.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();var b=new Bounds(rs[0].bounds.center,Vector3.zero);foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
 static SoldierWeaponView FinishView(GameObject root,Transform gun,AnimatorController controller,Vector3 mountPosition,Quaternion mountRotation,bool reversed=false)
 {
  foreach(var camera in root.GetComponentsInChildren<UnityEngine.Camera>(true))RemoveCamera(camera);
  foreach(var light in root.GetComponentsInChildren<UnityEngine.Light>(true))UnityEngine.Object.DestroyImmediate(light);
  foreach(var listener in root.GetComponentsInChildren<AudioListener>(true))UnityEngine.Object.DestroyImmediate(listener);
  if(root.name.EndsWith("_FPS"))foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.updateWhenOffscreen=true;
  var view=root.AddComponent<SoldierWeaponView>();view.animator=root.AddComponent<Animator>();view.animator.runtimeAnimatorController=controller;view.mountName=root.name;
  view.mountPosition=mountPosition;view.mountRotation=mountRotation;view.mountScale=Vector3.one;view.attachments=Array.Empty<SoldierWeaponView.AttachmentSocket>();
  var b=Bounds(gun.gameObject);Vector3 localMin=root.transform.InverseTransformPoint(b.min),localMax=root.transform.InverseTransformPoint(b.max);
  // Author in the canonical +Z frame. Markers remain editable in the test prefab.
  var max=Vector3.Max(localMin,localMax);var min=Vector3.Min(localMin,localMax);float midX=(min.x+max.x)*.5f;float frontZ=reversed?min.z:max.z;float rearZ=reversed?max.z:min.z;float direction=reversed?-1:1;
  view.effects=root.AddComponent<SoldierWeaponSockets>();
  view.effects.muzzleFlash=Marker(root.transform,"Muzzle",new Vector3(midX,max.y-.06f,frontZ));
  view.effects.muzzleFlash.localRotation=Quaternion.LookRotation(Vector3.forward*direction);
  view.effects.shellEjection=Marker(root.transform,"Ejection",new Vector3(midX+.055f,max.y-.11f,(min.z+max.z)*.5f));view.effects.shellEjection.localRotation=Quaternion.LookRotation(Vector3.right,Vector3.up);
  view.opticMount=Marker(root.transform,"Optic",new Vector3(midX,max.y,Mathf.Lerp(rearZ,frontZ,.3f)));
  view.rearSight=Marker(root.transform,"RearSight",new Vector3(midX,max.y-.01f,Mathf.Lerp(rearZ,frontZ,.25f)));
  view.frontSight=Marker(root.transform,"FrontSight",new Vector3(midX,max.y-.01f,frontZ-direction*.06f));
  return view;
 }
 static WeaponSightDefinition Bare(string key)
 {var sight=ScriptableObject.CreateInstance<WeaponSightDefinition>();sight.id=key+"-irons";sight.displayName="Built-in sights";sight.eyeDistance=.22f;sight.fieldOfView=60;return Save(sight,Root+"/Definitions/"+key+"_Irons.asset");}
 static void AddData(GameObject root,SoldierWeaponDefinition definition,WeaponCatalog.Entry reference,WeaponSightDefinition irons,SoldierWeaponView fp)
 {
  var weapon=root.AddComponent<SoldierWeapon>();EditorUtility.CopySerialized(reference.thirdPersonPrefab.GetComponent<SoldierWeapon>(),weapon);weapon.definition=definition;
  var sights=root.AddComponent<SoldierWeaponSights>();sights.defaultSight=irons;sights.compatibleSights=new[]{irons};sights.referenceWeaponPosition=fp.mountPosition;sights.referenceWeaponRotation=fp.mountRotation;
  sights.referenceWeaponScale=Vector3.one;sights.referenceCameraRotation=Quaternion.identity;sights.neutralCameraRotation=Quaternion.identity;sights.cameraReferenceConfigured=true;
  var v=root.GetComponent<SoldierWeaponView>();sights.thirdPersonMount=v.opticMount;sights.rearSight=v.rearSight;sights.frontSight=v.frontSight;
  root.AddComponent<SoldierWeaponAttachments>().mounts=Array.Empty<SoldierWeaponAttachments.Mount>();
 }
 [MenuItem("Tools/Game/Animation Tests/Rebuild test weapon variants")]
 public static void Rebuild(){if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before rebuilding test assets.");RebuildActions();Main();PolygonAnimationBuilder.Main();if(AssetDatabase.LoadAssetAtPath<SoldierWeaponDefinition>(Root+"/UE4RigProof/MG5_UE4RigProof.asset"))Ue4RigProofBuilder.Main();}
 public static object Main()
 {
  catalog=new FpsPackPreviewCatalog();original=AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");entries.Clear();entries.AddRange(original.entries);report.Clear();
  foreach(var preset in catalog.Layout.presets.Where(p=>p.variants.Any(v=>v.rig=="UE4")))BuildNative(preset);
  SanitizeSavedViews();BuildExternalDisplays();
  var result=ScriptableObject.CreateInstance<WeaponCatalog>();result.entries=entries.ToArray();Save(result,Root+"/AnimationTestCatalog.asset");
  AssetDatabase.SaveAssets();File.WriteAllLines(Report,report);return report.ToArray();
 }
 public static void ConfigureRoundReload(SoldierWeaponDefinition definition, string family)
 {
  definition.roundReload = default;
  if(family != "KXG12" && family != "Kar98K") return;
  string prefix="Assets/FPSAnimationPack/Actions/UE4/"+family+"/Weapon/AM_W_"+(family=="Kar98K"?"Kar98k":family)+"_Reload_";
  float Length(string section)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(prefix+section+".anim").length;
  definition.roundReload=new RoundReloadTiming{enabled=true,openingDuration=Length("Start"),insertionDuration=Length("Loop"),closingDuration=Length("End"),insertAt=family=="Kar98K"?17f/23f:.75f,bakedInsertions=family=="Kar98K"?5:6};
 }
 static void BuildNative(FpsPackSourceLayout.Preset preset)
 {
  var variant=preset.variants.First(v=>v.rig=="UE4");string key=Clean(preset.id.Substring(3))+"_UE4";
  string title=(preset.id=="DA_M14"?"M14":names.TryGetValue(preset.family,out var n)?n:preset.family)+(preset.id=="DA_AKX200"?" / AKX200":"")+" (UE4)";
  var reference=original.entries.First(e=>e.weapon.catalogId==matches[preset.family]);
  var group=catalog.Weapons.First(w=>w.Family==preset.family);
  var character=catalog.Entries.First(e=>e.file=="Meshes/Characters/FPS_Arms_UE4/SK_Arms_Mono.fbx");
  var idleEntry=catalog.CharacterIdle(group,character);var idle=Hold(Clip(idleEntry.source));
  var idleW=Hold(catalog.Clip(catalog.IdleWeapon(group)));
  var reloadLink=variant.links.FirstOrDefault(l=>l.key=="PrimaryReload");var equipLink=variant.links.FirstOrDefault(l=>l.key=="Equip");var outLink=variant.links.FirstOrDefault(l=>l.key=="UnEquip");
  var reload=Clip(reloadLink?.character);var reloadW=Clip(reloadLink?.weapon);
  var equip=Clip(equipLink?.character);var unequip=Clip(outLink?.character);
  var fireLink=variant.links.FirstOrDefault(l=>l.key=="Fire");var fireArms=Clip(fireLink?.character)??idle;
  if((preset.family=="KXG12"||preset.family=="Kar98K")&&variant.links.Any(l=>l.key=="AdditionalReload")){var loop=variant.links.First(l=>l.key=="SecondaryReload");var end=variant.links.First(l=>l.key=="AdditionalReload");int rounds=preset.family=="Kar98K"?5:Mathf.Min(6,reference.weapon.magazineCapacity);reload=Concat(key+"_FullReload",new[]{reload}.Concat(Enumerable.Repeat(Clip(loop.character),rounds)).Concat(new[]{Clip(end.character)}).ToArray());reloadW=Concat(key+"_FullPartsReload",new[]{reloadW}.Concat(Enumerable.Repeat(Clip(loop.weapon),rounds)).Concat(new[]{Clip(end.weapon)}).ToArray());}
  // The authored pair may be a pump/bolt montage rather than an animation named Fire.
  var fireW=Clip(fireLink?.weapon)??catalog.Clip(catalog.Entries.FirstOrDefault(e=>e.Weapon&&e.Family==preset.family&&e.type=="AnimSequence"&&e.Action=="Fire"));
  var root=new GameObject(key+"_FPS");
  try
  {
   var arms=Model(character.Path,root.transform,"SourceArms");idle.SampleAnimation(arms,0);
   var grip=Find(arms,"ik_hand_gun");var hand=Find(arms,"hand_r");
   var gunEntry=catalog.Entries.First(e=>e.source==preset.mesh);
   var gun=Model(gunEntry.Path,grip,"SourceWeapon");gun.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);if(idleW)idleW.SampleAnimation(gun,0);
   string armPrefix="SourceArms",gunPrefix=AnimationUtility.CalculateTransformPath(gun.transform,root.transform);
   var idleMerged=Merge(key+"_Idle",idle,armPrefix,idleW,gunPrefix,true);
   var fireMerged=Merge(key+"_Fire",fireArms,armPrefix,fireW?fireW:idleW,gunPrefix,false);
   var reloadMerged=Merge(key+"_Reload",reload?reload:idle,armPrefix,reloadW?reloadW:idleW,gunPrefix,false);
   var equipMerged=Merge(key+"_Equip_FirstPerson",equip?equip:idle,armPrefix,idleW,gunPrefix,false);
   var outMerged=Merge(key+"_Unequip_FirstPerson",unequip?unequip:idle,armPrefix,idleW,gunPrefix,false);
   var controller=Controller(key,idleMerged,fireMerged,reloadMerged,equipMerged,outMerged);
   // Source animation frame looks down -Z with an authored eye 1.65m above its origin.
   root.transform.rotation=Quaternion.Euler(0,180,0);
   foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())skin.updateWhenOffscreen=true;
   var fp=FinishView(root,gun.transform,controller,new Vector3(0,-1.65f,0),root.transform.rotation,true);fp.animationOverride=controller;fp.independentFirstPersonFrame=true;
   var definition=UnityEngine.Object.Instantiate(reference.weapon);definition.catalogId="animation-test-"+key;definition.displayName=title;definition.showFirstPersonHands=false;
   definition.viewOffset=definition.hipFireOffset=definition.aimOffset=definition.viewRotationOffset=definition.hipFireRotationOffset=definition.aimRotationOffset=Vector3.zero;
   definition.cameraPositionWeight=definition.cameraRotationWeight=0;definition.builtInIronEyeDistance=.22f;definition.isUtility=preset.family=="RPG7";definition.useAuthoredReload=preset.family=="RPG7";
   definition.reloadDuration=reloadMerged.length;ConfigureRoundReload(definition,preset.family);definition.equipDuration=Mathf.Clamp(equipMerged.length,.35f,1.2f);definition.unequipDuration=Mathf.Clamp(outMerged.length,.35f,1.2f);
   definition=Save(definition,Root+"/Definitions/"+key+".asset");
   // FPS source fingers and weapon retain their native shared grip frame.
   var nativeRightInWeapon=gun.transform.InverseTransformPoint(hand.position);var nativeRotationInWeapon=Quaternion.Inverse(gun.transform.rotation)*hand.rotation;
   var tp=BuildThirdPerson(key,gunEntry.Path,idleW,fireW,reloadW,reference,definition,nativeRightInWeapon,gun.transform.rotation);
   var irons=Bare(key);AddData(tp.gameObject,definition,reference,irons,fp);
   var pose=tp.GetComponent<SoldierWeapon>().thirdPersonHands;pose.leftGrip.position=pose.rightGrip.position+Find(arms,"hand_l").position-hand.position;tp.GetComponent<SoldierWeapon>().thirdPersonHands=pose;
   var grenade=BuildGrenade();fp.grenadeView=grenade;
   Directory.CreateDirectory(Root+"/Prefabs");var fpPrefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+key+"_FPS.prefab").GetComponent<SoldierWeaponView>();
   var tpPrefab=PrefabUtility.SaveAsPrefabAsset(tp.gameObject,Root+"/Prefabs/"+key+"_TPS.prefab").GetComponent<SoldierWeaponView>();UnityEngine.Object.DestroyImmediate(tp.gameObject);
   entries.Add(new WeaponCatalog.Entry{weapon=definition,basePrefab=tpPrefab.gameObject,firstPersonPrefab=fpPrefab,thirdPersonPrefab=tpPrefab,polished=false,sights=new[]{irons},attachments=Array.Empty<WeaponAttachmentDefinition>(),defaultSight=irons,animationStyle="UE4 source FPS / placeholder TPS"});
   report.Add(title+" | native hands + synchronized parts | reload="+definition.reloadDuration.ToString("F3")+" | rightHandInWeapon="+nativeRightInWeapon.ToString("F4"));
  }
  finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 static SoldierWeaponView BuildThirdPerson(string key,string modelPath,AnimationClip idle,AnimationClip fire,AnimationClip reload,WeaponCatalog.Entry reference,SoldierWeaponDefinition def,Vector3 handInWeapon,Quaternion gunOrientation)
 {
  var root=new GameObject(key+"_TPS");var gun=Model(modelPath,root.transform,"SourceWeapon");if(idle)idle.SampleAnimation(gun,0);
  var pose=reference.thirdPersonPrefab.GetComponent<SoldierWeapon>().thirdPersonHands.rightGrip;
  // The serialized grip target and equipped view share the weapon follow frame.
  gun.transform.localRotation=gunOrientation;
  gun.transform.localPosition=pose.position-gun.transform.localRotation*handInWeapon;
  var i=Merge(key+"_PartsIdle",null,"",idle,"SourceWeapon",true);var f=Merge(key+"_PartsFire",null,"",fire?fire:idle,"SourceWeapon",false);var r=Merge(key+"_PartsReload",null,"",reload?reload:idle,"SourceWeapon",false);
  var controller=Controller(key+"_Parts",i,f,r,null,null);def.firstPersonParts=controller;EditorUtility.SetDirty(def);
  var view=FinishView(root,gun.transform,controller,reference.thirdPersonPrefab.mountPosition,Quaternion.identity);return view;
 }
 static AuthoredGrenadeView BuildGrenade()
 {
  string path=Root+"/Prefabs/Grenade_UE4_FPS.prefab";var existing=AssetDatabase.LoadAssetAtPath<AuthoredGrenadeView>(path);if(existing)return existing;
  var source=catalog.Entries.First(e=>e.source.EndsWith("/AM_Grenade_Single"));var clip=Clip(source.source);
  var root=new GameObject("Grenade_UE4_FPS");var arms=Model(Pack+"Meshes/Characters/FPS_Arms_UE4/SK_Arms_Mono.fbx",root.transform,"SourceArms");
  var merged=Merge("Grenade_UE4_Throw",clip,"SourceArms",null,"",false);var controller=Controller("Grenade_UE4",merged,merged,merged,null,null);
  foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())skin.updateWhenOffscreen=true;
  root.AddComponent<Animator>().runtimeAnimatorController=controller;
  root.transform.SetPositionAndRotation(new Vector3(0,-1.65f,0),Quaternion.Euler(0,180,0));var view=root.AddComponent<AuthoredGrenadeView>();view.hand=Find(arms,"hand_r");view.duration=clip.length;view.releaseTime=clip.length*.58f;view.handPosition=new Vector3(.07f,.02f,0);view.handRotation=new Vector3(90,0,0);
  Directory.CreateDirectory(Path.GetDirectoryName(path));var saved=PrefabUtility.SaveAsPrefabAsset(root,path).GetComponent<AuthoredGrenadeView>();UnityEngine.Object.DestroyImmediate(root);return saved;
 }
 static void BuildExternal(string folder,string title,string referenceId,string fireName,string reloadName,string equipName,string outName)
 {
  string sourceFolder="Assets/Imported/FPSAnimationTests/"+folder;
  string modelPath=AssetDatabase.FindAssets("t:Model",new[]{sourceFolder}).Select(AssetDatabase.GUIDToAssetPath).First(p=>p.EndsWith(".fbx"));
  var sourceClips=AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")).ToArray();
  AnimationClip Take(string name)=>name==null?null:sourceClips.First(c=>c.name==name);
  var fire=Take(fireName);var reload=Take(reloadName);if(folder=="animated-shotgun")reload=Concat("RemingtonFullReload",new[]{Take("Armature|ReloadStart")}.Concat(Enumerable.Repeat(reload,6)).Concat(new[]{Take("Armature|ReloadEnd")}).ToArray());var idle=Hold(sourceClips.FirstOrDefault(c=>c.name.EndsWith("|Idle")||c.name.EndsWith("_Idle"))??fire);
  var reference=original.entries.First(e=>e.weapon.catalogId==referenceId);string key=Clean(title)+"_Source";
  var root=new GameObject(key+"_FPS");
  try{
   var rig=Model(modelPath,root.transform,"SourceRig");idle.SampleAnimation(rig,0);ApplyExternalMaterials(rig,sourceFolder);
   var hand=Find(rig,"hand_L.R")??Find(rig,"hand_R");if(!hand)throw new Exception("No right hand in "+folder);
   var eye=Find(rig,"camera");Vector3 eyePosition=eye?eye.position:new Vector3(0,34,0);
   const float scale=.02f;var fpPosition=-eyePosition*scale;
   var i=Merge(key+"_Idle",idle,"SourceRig",null,"",true);var f=Merge(key+"_Fire",fire,"SourceRig",null,"",false);var r=Merge(key+"_Reload",reload,"SourceRig",null,"",false);
   var eq=Merge(key+"_Equip_FirstPerson",Take(equipName)??idle,"SourceRig",null,"",false);var ou=Merge(key+"_Unequip_FirstPerson",Take(outName)??idle,"SourceRig",null,"",false);
   var controller=Controller(key,i,f,r,eq,ou);var fp=FinishView(root,rig.transform,controller,fpPosition,Quaternion.identity);fp.mountScale=Vector3.one*scale;fp.animationOverride=controller;fp.independentFirstPersonFrame=true;fp.grenadeView=BuildGrenade();
   // Arms were authored in the same FBX as the weapon: never retarget or re-offset one independently.
   var def=UnityEngine.Object.Instantiate(reference.weapon);def.catalogId="animation-test-"+key;def.displayName=title+" (Source)";def.showFirstPersonHands=false;
   def.viewOffset=def.hipFireOffset=def.aimOffset=def.viewRotationOffset=def.hipFireRotationOffset=def.aimRotationOffset=Vector3.zero;def.reloadDuration=r.length;
   def.equipDuration=Mathf.Clamp(eq.length,.4f,.9f);def.unequipDuration=Mathf.Clamp(ou.length,.4f,.9f);def.builtInIronEyeDistance=.22f;
   def=Save(def,Root+"/Definitions/"+key+".asset");var irons=Bare(key);
   var tpRoot=new GameObject(key+"_TPS");var tpRig=Model(modelPath,tpRoot.transform,"SourceRig");idle.SampleAnimation(tpRig,0);ApplyExternalMaterials(tpRig,sourceFolder);
   foreach(var mesh in tpRig.GetComponentsInChildren<Renderer>(true))if(mesh.name=="hardknuckle"||mesh.name=="sleeve")mesh.enabled=false;
   var grip=reference.thirdPersonPrefab.GetComponent<SoldierWeapon>().thirdPersonHands.rightGrip.position;
   tpRig.transform.localScale=Vector3.one*scale;tpRig.transform.localPosition=grip-hand.position*scale;
   var parts=Controller(key+"_Parts",i,f,r,null,null);def.firstPersonParts=parts;EditorUtility.SetDirty(def);
   var tp=FinishView(tpRoot,tpRig.transform,parts,reference.thirdPersonPrefab.mountPosition,Quaternion.identity);AddData(tpRoot,def,reference,irons,fp);
   tpRoot.GetComponent<SoldierWeaponSights>().referenceWeaponScale=fp.mountScale;
   Directory.CreateDirectory(Root+"/Prefabs");var fpSaved=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+key+"_FPS.prefab").GetComponent<SoldierWeaponView>();var tpSaved=PrefabUtility.SaveAsPrefabAsset(tpRoot,Root+"/Prefabs/"+key+"_TPS.prefab").GetComponent<SoldierWeaponView>();UnityEngine.Object.DestroyImmediate(tpRoot);
   entries.Add(new WeaponCatalog.Entry{weapon=def,basePrefab=tpSaved.gameObject,firstPersonPrefab=fpSaved,thirdPersonPrefab=tpSaved,polished=false,sights=new[]{irons},attachments=Array.Empty<WeaponAttachmentDefinition>(),defaultSight=irons,animationStyle="Authored source FPS / placeholder TPS"});
   report.Add(def.displayName+" | combined native weapon/arms rig | reload="+r.length+" | scale="+scale+" | eye="+eyePosition);
  }finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 public static object SanitizeSavedViews()
 {
  int removed=0;
  // Imported FBX camera components can survive a model clone as prefab overrides.
  // Remove them on the saved gameplay prefab as well; source FBXs remain intact.
  foreach(var path in AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"}).Select(AssetDatabase.GUIDToAssetPath))
  {
   var root=PrefabUtility.LoadPrefabContents(path);
   try
   {
    var view=root.GetComponent<SoldierWeaponView>();
    if(view)
    {
     string key=Path.GetFileNameWithoutExtension(path);bool fps=key.EndsWith("_FPS");
     key=key.Substring(0,key.Length-4);
     var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/Controllers/"+key+(fps?"":"_Parts")+".controller");
     view.animator.runtimeAnimatorController=controller;if(fps)view.animationOverride=controller;EditorUtility.SetDirty(view);
    }
    var nested=root.GetComponentsInChildren<Transform>(true).Select(t=>PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject)).Where(g=>g&&g!=root).Distinct().ToArray();
    foreach(var model in nested)if(PrefabUtility.IsPartOfPrefabInstance(model))PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
    foreach(var camera in root.GetComponentsInChildren<Camera>(true)){RemoveCamera(camera);removed++;}
    foreach(var light in root.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(light);
    foreach(var listener in root.GetComponentsInChildren<AudioListener>(true))UnityEngine.Object.DestroyImmediate(listener);
    PrefabUtility.SaveAsPrefabAsset(root,path);
   }
   finally{PrefabUtility.UnloadPrefabContents(root);}
   AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
  }
  AssetDatabase.SaveAssets();return removed;
 }
 static GameObject DisplayBase(SoldierWeaponView prefab,string key)
 {
  var source=UnityEngine.Object.Instantiate(prefab.gameObject);var root=new GameObject(key+"_Display");
  try
  {
   var control=(AnimatorController)prefab.animator.runtimeAnimatorController;
   var idle=control.layers[0].stateMachine.defaultState.motion as AnimationClip;
   if(idle)idle.SampleAnimation(source,0);
   int index=0;
   foreach(var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled))
   {
    var mesh=new Mesh{name=key+"_Display_"+index};skin.BakeMesh(mesh,true);
    mesh=Save(mesh,Root+"/Meshes/"+key+"_"+index+++".asset");
    var node=new GameObject(skin.name);node.transform.SetParent(root.transform,false);
    var matrix=source.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix;
    node.transform.SetLocalPositionAndRotation(matrix.GetColumn(3),matrix.rotation);node.transform.localScale=matrix.lossyScale;
    node.AddComponent<MeshFilter>().sharedMesh=mesh;
    node.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials.Select(material=>
    {
     var preview=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name=key+"_Display_"+material.name};
     preview.SetColor("_BaseColor",new Color(1.8f,1.8f,1.8f,1));preview.SetTexture("_BaseMap",material.GetTexture("_BaseMap"));
     return Save(preview,Root+"/Materials/"+preview.name+".mat");
    }).ToArray();
   }
   return PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+key+"_Display.prefab");
  }
  finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(root);}
 }
 static void BuildExternalDisplays()
 {
  foreach(var entry in entries.Where(e=>e.weapon.catalogId.EndsWith("_Source")))
   entry.basePrefab=DisplayBase(entry.thirdPersonPrefab,entry.weapon.catalogId.Replace("animation-test-",""));
 }
 public static object RepairExternalDisplays()
 {
  var c=AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Root+"/AnimationTestCatalog.asset");entries.Clear();entries.AddRange(c.entries);BuildExternalDisplays();c.entries=entries.ToArray();EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();return "Static source weapon displays prepared";
 }
 static void ApplyExternalMaterials(GameObject model,string folder)
 {
  var textures=AssetDatabase.FindAssets("t:Texture2D",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
  foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
  {
   renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>{
    string family=source.name.Replace(" (Instance)","");if(family.StartsWith("Default_OBJ"))family="weapon_raging";
    string texture=textures.FirstOrDefault(p=>Path.GetFileName(p).StartsWith(family+"_",StringComparison.OrdinalIgnoreCase)&&(p.Contains("Base_color")||p.Contains("BaseColor")||p.EndsWith("_D.png")));
    var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=Clean(Path.GetFileName(folder)+"_"+family)};mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Smoothness",.28f);
    if(texture!=null)mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
    string normal=textures.FirstOrDefault(p=>Path.GetFileName(p).StartsWith(family+"_",StringComparison.OrdinalIgnoreCase)&&(p.Contains("Normal")||p.EndsWith("_N.png")));
    if(normal!=null){var importer=(TextureImporter)AssetImporter.GetAtPath(normal);if(importer.textureType!=TextureImporterType.NormalMap){importer.textureType=TextureImporterType.NormalMap;importer.SaveAndReimport();}mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normal));mat.EnableKeyword("_NORMALMAP");}
    return Save(mat,Root+"/Materials/"+mat.name+".mat");
   }).ToArray();
  }
 }
}
