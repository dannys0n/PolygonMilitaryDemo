using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.Animations;using UnityEngine;using Object=UnityEngine.Object;

// Reuse the production motion state machine with generic, rig-independent motion carriers.
public static class Mk18GameplayBuilder
{
 const string Root=Mk18Ue4StageBuilder.Root;
 static T Save<T>(T value,string path)where T:Object {value.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(value,path);return value;}
 static AnimationClip Prefix(AnimationClip input,string folder)
 {
  var clip=new AnimationClip{frameRate=input.frameRate};foreach(var b in AnimationUtility.GetCurveBindings(input)){var target=b;target.path="ViewMotion/WeaponMotion"+(b.path.Length>0?"/"+b.path:"");AnimationUtility.SetEditorCurve(clip,target,AnimationUtility.GetEditorCurve(input,b));}
  // Base clips establish a neutral carrier before additive layers are evaluated.
  foreach(string path in new[]{"ViewMotion","ViewMotion/WeaponMotion"})for(int axis=0;axis<7;axis++)
  {
   var binding=EditorCurveBinding.FloatCurve(path,typeof(Transform),(axis<3?"m_LocalPosition.":"m_LocalRotation.")+"xyzw"[axis<3?axis:axis-3]);
   if(AnimationUtility.GetEditorCurve(clip,binding)==null)AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,input.length,axis==6?1:0));
  }
  AnimationUtility.SetAnimationClipSettings(clip,AnimationUtility.GetAnimationClipSettings(input));clip.EnsureQuaternionContinuity();return Save(clip,folder+"/Gameplay_"+input.name+".anim");
 }
 static AnimationClip Motion(AnimationClip input,AnimationClip neutral,string folder)
 {
  string path=AnimationUtility.GetCurveBindings(input).Any(b=>b.path=="ViewMotion/PolygonSoldier/AuthoredWeaponMotion")?"ViewMotion/PolygonSoldier/AuthoredWeaponMotion":"ViewMotion";
  string output=path=="ViewMotion"?"ViewMotion":"ViewMotion/WeaponMotion";
  var settings=AnimationUtility.GetAnimationClipSettings(input);var reference=settings.hasAdditiveReferencePose&&settings.additiveReferencePoseClip?settings.additiveReferencePoseClip:input;
  float referenceTime=settings.hasAdditiveReferencePose?settings.additiveReferencePoseTime:0;
  float Read(AnimationClip c,string prop,int axis,float time,float fallback){var curve=AnimationUtility.GetEditorCurve(c,EditorCurveBinding.FloatCurve(path,typeof(Transform),prop+"xyzw"[axis]));return curve!=null?curve.Evaluate(time):fallback;}
  Vector3 P(AnimationClip c,float time)=>new Vector3(Read(c,"m_LocalPosition.",0,time,0),Read(c,"m_LocalPosition.",1,time,0),Read(c,"m_LocalPosition.",2,time,0));
  Quaternion Q(AnimationClip c,float time){if(AnimationUtility.GetEditorCurve(c,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation.w"))!=null)return new Quaternion(Read(c,"m_LocalRotation.",0,time,0),Read(c,"m_LocalRotation.",1,time,0),Read(c,"m_LocalRotation.",2,time,0),Read(c,"m_LocalRotation.",3,time,1)).normalized;return Quaternion.Euler(Read(c,"localEulerAnglesRaw.",0,time,0),Read(c,"localEulerAnglesRaw.",1,time,0),Read(c,"localEulerAnglesRaw.",2,time,0));}
  var rp=P(reference,referenceTime);var rq=Q(reference,referenceTime);var tracks=Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray();int frames=Mathf.Max(1,Mathf.CeilToInt(input.length*60));var pivot=new Vector3(0,1.55f,0);
  for(int f=0;f<=frames;f++){float time=input.length*f/frames;var q=Q(input,time)*Quaternion.Inverse(rq);var p=(P(input,time)-rp)*.938631535f+pivot-q*pivot;for(int i=0;i<7;i++)tracks[i].Add(new Keyframe(time,i<3?p[i]:q[i-3]));}
  var clip=new AnimationClip{frameRate=60};for(int i=0;i<7;i++){var keys=tracks[i];var curve=new AnimationCurve(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<1e-6f)?new[]{keys.First(),keys.Last()}:keys.ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(output,typeof(Transform),(i<3?"m_LocalPosition.":"m_LocalRotation.")+"xyzw"[i<3?i:i-3]),curve);}
  clip.EnsureQuaternionContinuity();settings.hasAdditiveReferencePose=true;settings.additiveReferencePoseClip=neutral;settings.additiveReferencePoseTime=0;AnimationUtility.SetAnimationClipSettings(clip,settings);return Save(clip,folder+"/Gameplay_"+input.name+".anim");
 }
 static AnimationClip HeldFire(AnimationClip input,AnimationClip idle)
 {
  // Recoil moves the held assembly. Retargeting a second grip for firing made
  // wrists and fingers change pose on every shot and again when returning idle.
  var clip=Object.Instantiate(input);clip.name=input.name;
  bool Arms(string path)=>path=="SourceArms"||path.StartsWith("SourceArms/");
  foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(b=>Arms(b.path)))AnimationUtility.SetEditorCurve(clip,b,null);
  foreach(var b in AnimationUtility.GetCurveBindings(idle).Where(b=>Arms(b.path)))AnimationUtility.SetEditorCurve(clip,b,AnimationCurve.Constant(0,input.length,AnimationUtility.GetEditorCurve(idle,b).Evaluate(0)));
  float Read(AnimationClip c,string path,string property,float time,float fallback)=>AnimationUtility.GetEditorCurve(c,EditorCurveBinding.FloatCurve(path,typeof(Transform),property))?.Evaluate(time)??fallback;
  Vector3 P(AnimationClip c,string path,float t)=>new Vector3(Read(c,path,"m_LocalPosition.x",t,0),Read(c,path,"m_LocalPosition.y",t,0),Read(c,path,"m_LocalPosition.z",t,0));
  Quaternion Q(AnimationClip c,string path,float t)=>new Quaternion(Read(c,path,"m_LocalRotation.x",t,0),Read(c,path,"m_LocalRotation.y",t,0),Read(c,path,"m_LocalRotation.z",t,0),Read(c,path,"m_LocalRotation.w",t,1)).normalized;
  // Hold both children at idle and animate their common parent. Independent
  // child curves slip apart during Animator crossfades, especially on pistols.
  foreach(var b in AnimationUtility.GetCurveBindings(idle).Where(b=>b.path=="SourceWeapon"))AnimationUtility.SetEditorCurve(clip,b,AnimationCurve.Constant(0,input.length,AnimationUtility.GetEditorCurve(idle,b).Evaluate(0)));
  var pivot=P(idle,"SourceWeapon",0);var start=P(input,"SourceWeapon",0);var startRotation=Q(input,"SourceWeapon",0);
  var tracks=Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray();int frames=Mathf.Max(1,Mathf.CeilToInt(input.length*60));
  for(int f=0;f<=frames;f++)
  {
   float t=input.length*f/frames;var delta=P(input,"SourceWeapon",t)-start;var turn=Q(input,"SourceWeapon",t)*Quaternion.Inverse(startRotation);
   var position=pivot+delta-turn*pivot;
   for(int axis=0;axis<7;axis++)tracks[axis].Add(new Keyframe(t,axis<3?position[axis]:turn[axis-3]));
  }
  for(int axis=0;axis<7;axis++){var curve=new AnimationCurve(tracks[axis].ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Transform),(axis<3?"m_LocalPosition.":"m_LocalRotation.")+"xyzw"[axis<3?axis:axis-3]),curve);}
  clip.EnsureQuaternionContinuity();return clip;
 }
 public static RuntimeAnimatorController Controller(SoldierWeaponDefinition definition,AnimationClip idle,AnimationClip reload,AnimationClip fire,AnimationClip aimFire,string outputFolder=null,bool preserveAuthoredFirePose=false,bool? sharedAdsRecoil=null)
 {
  var original=definition.firstPerson;var source=WeaponAnimationSource.Base(original);var folder=outputFolder??Root;
  string basePath=folder+"/"+(outputFolder==null?"MK18_UE4Gameplay":"UE4Gameplay")+".controller";if(!AssetDatabase.LoadAssetAtPath<AnimatorController>(basePath))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),basePath);
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(basePath);var layers=controller.layers;for(int i=0;i<layers.Length;i++){layers[i].avatarMask=null;if(layers[i].name=="Grenade")layers[i].defaultWeight=0;}controller.layers=layers;EditorUtility.SetDirty(controller);
  var neutral=new AnimationClip{frameRate=60};foreach(string path in new[]{"ViewMotion","ViewMotion/WeaponMotion"})for(int axis=0;axis<7;axis++)neutral.SetCurve(path,typeof(Transform),(axis<3?"m_LocalPosition.":"m_LocalRotation.")+"xyzw"[axis<3?axis:axis-3],AnimationCurve.Constant(0,.1f,axis==6?1:0));neutral=Save(neutral,folder+"/Gameplay_MotionReference.anim");
  var iClip=Prefix(idle,folder);var rClip=Prefix(reload,folder);var held=preserveAuthoredFirePose?fire:HeldFire(fire,idle);var aimed=preserveAuthoredFirePose?aimFire:HeldFire(aimFire,idle);var fClip=Prefix(held,folder);var aClip=Prefix(aimed,folder);if(!preserveAuthoredFirePose){Object.DestroyImmediate(held);Object.DestroyImmediate(aimed);}if(sharedAdsRecoil??SharedAdsRecoil.Supports(definition))aClip=SharedAdsRecoil.Create(aClip,iClip,folder);var replacements=new List<KeyValuePair<AnimationClip,AnimationClip>>();
  var sourceIdle=WeaponAnimationSource.State(original,"Base Layer","Idle");var sourceAim=WeaponAnimationSource.State(original,"Base Layer","Idle",1);
  var sourceReload=WeaponAnimationSource.State(original,"Base Layer","Reload");var sourceFire=WeaponAnimationSource.State(original,"Base Layer","Fire");var sourceAimFire=WeaponAnimationSource.State(original,"Base Layer","Fire",1);
  foreach(var clip in controller.animationClips.Distinct())
  {
   var input=WeaponAnimationSource.Resolve(original,clip);AnimationClip converted;
   // These legacy gestures already move between hip and ADS. The authored rig
   // uses the sight placement solver for that motion, so do not apply it twice.
   if(input.name.Contains("GrenadeThrow")||input.name.Contains("Aim_In_Transition")||input.name.Contains("Aim_Out_Transition"))converted=neutral;
   else if(input==sourceReload)converted=rClip;
   else if(input==sourceAimFire)converted=aClip;
   else if(input==sourceFire)converted=fClip;
   else if(input==sourceIdle||input==sourceAim)converted=iClip;
   else converted=Motion(input,neutral,folder);
   replacements.Add(new KeyValuePair<AnimationClip,AnimationClip>(clip,converted));
  }
  var result=new AnimatorOverrideController(controller);result.ApplyOverrides(replacements);return Save(result,folder+"/"+(outputFolder==null?"MK18_UE4Gameplay":"UE4Gameplay")+".overrideController");
 }
 public static void PrepareView(SoldierWeaponView view)
 {
  var children=view.transform.Cast<Transform>().ToArray();var motion=new GameObject("ViewMotion").transform;motion.SetParent(view.transform,false);var weaponMotion=new GameObject("WeaponMotion").transform;weaponMotion.SetParent(motion,false);foreach(var child in children)child.SetParent(weaponMotion,false);
  view.sightPlacementRoot=view.GetComponentsInChildren<Transform>(true).First(t=>t.name=="SourceWeapon");view.gameObject.AddComponent<SoldierAuthoredWeaponMotion>();
 }
 public static void CalibrateSights(GameObject thirdPerson,SoldierWeaponView view)
 {
  var sights=thirdPerson.GetComponent<SoldierWeaponSights>();
  var frame=Matrix4x4.TRS(view.mountPosition,view.mountRotation,view.mountScale)*view.transform.worldToLocalMatrix*view.sightPlacementRoot.localToWorldMatrix;
  if(view.sharedFirstPersonRig)
  {
   // The gun's mount TRS is relative to the shared rig, not FirstPersonView.
   // Sample its neutral authored pose before saving the existing ADS solver's frame.
   var rig=Object.Instantiate(view.sharedFirstPersonRig);
   try
   {
    var shared=rig.GetComponent<SoldierSharedFirstPersonRig>();
    rig.transform.SetLocalPositionAndRotation(shared.mountPosition,shared.mountRotation);rig.transform.localScale=shared.mountScale;
    var mount=string.IsNullOrEmpty(view.sharedWeaponMountPath)?shared.weaponMount:rig.transform.Find(view.sharedWeaponMountPath);
    if(!mount)throw new InvalidOperationException("Missing shared FPS weapon mount for "+view.name);
    var weapon=Object.Instantiate(view,mount);weapon.name="SourceWeapon";
    weapon.transform.SetLocalPositionAndRotation(view.mountPosition,view.mountRotation);weapon.transform.localScale=view.mountScale;
    var idle=WeaponAnimationSource.State(view.animationOverride,"Base Layer","Idle");
    if(!idle)throw new InvalidOperationException("Missing neutral FPS pose for "+view.name);
    idle.SampleAnimation(rig,0);
    var root=weapon.sightPlacementRoot?weapon.sightPlacementRoot:weapon.transform;
    frame=Matrix4x4.TRS(shared.mountPosition,shared.mountRotation,shared.mountScale)*rig.transform.worldToLocalMatrix*root.localToWorldMatrix;
   }
   finally{Object.DestroyImmediate(rig);}
  }
  sights.referenceWeaponPosition=frame.GetColumn(3);sights.referenceWeaponRotation=frame.rotation;sights.referenceWeaponScale=frame.lossyScale;
 }
}
