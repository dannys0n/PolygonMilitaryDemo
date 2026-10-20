using System;using System.Linq;using UnityEditor.Animations;using UnityEngine;

// Resolve the effective motion behind an existing weapon's state, including nested overrides.
public static class ArenaWeaponAnimationSource
{
 public static AnimatorController Base(RuntimeAnimatorController controller)
 {while(controller is AnimatorOverrideController o)controller=o.runtimeAnimatorController;return (AnimatorController)controller;}
 public static AnimationClip Resolve(RuntimeAnimatorController controller,AnimationClip clip)
 {
  if(controller is not AnimatorOverrideController o)return clip;
  var replacement=o[clip];return replacement&&replacement!=clip?replacement:Resolve(o.runtimeAnimatorController,clip);
 }
 public static AnimationClip Body(RuntimeAnimatorController controller,string state)
  =>state=="Idle"?State(controller,"SourceSway","Sway"):State(controller,"Reload","Reload");
 public static AnimationClip State(RuntimeAnimatorController controller,string layer,string state,int child=0)
 {
  if(!controller)return null;var l=Base(controller).layers.FirstOrDefault(l=>l.name==layer);if(l==null)return null;
  var s=l.stateMachine.states.FirstOrDefault(s=>s.state.name==state).state;if(!s)return null;var motion=s.motion;
  while(motion is BlendTree tree){if(tree.children.Length==0)return null;motion=tree.children[Mathf.Min(child,tree.children.Length-1)].motion;child=0;}
  return motion is AnimationClip clip?Resolve(controller,clip):null;
 }
}
