using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Offline reload variants. Shared gameplay data never references the FPS animation graphs.
public static class WeaponReloadBuilder
{
 const string Root="Assets/Game/Animations/Weapons/Reloads";
 const string Catalog="Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset";
 static readonly HashSet<string> Families=new(){"AKX","ASVal","Colibri","DGL50","Drake-12","G3","L96X","M1911","MGX5","MPS5","MX16A4","Mk14EBR","PDW90","SVD","Striker-V","X18"};
 public sealed class Job
 {
  public WeaponCatalog.Entry entry;public string key,family;
  public string id=>entry.weapon.catalogId;
  public string Folder(bool less=false)=>Root+"/"+entry.weapon.name+"/"+(less?"TacticalLess":"Tactical");
 }
 public static Job[] Jobs()=>AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Catalog).entries.Where(e=>e.polished&&e.firstPersonPrefab&&e.firstPersonPrefab.sharedFirstPersonRig).Select(e=>
 {
  var match=Regex.Match(e.animationStyle??"",@"Native UE4 ([^ /]+)_UE4");string key=match.Success?match.Groups[1].Value:null;
  string family=key=="AKX12"||key=="AKX200"?"AKX":key=="Val"?"ASVal":key=="M14"?"Mk14EBR":key;
  return new Job{entry=e,key=key,family=family};
 }).Where(j=>j.family!=null&&Families.Contains(j.family)).ToArray();
 static Job Get(string id)=>Jobs().First(j=>j.id==id);
 public static T Save<T>(T value,string path)where T:Object
 {
  value.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<T>(path);
  if(old){EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
  AssetDatabase.CreateAsset(value,path);return value;
 }
 static void CheckEditor(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Author reloads in Edit Mode after compilation.");}
 static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
 static AnimationClip Action(FpsPackSourceLayout layout,string source)
 {
  var action=layout.actions.FirstOrDefault(a=>a.source==source);if(action==null)throw new InvalidOperationException("Missing source action "+source);
  return AssetDatabase.LoadAssetAtPath<AnimationClip>(FpsPackPreviewCatalog.Root+action.file);
 }
 static FpsPackSourceLayout.Preset Preset(FpsPackSourceLayout layout,Job job)
  =>layout.presets.FirstOrDefault(p=>p.id=="DA_"+job.key)??layout.presets.First(p=>p.family==job.family);
 public static RuntimeAnimatorController Override(RuntimeAnimatorController source,Dictionary<AnimationClip,AnimationClip> replacements,string path)
 {
  var result=new AnimatorOverrideController(WeaponAnimationSource.Base(source));var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();result.GetOverrides(pairs);
  for(int i=0;i<pairs.Count;i++){var key=pairs[i].Key;var current=WeaponAnimationSource.Resolve(source,key);pairs[i]=new(key,replacements.TryGetValue(current,out var next)?next:current);}
  result.ApplyOverrides(pairs);return Save(result,path);
 }
 public static object Prepare(string id,bool less=false)
 {
  CheckEditor();var job=Get(id);string folder=job.Folder(less);Directory.CreateDirectory(folder);AssetDatabase.Refresh();
  var layout=FpsPackSourceLayout.Read();var preset=Preset(layout,job);var links=preset.variants.First(v=>v.rig=="UE4").links;
  var link=links.First(l=>l.key==(less?"AdditionalReload":"SecondaryReload"));var primary=links.First(l=>l.key=="PrimaryReload");
  var character=Action(layout,link.character);var weapon=Action(layout,link.weapon);var originalCharacter=Action(layout,primary.character);var originalWeapon=Action(layout,primary.weapon);
  var entry=job.entry;var view=entry.firstPersonPrefab;var idle=WeaponAnimationSource.State(view.animationOverride,"Base Layer","Idle");var baseline=WeaponAnimationSource.State(view.animationOverride,"Base Layer","Reload");
  float timeScale=entry.weapon.fullReload.duration/originalCharacter.length;
  var root=Object.Instantiate(view.sharedFirstPersonRig);GameObject native=null;Matrix4x4 gunFromNative=Matrix4x4.identity;bool existing=entry.animationStyle.Contains("existing Polygon frame");
  try
  {
   var gun=Object.Instantiate(view.gameObject,root.transform.Find(view.sharedWeaponMountPath),false);gun.name="SourceWeapon";gun.transform.SetLocalPositionAndRotation(view.mountPosition,view.mountRotation);gun.transform.localScale=view.mountScale;
   foreach(var animator in root.GetComponentsInChildren<Animator>(true))animator.enabled=false;
   var arms=Find(root,"SourceArms");idle.SampleAnimation(root,0);
   var bones=AnimationUtility.GetCurveBindings(baseline).Where(b=>b.type==typeof(Transform)).Select(b=>root.transform.Find(b.path)).Where(t=>t).Distinct().ToArray();
   var rest=bones.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();var armRest=(arms.localPosition,arms.localRotation,arms.localScale);
   void Restore(){for(int i=0;i<bones.Length;i++){bones[i].SetLocalPositionAndRotation(rest[i].localPosition,rest[i].localRotation);bones[i].localScale=rest[i].localScale;}}
   NativePartFit fit=null;
   if(gun.transform.Find("PolygonParts")||existing)
   {
    var manifest=JsonUtility.FromJson<FpsPackActionBuilder.Manifest>("{\"assets\":"+File.ReadAllText(FpsPackPreviewCatalog.Root+"export-manifest.json")+"}");
    var model=AssetDatabase.LoadAssetAtPath<GameObject>(manifest.assets.First(m=>m.source==preset.mesh).Path);
    native=Object.Instantiate(model,gun.transform.parent,false);native.name="Reload fitting source";native.transform.SetLocalPositionAndRotation(gun.transform.localPosition,gun.transform.localRotation);native.transform.localScale=gun.transform.localScale;
    foreach(var a in native.GetComponentsInChildren<Animator>(true))a.enabled=false;
    if(existing){var source=Jobs().First(j=>j.key==job.key&&j.id.StartsWith("ue4-original-proportions-")&&!j.entry.animationStyle.Contains("existing Polygon frame")&&!j.entry.firstPersonPrefab.transform.Find("PolygonParts")).entry.firstPersonPrefab;
     native.transform.SetParent(root.transform.Find(source.sharedWeaponMountPath),false);native.transform.SetLocalPositionAndRotation(source.mountPosition,source.mountRotation);native.transform.localScale=source.mountScale;}
    originalWeapon.SampleAnimation(native,0);Restore();gunFromNative=native.transform.worldToLocalMatrix*gun.transform.localToWorldMatrix;
    var fittedBones=existing?bones.Where(b=>b!=gun.transform&&b.IsChildOf(gun.transform)&&!PlacementControl(AnimationUtility.CalculateTransformPath(b,root.transform))).ToArray():null;
    fit=FitParts(job,root,gun.gameObject,native,baseline,originalWeapon,idle,fittedBones,existing?NativeRest(job.key):null);
    Restore();
   }
   var thumb=job.family=="Colibri"?new[]{Find(root,"thumb_01_l"),Find(root,"thumb_01_r")}:Array.Empty<Transform>();
   var thumbIdle=thumb.Select(t=>t.localRotation).ToArray();originalCharacter.SampleAnimation(arms.gameObject,0);
   var corrections=thumb.Select((t,i)=>thumbIdle[i]*Quaternion.Inverse(t.localRotation)).ToArray();Restore();
   var tracks=bones.Where(b=>!PlacementControl(AnimationUtility.CalculateTransformPath(b,root.transform))).Select(b=>new TpsPartsAdapter.PoseTrack{path=AnimationUtility.CalculateTransformPath(b,root.transform),source=b}).ToArray();
   float duration=character.length*timeScale;int frames=Mathf.CeilToInt(duration*60);var times=new float[frames+1];
   for(int f=0;f<=frames;f++)
   {
    float time=duration*f/frames;times[f]=time;Restore();character.SampleAnimation(arms.gameObject,time/timeScale);arms.SetLocalPositionAndRotation(armRest.localPosition,armRest.localRotation);arms.localScale=armRest.localScale;
    if(native){weapon.SampleAnimation(native,time/timeScale);if(existing){var frame=native.transform.localToWorldMatrix*gunFromNative;gun.transform.SetPositionAndRotation(frame.GetColumn(3),frame.rotation);}fit.Sample();}else weapon.SampleAnimation(gun.gameObject,time/timeScale);
    float phase=time/duration;float grip=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.12f,phase))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,.88f,phase));
    for(int i=0;i<thumb.Length;i++)thumb[i].localRotation=Quaternion.Slerp(Quaternion.identity,corrections[i],grip)*thumb[i].localRotation;
    foreach(var track in tracks)track.Sample();
   }
   var clip=new AnimationClip{frameRate=60};foreach(var track in tracks)TpsPartsAdapter.Write(clip,track,times);
   clip=Save(clip,folder+"/ReloadFPS.anim");
   if(existing||id.Contains("polygon-submachine5")){var contactBones=tracks.Select(t=>t.source).OrderBy(t=>AnimationUtility.CalculateTransformPath(t,root.transform).Count(c=>c=='/')).ToArray();clip=GripReloadContinuity.Bake(root,gun.transform,contactBones,idle,clip,idle,folder+"/ReloadFPS.anim",duration);}
   AssetDatabase.SaveAssetIfDirty(clip);
   return new{id,job.family,duration=clip.length,timeScale,fitted=fit!=null,tracks=tracks.Length,bytes=new FileInfo(folder+"/ReloadFPS.anim").Length};
  }
  finally{if(native)Object.DestroyImmediate(native);Object.DestroyImmediate(root);}
 }
 public static Dictionary<Transform,List<Vector3>> Geometry(GameObject root)
 {
  var result=new Dictionary<Transform,List<Vector3>>();
  foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
  {
   if(!skin.sharedMesh)continue;var mesh=new Mesh();skin.BakeMesh(mesh);var weights=skin.sharedMesh.boneWeights;var vertices=mesh.vertices;
   for(int i=0;i<vertices.Length&&i<weights.Length;i++)
   {
    var bone=skin.bones[weights[i].boneIndex0];if(!result.TryGetValue(bone,out var points))result[bone]=points=new();points.Add(skin.transform.TransformPoint(vertices[i]));
   }
   Object.DestroyImmediate(mesh);
  }
  foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh)result[filter.transform]=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToList();
  return result;
 }
 // Empty reloads can begin with an open slide. Register mechanism deltas against the closed idle, not frame zero of that reload.
 public static Dictionary<string,(Vector3 p,Quaternion q,Vector3 s)> NativeRest(string key)
 {
  var view=Jobs().First(j=>j.key==key&&j.id.StartsWith("ue4-original-proportions-")&&!j.entry.animationStyle.Contains("existing Polygon frame")&&!j.entry.firstPersonPrefab.transform.Find("PolygonParts")).entry.firstPersonPrefab;
  var root=Object.Instantiate(view.sharedFirstPersonRig);
  try{var gun=Object.Instantiate(view.gameObject,root.transform.Find(view.sharedWeaponMountPath),false);gun.name="SourceWeapon";gun.transform.SetLocalPositionAndRotation(view.mountPosition,view.mountRotation);gun.transform.localScale=view.mountScale;foreach(var a in root.GetComponentsInChildren<Animator>(true))a.enabled=false;WeaponAnimationSource.State(view.animationOverride,"Base Layer","Idle").SampleAnimation(root,0);return gun.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.bones).Where(t=>t).Distinct().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>{var t=g.First();return(t.localPosition,t.localRotation,t.localScale);});}
  finally{Object.DestroyImmediate(root);}
 }
 public static NativePartFit FitParts(Job job,GameObject fps,GameObject gun,GameObject native,AnimationClip baseline,AnimationClip originalWeapon,AnimationClip idle,Transform[] fittedBones=null,Dictionary<string,(Vector3 p,Quaternion q,Vector3 s)> nativeRest=null)
 {
  var source=native.GetComponentsInChildren<Transform>(true);var sourceRoot=source.First(b=>b.name=="Root"||b.name=="Body");
  var targets=fittedBones??gun.transform.Find("PolygonParts").GetComponentsInChildren<Transform>(true).Skip(1).ToArray();
  Transform Match(Transform t)
  {
   string n=t.name.ToLowerInvariant(),semantic=null;
   if(n=="bone"||n=="main"||n=="root")semantic=sourceRoot.name;
   if(n.Contains("magazine")||n=="mag"||t.name=="Bone.002")semantic="Mag";
   if(n.Contains("slide")||n.Contains("charge")||n.Contains("bolt")||t.name=="Bone.003")semantic="Bolt";
   if(t.name=="Bone.004")semantic="ChargingHandle";
   if(n.Contains("trigger"))semantic="Trigger";if(n=="open")semantic="Top";if(n=="reload")semantic=job.family=="MX16A4"?"ChargingHandle":"Rail";if(n=="output_bullet")semantic="Bolt";if(n=="bullet_holder")semantic="Feed_Tray";
   if(job.family=="MPS5"&&t.name=="Bone.003")semantic="ChargingHandle";
   if(job.family=="MPS5"&&t.name=="Bone.004")semantic="Bolt";
   if(job.family=="Striker-V"&&t.name=="Bone.006")semantic="ActiveCharging";
   if(job.family=="Striker-V"&&(t.name=="Bone.003"||t.name=="Bone.004"))semantic="StaticCharging";
   if(job.family=="MGX5"&&t.name.StartsWith("Bone.")&&int.TryParse(t.name.Substring(5),out int belt))semantic=belt==6?"bullet":"bullet_"+((belt-6)/2).ToString("D3");
   if(job.family=="M1911"||job.family=="X18"){
    if(n.Contains("slide")||n=="reload"||t.name=="Bone.004")semantic="Slider";
    else if(t.name=="Bone.003"||t.name=="Bone.005")semantic=sourceRoot.name;
   }
   var aliases=semantic=="Mag"?new[]{"Mag","Magazine","Bag"}:semantic=="Bolt"?new[]{"Bolt","ChargingHandle","Charger","Slider"}:semantic=="Feed_Tray"?new[]{"Feed_Tray","Top"}:new[]{semantic??""};
   return aliases.Select(a=>source.FirstOrDefault(b=>b.name==a)).FirstOrDefault(b=>b)??sourceRoot;
  }
  var mapping=targets.ToDictionary(t=>t,Match);
  void ResetNative(){originalWeapon.SampleAnimation(native,0);if(nativeRest!=null)foreach(var t in source)if(nativeRest.TryGetValue(t.name,out var rest)){t.SetLocalPositionAndRotation(rest.p,rest.q);t.localScale=rest.s;}}
  idle.SampleAnimation(fps,0);ResetNative();
  var targetRest=targets.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();
  var sourceRest=targets.Select(t=>(mapping[t].localPosition,mapping[t].localRotation)).ToArray();
  var sourcePos=new float[targets.Length];var targetPos=new float[targets.Length];var sourceRot=new float[targets.Length];var targetRot=new float[targets.Length];
  for(int f=0;f<=24;f++)
  {
   baseline.SampleAnimation(fps,baseline.length*f/24);originalWeapon.SampleAnimation(native,originalWeapon.length*f/24);
   for(int i=0;i<targets.Length;i++)
   {
    sourcePos[i]=Mathf.Max(sourcePos[i],Vector3.Distance(mapping[targets[i]].localPosition,sourceRest[i].localPosition));
    targetPos[i]=Mathf.Max(targetPos[i],Vector3.Distance(targets[i].localPosition,targetRest[i].localPosition));
    sourceRot[i]=Mathf.Max(sourceRot[i],Quaternion.Angle(mapping[targets[i]].localRotation,sourceRest[i].localRotation));
    targetRot[i]=Mathf.Max(targetRot[i],Quaternion.Angle(targets[i].localRotation,targetRest[i].localRotation));
   }
  }
  idle.SampleAnimation(fps,0);ResetNative();
  for(int i=0;i<targets.Length;i++){targets[i].SetLocalPositionAndRotation(targetRest[i].localPosition,targetRest[i].localRotation);targets[i].localScale=targetRest[i].localScale;}
  var fit=new NativePartFit();
  for(int i=0;i<targets.Length;i++)
  {
   var t=targets[i];bool follows=mapping.TryGetValue(t.parent,out var parent)&&parent==mapping[t];
   float rs=sourceRot[i]>.1f?Mathf.Clamp(targetRot[i]/sourceRot[i],0,3):1;
   float ps=sourcePos[i]>.0001f?Mathf.Clamp(targetPos[i]/sourcePos[i],0,3):1;
   fit.Add(mapping[t],t,native.transform,gun.transform,follows,rs,ps);
  }
  var polygon=Geometry(gun);polygon=polygon.Where(p=>mapping.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
  fit.ConfigureHand(fps,gun.transform,Geometry(native),polygon,mapping,targets.ToDictionary(t=>t,t=>t));
  return fit;
 }
 public static object BuildTps(string id,bool less=false)
 {
  CheckEditor();var job=Get(id);var entry=job.entry;var folder=job.Folder(less);
  var proof=ScriptableObject.CreateInstance<WeaponAnimationConversionProof>();
  try
  {
   proof.originalProportions=true;proof.source=proof.converted=entry;
   proof.idle=WeaponAnimationSource.State(entry.firstPersonPrefab.animationOverride,"Base Layer","Idle");
   proof.reload=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/ReloadFPS.anim");
   if(!proof.reload)throw new InvalidOperationException("Prepare FPS reload first: "+id);
   proof.thirdPersonSourceController=entry.weapon.fullReload.thirdPerson;
   proof.thirdPersonSourceParts=entry.weapon.fullReload.thirdPersonParts;
   return TpsAnimationAdapter.BuildReload(proof,folder);
  }
  finally{Object.DestroyImmediate(proof);}
 }
 static bool PlacementControl(string path)=>path=="ViewMotion"||path=="ViewMotion/WeaponMotion"||path.Split('/').Any(n=>n=="SightReferences"||n.Contains("Sight")||n.Contains("Socket")||n.Contains("Mount")||n=="MuzzleFlash"||n=="ShellEjection");
 static int StripPlacementCurves(AnimationClip clip)
 {
  var bindings=AnimationUtility.GetCurveBindings(clip).Where(b=>PlacementControl(b.path)).ToArray();
  if(bindings.Length>0){AnimationUtility.SetEditorCurves(clip,bindings,new AnimationCurve[bindings.Length]);EditorUtility.SetDirty(clip);}
  return bindings.Length;
 }
 public static int CleanPlacementCurves(AnimationClip clip)=>StripPlacementCurves(clip);
 public static object Apply(string id,bool less=false)
 {
  CheckEditor();var job=Get(id);var entry=job.entry;var folder=job.Folder(less);var view=entry.firstPersonPrefab;var profile=entry.weapon.fullReload;
  AnimationClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/"+name+".anim")??throw new InvalidOperationException("Missing baked reload: "+folder+"/"+name);
  StripPlacementCurves(Clip("ReloadFPS"));StripPlacementCurves(Clip("ReloadParts"));
  var fps=Override(view.animationOverride,new(){[WeaponAnimationSource.State(view.animationOverride,"Base Layer","Reload")]=Clip("ReloadFPS")},folder+"/TacticalFPS.overrideController");
  var body=profile.thirdPerson?profile.thirdPerson:entry.weapon.thirdPerson;
  profile.thirdPerson=Override(body,new(){[WeaponAnimationSource.State(body,"Reload","Reload")]=Clip("Reload"),[WeaponAnimationSource.State(body,"WeaponPose","Reload")]=Clip("ReloadWeapon")},folder+"/TacticalTPS.overrideController");
  var parts=profile.thirdPersonParts?profile.thirdPersonParts:entry.weapon.thirdPersonParts;
  profile.thirdPersonParts=Override(parts,new(){[WeaponAnimationSource.State(parts,"Base Layer","Reload")]=Clip("ReloadParts")},folder+"/TacticalParts.overrideController");
  profile.duration=Clip("ReloadFPS").length;profile.roundReload=default;
  if(!less)
  {
   var path=AssetDatabase.GetAssetPath(view);var prefab=PrefabUtility.LoadPrefabContents(path);
   try{prefab.GetComponent<SoldierWeaponView>().tacticalReloadAnimationOverride=fps;PrefabUtility.SaveAsPrefabAsset(prefab,path);}
   finally{PrefabUtility.UnloadPrefabContents(prefab);}
   entry.weapon.tacticalReload=profile;EditorUtility.SetDirty(entry.weapon);
  }
  AssetDatabase.SaveAssets();return new{id,entry.weapon.displayName,full=entry.weapon.fullReload.duration,tactical=profile.duration,active=!less};
 }
}
