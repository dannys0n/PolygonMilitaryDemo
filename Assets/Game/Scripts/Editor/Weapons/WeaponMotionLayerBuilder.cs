using System;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.Animations;using UnityEngine;
// Shared state machine and clock; model-specific mechanisms run on its synchronized layer.
public static class WeaponMotionLayerBuilder
{
 public static void Speeds(AnimatorController controller)
 {
  foreach(string rate in new[]{"IdlePlaybackRate","FirePlaybackRate","ReloadPlaybackRate"})
   if(!controller.parameters.Any(p=>p.name==rate))controller.AddParameter(rate,AnimatorControllerParameterType.Float);
  var parameters=controller.parameters;foreach(var p in parameters)if(p.name.EndsWith("PlaybackRate"))p.defaultFloat=1;controller.parameters=parameters;
  void Configure(AnimatorStateMachine machine){foreach(var child in machine.states){var state=child.state;string rate=state.name=="Reload"?"ReloadPlaybackRate":state.name=="Fire"?"FirePlaybackRate":state.name=="Idle"||state.name=="Sway"?"IdlePlaybackRate":null;if(rate!=null){state.speedParameter=rate;state.speedParameterActive=true;EditorUtility.SetDirty(state);}}foreach(var child in machine.stateMachines)Configure(child.stateMachine);}
  foreach(var layer in controller.layers)if(layer.syncedLayerIndex<0)Configure(layer.stateMachine);EditorUtility.SetDirty(controller);
 }
 public static AnimatorController WithMechanisms(RuntimeAnimatorController source,string path)
 {
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
  if(!controller){if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(WeaponAnimationSource.Base(source)),path))throw new Exception("Could not create shared weapon graph");controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);}
  Speeds(controller);
  // Every action supplies an explicit rest for its mechanisms. Avoid writing
  // unrelated default arm channels from the synchronized part-only layer.
  foreach(var child in controller.layers[0].stateMachine.states){child.state.writeDefaultValues=false;EditorUtility.SetDirty(child.state);}
  if(!controller.layers.Any(l=>l.name=="Mechanisms")){
   var machine=new AnimatorStateMachine{name="Mechanisms"};AssetDatabase.AddObjectToAsset(machine,controller);
   var layer=new AnimatorControllerLayer{name="Mechanisms",defaultWeight=1,blendingMode=AnimatorLayerBlendingMode.Override,syncedLayerIndex=0,syncedLayerAffectsTiming=false,stateMachine=machine};controller.AddLayer(layer);
   int index=controller.layers.Length-1;
   foreach(var child in controller.layers[0].stateMachine.states){var clip=new AnimationClip{name="Mechanisms"+child.state.name};AssetDatabase.AddObjectToAsset(clip,controller);controller.SetStateEffectiveMotion(child.state,clip,index);}
  }
  EditorUtility.SetDirty(controller);return controller;
 }
 public static RuntimeAnimatorController Bind(RuntimeAnimatorController source,AnimatorController shared,Dictionary<AnimationClip,AnimationClip> replacements,Dictionary<string,AnimationClip> parts,string path)
 {
  var output=new AnimatorOverrideController(shared);var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();output.GetOverrides(pairs);
  for(int i=0;i<pairs.Count;i++){var key=pairs[i].Key;AnimationClip value;if(key.name.StartsWith("Mechanisms")){if(!parts.TryGetValue(key.name.Substring("Mechanisms".Length),out value))throw new Exception("Missing mechanisms "+key.name);}else{value=WeaponAnimationSource.Resolve(source,key);if(replacements.TryGetValue(value,out var next))value=next;}pairs[i]=new(key,value);}
  output.ApplyOverrides(pairs);return WeaponReloadBuilder.Save(output,path);
 }
}
