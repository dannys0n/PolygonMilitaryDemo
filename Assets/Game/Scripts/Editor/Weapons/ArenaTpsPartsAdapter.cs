using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

// Extract evaluated local mechanism poses, never raw Euler/quaternion component curves.
// Body/world motion stays on the body weapon layer. Each part clip also supplies
// the idle values of parts animated by another action, so actions are self-contained.
public static class ArenaTpsPartsAdapter
{
    sealed class PoseTrack
    {
        public string path;
        public Transform source;
        public readonly List<Vector3> positions=new(),scales=new();
        public readonly List<Quaternion> rotations=new();
        public void Sample()
        {
            positions.Add(source.localPosition);scales.Add(source.localScale);
            var q=source.localRotation.normalized;
            if(rotations.Count>0&&Quaternion.Dot(rotations[rotations.Count-1],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
            rotations.Add(q);
        }
    }
    static T Save<T>(T value,string path)where T:Object
    {
        value.name=Path.GetFileNameWithoutExtension(path);var existing=AssetDatabase.LoadAssetAtPath<T>(path);
        if(existing){EditorUtility.CopySerialized(value,existing);Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;}
        AssetDatabase.CreateAsset(value,path);return value;
    }
    static int[] Reduce(int count,Func<int,int,int,float> error,float tolerance)
    {
        if(count<3)return Enumerable.Range(0,count).ToArray();var keep=new SortedSet<int>{0,count-1};
        void Split(int start,int end){int worst=-1;float maximum=tolerance;for(int i=start+1;i<end;i++){float e=error(start,end,i);if(e>maximum){maximum=e;worst=i;}}if(worst<0)return;keep.Add(worst);Split(start,worst);Split(worst,end);}
        Split(0,count-1);return keep.ToArray();
    }
    static AnimationCurve Curve(float[] times,int[] kept,Func<int,float> value)
    {
        var curve=new AnimationCurve(kept.Select(i=>new Keyframe(times[i],value(i))).ToArray());
        for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}return curve;
    }
    static void Write(AnimationClip clip,PoseTrack track,float[] times)
    {
        var q=track.rotations;var p=track.positions;var s=track.scales;
        int[] rotations=Reduce(q.Count,(a,b,i)=>Quaternion.Angle(Quaternion.LerpUnclamped(q[a],q[b],(times[i]-times[a])/(times[b]-times[a])),q[i]),.05f);
        int[] positions=Reduce(p.Count,(a,b,i)=>Vector3.Distance(Vector3.LerpUnclamped(p[a],p[b],(times[i]-times[a])/(times[b]-times[a])),p[i]),.00005f);
        int[] scales=Reduce(s.Count,(a,b,i)=>Vector3.Distance(Vector3.LerpUnclamped(s[a],s[b],(times[i]-times[a])/(times[b]-times[a])),s[i]),.00005f);
        for(int axis=0;axis<3;axis++)
        {
            int component=axis;
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(track.path,typeof(Transform),"m_LocalPosition."+"xyz"[axis]),Curve(times,positions,i=>p[i][component]));
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(track.path,typeof(Transform),"m_LocalScale."+"xyz"[axis]),Curve(times,scales,i=>s[i][component]));
        }
        for(int axis=0;axis<4;axis++){int component=axis;AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(track.path,typeof(Transform),"m_LocalRotation."+"xyzw"[axis]),Curve(times,rotations,i=>q[i][component]));}
    }
    const string SharedFolder="Assets/Game/Animations/Weapons/ScaledTPS";
    static AnimatorController SharedController()
    {
        string path=SharedFolder+"/Parts.controller";var existing=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);if(existing)return existing;
        var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Fire",AnimatorControllerParameterType.Trigger);controller.AddParameter("Reload",AnimatorControllerParameterType.Trigger);
        var machine=controller.layers[0].stateMachine;var states=new Dictionary<string,AnimatorState>();
        foreach(string action in new[]{"Idle","Reload","Fire"})
        {
            var key=new AnimationClip{name="Parts"+action,frameRate=60};AnimationUtility.SetEditorCurve(key,EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition.x"),AnimationCurve.Constant(0,.1f,0));
            key=Save(key,SharedFolder+"/Parts"+action+"Key.anim");var state=machine.AddState(action);state.motion=key;state.writeDefaultValues=false;states[action]=state;
        }
        machine.defaultState=states["Idle"];
        foreach(string action in new[]{"Reload","Fire"})
        {
            var enter=machine.AddAnyStateTransition(states[action]);enter.hasExitTime=false;enter.duration=.04f;enter.hasFixedDuration=true;enter.canTransitionToSelf=action=="Fire";enter.AddCondition(AnimatorConditionMode.If,0,action);
            var exit=states[action].AddTransition(states["Idle"]);exit.hasExitTime=true;exit.exitTime=1;exit.duration=.04f;exit.hasFixedDuration=true;
        }
        EditorUtility.SetDirty(controller);return controller;
    }
    static bool Map(EditorCurveBinding input,string prefix,Transform targetRoot,out EditorCurveBinding output)
    {
        output=input;if(!input.path.StartsWith(prefix,StringComparison.Ordinal))return false;
        string relative=input.path.Substring(prefix.Length);var target=targetRoot.Find(relative)?targetRoot.Find(relative):targetRoot.Find("SourceWeapon/"+relative);if(!target)return false;
        output.path=AnimationUtility.CalculateTransformPath(target,targetRoot);return true;
    }
    public static int Build(WeaponAnimationConversionProof proof,string folder)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var entry=proof.converted;var view=entry.firstPersonPrefab;var root=Object.Instantiate(view.sharedFirstPersonRig);
        try
        {
            var rig=root.GetComponent<SoldierSharedFirstPersonRig>();var mount=string.IsNullOrEmpty(view.sharedWeaponMountPath)?rig.weaponMount:root.transform.Find(view.sharedWeaponMountPath);
            var gun=Object.Instantiate(view.gameObject,mount,false);gun.name="SourceWeapon";gun.transform.SetLocalPositionAndRotation(view.mountPosition,view.mountRotation);gun.transform.localScale=view.mountScale;
            var targetRoot=entry.thirdPersonPrefab.animator.transform;string prefix=AnimationUtility.CalculateTransformPath(gun.transform,rig.animator.transform)+"/";
            var actions=new[]{("Idle",proof.idle),("Reload",proof.reload),("Fire",proof.fire)}.Where(a=>a.Item2).ToArray();
            var sourcePaths=actions.SelectMany(a=>AnimationUtility.GetCurveBindings(a.Item2)).Where(b=>b.type==typeof(Transform)&&b.path.StartsWith(prefix,StringComparison.Ordinal)).Select(b=>b.path).Distinct().ToArray();
            var paths=sourcePaths.Select(path=>{string relative=path.Substring(prefix.Length);var target=targetRoot.Find(relative)?targetRoot.Find(relative):targetRoot.Find("SourceWeapon/"+relative);return(source:root.transform.Find(path),target:target);}).Where(p=>p.source&&p.target).ToArray();
            if(paths.Length==0)return 0;
            var controller=SharedController();
            if(!controller)throw new InvalidOperationException("Missing TPS part controller: "+entry.weapon.catalogId);
            proof.idle.SampleAnimation(root,0);
            var idlePoses=paths.Select(p=>(p.source,position:p.source.localPosition,rotation:p.source.localRotation,scale:p.source.localScale)).ToArray();
            var floatDefaults=new Dictionary<EditorCurveBinding,float>();
            foreach(var binding in actions.SelectMany(a=>AnimationUtility.GetCurveBindings(a.Item2)).Where(b=>b.type!=typeof(Transform)).Distinct())
            {if(!Map(binding,prefix,targetRoot,out _))continue;var idle=AnimationUtility.GetEditorCurve(proof.idle,binding);if(idle!=null)floatDefaults[binding]=idle.Evaluate(0);else if(AnimationUtility.GetFloatValue(root,binding,out float initial))floatDefaults[binding]=initial;}
            var objectDefaults=new Dictionary<EditorCurveBinding,Object>();
            foreach(var binding in actions.SelectMany(a=>AnimationUtility.GetObjectReferenceCurveBindings(a.Item2)).Distinct())
            {if(!Map(binding,prefix,targetRoot,out _))continue;var idle=AnimationUtility.GetObjectReferenceCurve(proof.idle,binding);if(idle!=null&&idle.Length>0)objectDefaults[binding]=idle[0].value;else if(AnimationUtility.GetObjectReferenceValue(root,binding,out Object initial))objectDefaults[binding]=initial;}
            var replacements=new Dictionary<AnimationClip,AnimationClip>();
            foreach(var action in actions)
            {
                float duration=Mathf.Max(.01f,action.Item2.length);int frames=Mathf.Max(1,Mathf.RoundToInt(duration*240));
                // Preserve authored key boundaries as well as the regular sampling grid.
                // Fast magazine/bolt moves can begin between grid samples.
                var times=Enumerable.Range(0,frames+1).Select(i=>duration*i/frames)
                    .Concat(AnimationUtility.GetCurveBindings(action.Item2)
                        .Where(b=>b.type==typeof(Transform)&&b.path.StartsWith(prefix,StringComparison.Ordinal))
                        .SelectMany(b=>AnimationUtility.GetEditorCurve(action.Item2,b).keys.Select(k=>k.time)))
                    .Where(t=>t>=0&&t<=duration).Distinct().OrderBy(t=>t).ToArray();
                var tracks=paths.Select(p=>new PoseTrack{source=p.source,path=AnimationUtility.CalculateTransformPath(p.target,targetRoot)}).ToArray();
                foreach(float time in times)
                {
                    foreach(var pose in idlePoses){pose.source.SetLocalPositionAndRotation(pose.position,pose.rotation);pose.source.localScale=pose.scale;}
                    proof.idle.SampleAnimation(root,0);action.Item2.SampleAnimation(root,time);
                    foreach(var track in tracks)track.Sample();
                }
                // Sample curved source rotations densely before reducing with an angular-error bound.
                var result=new AnimationClip{frameRate=240};foreach(var track in tracks)Write(result,track,times);
                // Discrete visibility/reference channels must also reset on Idle/Fire,
                // including those only keyed by Reload (rockets, loose cartridges).
                foreach(var binding in actions.SelectMany(a=>AnimationUtility.GetCurveBindings(a.Item2)).Where(b=>b.type!=typeof(Transform)).Distinct())
                {
                    if(!Map(binding,prefix,targetRoot,out var mapped))continue;
                    var curve=AnimationUtility.GetEditorCurve(action.Item2,binding);
                    if(curve==null){if(!floatDefaults.TryGetValue(binding,out float initial))continue;curve=AnimationCurve.Constant(0,duration,initial);}
                    AnimationUtility.SetEditorCurve(result,mapped,curve);
                }
                foreach(var binding in actions.SelectMany(a=>AnimationUtility.GetObjectReferenceCurveBindings(a.Item2)).Distinct())
                {
                    if(!Map(binding,prefix,targetRoot,out var mapped))continue;
                    var keys=AnimationUtility.GetObjectReferenceCurve(action.Item2,binding);
                    if(keys==null||keys.Length==0){if(!objectDefaults.TryGetValue(binding,out Object initial))continue;keys=new[]{new ObjectReferenceKeyframe{time=0,value=initial},new ObjectReferenceKeyframe{time=duration,value=initial}};}
                    AnimationUtility.SetObjectReferenceCurve(result,mapped,keys);
                }
                // Quaternions already share key times and hemisphere; EnsureQuaternionContinuity would re-smooth sparse holds.
                var settings=AnimationUtility.GetAnimationClipSettings(result);settings.loopTime=action.Item1=="Idle";AnimationUtility.SetAnimationClipSettings(result,settings);
                result=Save(result,folder+"/"+action.Item1+"Parts.anim");var old=ArenaWeaponAnimationSource.State(controller,"Base Layer",action.Item1);if(old)replacements[old]=result;
            }
            var output=new AnimatorOverrideController(controller);var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();output.GetOverrides(pairs);
            for(int i=0;i<pairs.Count;i++){var key=pairs[i].Key;var effective=ArenaWeaponAnimationSource.Resolve(controller,key);pairs[i]=new KeyValuePair<AnimationClip,AnimationClip>(key,replacements.TryGetValue(effective,out var next)?next:effective);}
            output.ApplyOverrides(pairs);entry.weapon.thirdPersonParts=Save(output,folder+"/Parts.overrideController");EditorUtility.SetDirty(entry.weapon);AssetDatabase.SaveAssets();return paths.Length;
        }
        finally{Object.DestroyImmediate(root);}
    }
}
