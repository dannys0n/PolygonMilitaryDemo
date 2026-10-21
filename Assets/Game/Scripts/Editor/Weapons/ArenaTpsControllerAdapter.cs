using System;using System.Collections.Generic;using System.Linq;using UnityEditor;using UnityEditor.Animations;using UnityEngine;

// Converted idle clips contain absolute poses. Share one override-layer base,
// leaving the older additive source controller available to unconverted weapons.
public static class ArenaTpsControllerAdapter
{
 public static RuntimeAnimatorController Rebase(RuntimeAnimatorController input,string outputPath)
 {
  const string path="Assets/Game/Animations/Weapons/ScaledTPS/ConvertedBody.controller";
  var original=ArenaWeaponAnimationSource.Base(input);
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
  if(!controller){if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original),path))throw new InvalidOperationException("Cannot create the shared converted TPS controller.");controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);}
  ConfigureLayers(controller);
  var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();var result=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(outputPath);
  // Read effective mappings before replacing the existing controller's base.
  var values=original.animationClips.Distinct().ToDictionary(c=>c,c=>ArenaWeaponAnimationSource.Resolve(input,c));
  if(!result){result=new AnimatorOverrideController(controller);AssetDatabase.CreateAsset(result,outputPath);}else result.runtimeAnimatorController=controller;
  result.GetOverrides(pairs);for(int i=0;i<pairs.Count;i++){var key=pairs[i].Key;pairs[i]=new KeyValuePair<AnimationClip,AnimationClip>(key,values.TryGetValue(key,out var value)?value:key);}result.ApplyOverrides(pairs);EditorUtility.SetDirty(result);return result;
 }
 public static void ConfigureLayers(AnimatorController controller)
 {
  var layers=controller.layers.ToList();
  var hold=layers.First(l=>l.name=="SourceSway");
  hold.blendingMode=AnimatorLayerBlendingMode.Override;
  const string holdPath="Assets/Game/Animations/Weapons/ScaledTPS/HoldArms.mask";
  var holdMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(holdPath);
  if(!holdMask){holdMask=UnityEngine.Object.Instantiate(hold.avatarMask);holdMask.name="HoldArms";AssetDatabase.CreateAsset(holdMask,holdPath);}
  holdMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,true);holdMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers,true);
  hold.avatarMask=holdMask;EditorUtility.SetDirty(holdMask);
  // The imported walking clip has an unrelated gripping pose. Locomotion stays
  // on the body base; this layer owns the weapon-specific hands at every speed.
  foreach(var child in hold.stateMachine.states)
   if(child.state.motion is BlendTree tree)child.state.motion=tree.children.OrderBy(c=>c.threshold).First().motion;
  var fire=layers.First(l=>l.name=="Fire");
  if(fire.blendingMode==AnimatorLayerBlendingMode.Additive)
  {
   const string maskPath="Assets/Game/Animations/Weapons/ScaledTPS/RecoilArms.mask";
   var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
   if(!mask){mask=new AvatarMask{name="RecoilArms"};AssetDatabase.CreateAsset(mask,maskPath);}
   for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,i==(int)AvatarMaskBodyPart.LeftArm||i==(int)AvatarMaskBodyPart.RightArm);
   mask.transformCount=0;EditorUtility.SetDirty(mask);fire.avatarMask=mask;
  }
  var sprint=layers.First(l=>l.name=="SprintArms");
  const string sprintPath="Assets/Game/Animations/Weapons/ScaledTPS/SprintArms.mask";
  var sprintMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(sprintPath);
  if(!sprintMask){sprintMask=UnityEngine.Object.Instantiate(sprint.avatarMask);sprintMask.name="SprintArms";AssetDatabase.CreateAsset(sprintMask,sprintPath);}
  sprintMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,false);sprintMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers,false);
  for(int i=0;i<sprintMask.transformCount;i++)if(sprintMask.GetTransformPath(i).Contains("Finger_")||sprintMask.GetTransformPath(i).Contains("Thumb_"))sprintMask.SetTransformActive(i,false);
  sprint.avatarMask=sprintMask;EditorUtility.SetDirty(sprintMask);
  var reload=layers.First(l=>l.name=="Reload");reload.defaultWeight=0;
  const string reloadMaskPath="Assets/Game/Animations/Weapons/ScaledTPS/ReloadUpperBody.mask";
  var reloadMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(reloadMaskPath);
  if(!reloadMask){reloadMask=UnityEngine.Object.Instantiate(reload.avatarMask);reloadMask.name="ReloadUpperBody";AssetDatabase.CreateAsset(reloadMask,reloadMaskPath);}
  reloadMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body,true);
  reloadMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root,false);
  reloadMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg,false);
  reloadMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg,false);
  EditorUtility.SetDirty(reloadMask);reload.avatarMask=reloadMask;
  if(fire.blendingMode==AnimatorLayerBlendingMode.Override)fire.defaultWeight=0;
  // A null Humanoid motion can output zero finger muscles even with Write Defaults off.
  // Share the weapon's idle key so its override supplies an explicit resting grip.
  var emptyReload=reload.stateMachine.states.First(s=>s.state.name=="Empty").state;
  emptyReload.motion=hold.stateMachine.states.First().state.motion;EditorUtility.SetDirty(emptyReload);
  var hit=layers.First(l=>l.name=="HitReaction");hit.defaultWeight=0;
  const string hitMaskPath="Assets/Game/Animations/Weapons/ScaledTPS/HitReactionBody.mask";
  var hitMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(hitMaskPath);
  if(!hitMask){hitMask=UnityEngine.Object.Instantiate(hit.avatarMask);hitMask.name="HitReactionBody";AssetDatabase.CreateAsset(hitMask,hitMaskPath);}
  hitMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,false);hitMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers,false);
  for(int i=0;i<hitMask.transformCount;i++)if(hitMask.GetTransformPath(i).Contains("Finger_")||hitMask.GetTransformPath(i).Contains("Thumb_"))hitMask.SetTransformActive(i,false);
  hit.avatarMask=hitMask;EditorUtility.SetDirty(hitMask);
  layers.Remove(hold);layers.Remove(fire);layers.Remove(reload);
  int index=layers.FindIndex(l=>l.name=="UpperBody")+1;
  layers.Insert(index,hold);layers.Insert(index+1,fire);layers.Insert(index+2,reload);
  // Body control clips own only the shared weapon frame and grip targets.
  // The child weapon Animator owns its receiver, magazine and mechanisms.
  string motionPath=Enumerable.Range(0,layers.First(l=>l.name=="WeaponHold").avatarMask.transformCount)
   .Select(i=>layers.First(l=>l.name=="WeaponHold").avatarMask.GetTransformPath(i)).First(p=>p.EndsWith("/WeaponMotion"));
  AvatarMask Controls(bool grips)
  {
   string path="Assets/Game/Animations/Weapons/ScaledTPS/"+(grips?"WeaponHoldControls":"WeaponMotionControls")+".mask";
   var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
   if(!mask){mask=new AvatarMask{name=System.IO.Path.GetFileNameWithoutExtension(path)};AssetDatabase.CreateAsset(mask,path);}
   for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
   var ancestors=new List<string>{""};string parent="";
   foreach(string part in motionPath.Split('/')){parent=string.IsNullOrEmpty(parent)?part:parent+"/"+part;ancestors.Add(parent);}
   var paths=ancestors.Concat(new[]{motionPath+"/RMotionGrip",motionPath+"/SupportHandGrip",motionPath+"/"+SoldierWeaponLoadout.ThirdPersonWeaponName}).ToArray();
   mask.transformCount=paths.Length;
   for(int i=0;i<paths.Length;i++){mask.SetTransformPath(i,paths[i]);mask.SetTransformActive(i,paths[i]==motionPath||grips&&(paths[i].EndsWith("/RMotionGrip")||paths[i].EndsWith("/SupportHandGrip")));}
   EditorUtility.SetDirty(mask);return mask;
  }
  var holdControls=Controls(true);var motionControls=Controls(false);
  foreach(var layer in layers.Where(l=>l.name.StartsWith("Weapon")))
   layer.avatarMask=layer.name=="WeaponHold"||layer.name=="WeaponPose"||layer.name=="WeaponFire"&&layer.blendingMode==AnimatorLayerBlendingMode.Override?holdControls:motionControls;
  layers.First(l=>l.name=="WeaponSwitch").defaultWeight=0;
  // Animation Rigging needs defaults enabled to synchronize script-driven
  // hold positions and grip targets. Inactive Humanoid actions are gated by
  // layer weight in SoldierWeaponPresentation, rather than freezing the stream.
  foreach(var layer in layers)
   foreach(var state in layer.stateMachine.states){state.state.writeDefaultValues=true;EditorUtility.SetDirty(state.state);}
  controller.layers=layers.ToArray();EditorUtility.SetDirty(controller);
 }

}
