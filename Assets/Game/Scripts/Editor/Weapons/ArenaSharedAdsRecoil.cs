using System;using System.Linq;using UnityEditor;using UnityEngine;
using Object=UnityEngine.Object;

// Editor-only: reuse recoil motion without transferring another weapon's grip or placement.
public static class ArenaSharedAdsRecoil
{
 const string Carrier="ViewMotion/WeaponMotion";
 public static bool Supports(SoldierWeaponDefinition weapon)
 {
  if(!weapon)return false;
  var kind=weapon.category;
  if(kind!=SoldierWeaponDefinition.WeaponCategory.Rifle&&kind!=SoldierWeaponDefinition.WeaponCategory.SMG&&kind!=SoldierWeaponDefinition.WeaponCategory.LMG)return false;
  string id=weapon.catalogId;
  return !id.Contains("M14_UE4")&&!id.Contains("Mk14EBR_UE4")&&!id.Contains("svd")&&!id.Contains("SVD");
 }
 public static AnimationClip Create(AnimationClip firing,AnimationClip idle,string folder)
 {
  string sourceFolder=ArenaPolygonWeaponConversionBuilder.Folder("legacy-ak12");
  var recoil=AssetDatabase.LoadAssetAtPath<AnimationClip>(sourceFolder+"/AimFire_UE4.anim");
  var rest=AssetDatabase.LoadAssetAtPath<AnimationClip>(sourceFolder+"/Idle_UE4.anim");
  if(!recoil||!rest)throw new InvalidOperationException("Build Polygon AK recoil before sharing ADS motion.");
  var clip=Object.Instantiate(firing);clip.name="Gameplay_SharedAimFire_UE4";
  var bindings=AnimationUtility.GetCurveBindings(idle);
  string gun=bindings.Select(b=>b.path).First(p=>p.EndsWith("/SourceWeapon",StringComparison.Ordinal));
  bool Held(string p)=>(p==Carrier+"/SourceArms"||p.StartsWith(Carrier+"/SourceArms/",StringComparison.Ordinal))&&!p.StartsWith(gun+"/",StringComparison.Ordinal)||p==gun;
  // Keep the recipient's hands and weapon seated together; retain its animated internal parts.
  foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(b=>Held(b.path)))AnimationUtility.SetEditorCurve(clip,b,null);
  foreach(var b in bindings.Where(b=>Held(b.path)))AnimationUtility.SetEditorCurve(clip,b,AnimationCurve.Constant(0,recoil.length,AnimationUtility.GetEditorCurve(idle,b).Evaluate(0)));
  float Read(AnimationClip c,string p,string property,float t,float fallback)=>AnimationUtility.GetEditorCurve(c,EditorCurveBinding.FloatCurve(p,typeof(Transform),property))?.Evaluate(t)??fallback;
  Vector3 P(AnimationClip c,string p,float t)=>new(Read(c,p,"m_LocalPosition.x",t,0),Read(c,p,"m_LocalPosition.y",t,0),Read(c,p,"m_LocalPosition.z",t,0));
  Quaternion Q(AnimationClip c,string p,float t)=>new Quaternion(Read(c,p,"m_LocalRotation.x",t,0),Read(c,p,"m_LocalRotation.y",t,0),Read(c,p,"m_LocalRotation.z",t,0),Read(c,p,"m_LocalRotation.w",t,1)).normalized;
  var frame=Matrix4x4.identity;string path=Carrier;
  foreach(string node in gun.Substring(Carrier.Length+1).Split('/')){path+="/"+node;var scale=new Vector3(Read(idle,path,"m_LocalScale.x",0,1),Read(idle,path,"m_LocalScale.y",0,1),Read(idle,path,"m_LocalScale.z",0,1));frame*=Matrix4x4.TRS(P(idle,path,0),Q(idle,path,0),scale);}
  var pivot=frame.MultiplyPoint3x4(Vector3.zero);
  var curves=Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray();
  int frames=Mathf.CeilToInt(recoil.length*60);
  for(int f=0;f<=frames;f++){
   float t=recoil.length*f/frames;var delta=P(recoil,"SourceWeapon",t)-P(rest,"SourceWeapon",0);var turn=Q(recoil,"SourceWeapon",t)*Quaternion.Inverse(Q(rest,"SourceWeapon",0));var position=pivot+delta-turn*pivot;
   for(int a=0;a<7;a++)curves[a].AddKey(t,a<3?position[a]:turn[a-3]);
  }
  for(int a=0;a<7;a++){for(int k=0;k<curves[a].length;k++){AnimationUtility.SetKeyLeftTangentMode(curves[a],k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curves[a],k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(Carrier,typeof(Transform),(a<3?"m_LocalPosition.":"m_LocalRotation.")+"xyzw"[a<3?a:a-3]),curves[a]);}
  clip.EnsureQuaternionContinuity();string output=folder+"/Gameplay_SharedAimFire_UE4.anim";var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(output);if(saved){EditorUtility.CopySerialized(clip,saved);Object.DestroyImmediate(clip);EditorUtility.SetDirty(saved);}else{AssetDatabase.CreateAsset(clip,output);saved=clip;}return saved;
 }
}
