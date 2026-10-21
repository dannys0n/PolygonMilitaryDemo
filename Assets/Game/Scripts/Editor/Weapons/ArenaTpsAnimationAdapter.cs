using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Authoring-time retarget only. Gameplay keeps its humanoid body and existing layered controller.
public static class ArenaTpsAnimationAdapter
{
    const string Root = "Assets/Game/Animations/Weapons/ScaledTPS";
    static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    static T Save<T>(T value, string path) where T : Object
    {
        value.name=Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(value, existing); Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(value, path); return value;
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\','/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'), Path.GetFileName(path));
    }
    sealed class Curves
    {
        readonly Dictionary<EditorCurveBinding,List<Keyframe>> tracks = new();
        readonly Dictionary<string,Quaternion> rotations = new();
        static Keyframe[] Reduce(List<Keyframe> input,float tolerance)
        {
            if(input.Count<=2)return input.ToArray();
            var keep=new SortedSet<int>{0,input.Count-1};
            void Split(int start,int end)
            {
                int worst=-1;float error=tolerance;
                for(int i=start+1;i<end;i++)
                {
                    float t=(input[i].time-input[start].time)/(input[end].time-input[start].time);
                    float difference=Mathf.Abs(input[i].value-Mathf.LerpUnclamped(input[start].value,input[end].value,t));
                    if(difference>error){error=difference;worst=i;}
                }
                if(worst<0)return;keep.Add(worst);Split(start,worst);Split(worst,end);
            }
            Split(0,input.Count-1);return keep.Select(i=>input[i]).ToArray();
        }
        public void Add(EditorCurveBinding binding,float time,float value)
        {
            if (!float.IsFinite(value)) throw new InvalidOperationException("Invalid retargeted pose: " + binding.propertyName);
            if (!tracks.TryGetValue(binding,out var keys)) tracks[binding]=keys=new List<Keyframe>();
            keys.Add(new Keyframe(time,value));
        }
        public void Transform(Transform t,Transform root,float time)
        {
            string path=AnimationUtility.CalculateTransformPath(t,root);
            var rotation=t.localRotation;
            if(rotations.TryGetValue(path,out var prior)&&Quaternion.Dot(prior,rotation)<0)rotation=new Quaternion(-rotation.x,-rotation.y,-rotation.z,-rotation.w);
            rotations[path]=rotation;
            for(int i=0;i<7;i++) Add(EditorCurveBinding.FloatCurve(path,typeof(Transform),i<3?"m_LocalPosition."+"xyz"[i]:"m_LocalRotation."+"xyzw"[i-3]),time,i<3?t.localPosition[i]:rotation[i-3]);
        }
        public AnimationClip Finish(string name,bool loop,float duration)
        {
            var clip=new AnimationClip{name=name,frameRate=30};
            foreach(var track in tracks)
            {
                var keys=track.Value;
                // Constant channels need only endpoints. Keep moving channels sampled linearly.
                if(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<.00001f)) keys=new List<Keyframe>{new Keyframe(0,keys[0].value),new Keyframe(duration,keys[0].value)};
                var curve=new AnimationCurve(Reduce(keys,track.Key.propertyName.StartsWith("m_LocalPosition")?.0001f:.0004f));
                for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
                AnimationUtility.SetEditorCurve(clip,track.Key,curve);
            }
            clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);return clip;
        }
    }
    static RuntimeAnimatorController Override(RuntimeAnimatorController input, Dictionary<AnimationClip,AnimationClip> replacements,string path)
    {
        var original=input as AnimatorOverrideController;
        var controller=new AnimatorOverrideController(original?original.runtimeAnimatorController:input);
        var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();controller.GetOverrides(overrides);
        var old=new List<KeyValuePair<AnimationClip,AnimationClip>>();if(original)original.GetOverrides(old);
        for(int i=0;i<overrides.Count;i++)
        {
            var key=overrides[i].Key;var inherited=old.FirstOrDefault(p=>p.Key==key).Value;var effective=inherited?inherited:key;
            overrides[i]=new KeyValuePair<AnimationClip,AnimationClip>(key,replacements.TryGetValue(effective,out var next)?next:effective);
        }
        controller.ApplyOverrides(overrides);return Save(controller,path);
    }
    [MenuItem("Arena/Weapons/Retarget selected weapon motions to TPS")]
    static void Selected()
    {
        foreach(var weapon in Selection.objects.OfType<SoldierWeaponDefinition>())Debug.Log(Build(weapon.catalogId));
    }
    public static object Build(string id)
    {
        string path=ArenaPolygonWeaponConversionBuilder.Root+"/OriginalProportions/"+id+"-polygon-ue4/Conversion.asset";
        if(File.Exists(path))return BuildProof(path);
        var proof=AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{ArenaPolygonWeaponConversionBuilder.Root+"/OriginalProportions"}).Select(AssetDatabase.GUIDToAssetPath).First(p=>AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(p).converted.weapon.catalogId==id);
        return BuildProof(proof);
    }
    public static object BuildProof(string proofPath)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode only.");
        var proof=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(proofPath);
        if(!proof||!proof.originalProportions||!proof.idle||!proof.reload)throw new InvalidOperationException("A complete scaled FPS proof is required.");
        var entry=proof.converted;var definition=entry.weapon;string id=definition.catalogId;string folder=Root+"/"+id;Folder(folder);
        if(!proof.thirdPersonSourceController){proof.thirdPersonSourceController=definition.thirdPerson;proof.thirdPersonSourceParts=definition.thirdPersonParts?definition.thirdPersonParts:definition.firstPersonParts;EditorUtility.SetDirty(proof);}
        var baselineDefinition=Object.Instantiate(definition);baselineDefinition.thirdPerson=proof.thirdPersonSourceController;baselineDefinition.thirdPersonParts=proof.thirdPersonSourceParts;
        var baselineEntry=new ArenaWeaponCatalog.Entry{weapon=baselineDefinition,firstPersonPrefab=entry.firstPersonPrefab,thirdPersonPrefab=entry.thirdPersonPrefab};
        var view=entry.firstPersonPrefab;
        if(!view.sharedFirstPersonRig)throw new InvalidOperationException(id+" requires the shared FPS rig.");
        var source=Object.Instantiate(view.sharedFirstPersonRig);var rig=source.GetComponent<SoldierSharedFirstPersonRig>();
        var mount=string.IsNullOrEmpty(view.sharedWeaponMountPath)?rig.weaponMount:source.transform.Find(view.sharedWeaponMountPath);
        var gun=Object.Instantiate(view.gameObject,mount,false);gun.name="SourceWeapon";gun.transform.SetLocalPositionAndRotation(view.mountPosition,view.mountRotation);gun.transform.localScale=view.mountScale;
        using var target=new ArenaMk18PoseFit.Source(baselineEntry,true);
        try
        {
            var sourceHand=Find(source,"hand_r");var sourceLeft=Find(source,"hand_l");var sourceElbow=Find(source,"lowerarm_r");var sourceLeftElbow=Find(source,"lowerarm_l");
            var destinationHand=target.animator.GetBoneTransform(HumanBodyBones.RightHand);var destinationLeft=target.animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var oldIdle=ArenaWeaponAnimationSource.Body(baselineDefinition.thirdPerson,"Idle");var oldReload=ArenaWeaponAnimationSource.Body(baselineDefinition.thirdPerson,"Reload");
            var oldHold=ArenaWeaponAnimationSource.State(baselineDefinition.thirdPerson,"WeaponHold","Idle");var oldWeaponReload=ArenaWeaponAnimationSource.State(baselineDefinition.thirdPerson,"WeaponPose","Reload");
            target.Begin(oldIdle);target.Sample(0);proof.idle.SampleAnimation(source,0);
            var baseline=new HumanPose();using var handler=new HumanPoseHandler(target.animator.avatar,target.animator.transform);handler.GetHumanPose(ref baseline);
            var sourceRightIdle=new Pose(sourceHand.position,sourceHand.rotation);var sourceLeftIdle=new Pose(sourceLeft.position,sourceLeft.rotation);
            var destinationRightIdle=new Pose(destinationHand.position,destinationHand.rotation);var destinationLeftIdle=new Pose(destinationLeft.position,destinationLeft.rotation);
            var sourceElbows=new[]{sourceLeftElbow.position,sourceElbow.position};
            var targetElbows=new[]{target.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position,target.animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position};
            var sourceGunIdle=new Pose(gun.transform.position,gun.transform.rotation);var targetGunIdle=new Pose(target.weapon.transform.position,target.weapon.transform.rotation);
            Quaternion SightFrame(SoldierWeaponView weapon)
            {
                var forward=weapon.frontSight.position-weapon.rearSight.position;
                var up=Vector3.ProjectOnPlane(Vector3.up,forward.normalized);
                if(up.sqrMagnitude<.01f)up=Vector3.ProjectOnPlane(weapon.rearSight.up,forward.normalized);
                return Quaternion.LookRotation(forward,up);
            }
            var alignment=SightFrame(target.weapon)*Quaternion.Inverse(SightFrame(gun.GetComponent<SoldierWeaponView>()));
            var relativeFrame=Quaternion.Inverse(targetGunIdle.rotation)*alignment*sourceGunIdle.rotation;
            float sourceLength=Vector3.Distance(Find(source,"upperarm_r").position,sourceElbow.position)+Vector3.Distance(sourceElbow.position,sourceHand.position);
            float targetLength=Vector3.Distance(target.animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position,targetElbows[1])+Vector3.Distance(targetElbows[1],destinationHand.position);
            float scale=targetLength/sourceLength;
            var rightCalibration=Quaternion.Inverse(targetGunIdle.rotation)*(destinationRightIdle.position-targetGunIdle.position)-relativeFrame*Quaternion.Inverse(sourceGunIdle.rotation)*(sourceRightIdle.position-sourceGunIdle.position)*scale;
            var leftCalibration=Quaternion.Inverse(targetGunIdle.rotation)*(destinationLeftIdle.position-targetGunIdle.position)-relativeFrame*Quaternion.Inverse(sourceGunIdle.rotation)*(sourceLeftIdle.position-sourceGunIdle.position)*scale;
            var sourceSkin=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.bones.Any(b=>b.name=="hand_r"));
            var targetSkin=target.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.bones.Any(b=>b.name=="Hand_R"));
            Quaternion Bind(SkinnedMeshRenderer skin,string name)
            {
                int i=Array.FindIndex(skin.bones,b=>b.name==name);
                if(i<0)throw new InvalidOperationException("Missing bind bone "+name);
                return(skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[i].inverse).rotation;
            }
            var fingers=new List<(Transform source,Transform target,Transform sourceHand,Transform targetHand,Quaternion handBasis,Quaternion basis)>();
            foreach(string side in new[]{"l","r"})foreach(var pair in new[]{("thumb","Thumb"),("index","IndexFinger"),("ring","Finger")})for(int j=1;j<=3;j++)
            {
                string sourceName=pair.Item1+"_0"+j+"_"+side,targetName=pair.Item2+"_0"+j+(side=="r"?"1":"");
                var s=Find(source,sourceName);var t=Find(target.animator.gameObject,targetName);
                var handBasis=Quaternion.Inverse(Bind(sourceSkin,"hand_"+side))*Bind(targetSkin,"Hand_"+(side=="r"?"R":"L"));
                var basis=Quaternion.Inverse(Bind(sourceSkin,sourceName))*Bind(targetSkin,targetName);
                fingers.Add((s,t,side=="r"?sourceHand:sourceLeft,side=="r"?destinationHand:destinationLeft,handBasis,basis));
            }
            var presentation=target.player.GetComponent<SoldierWeaponPresentation>();var root=target.animator.transform;var weaponRoot=presentation.weaponRoot;
            var weaponLocalPosition=target.weapon.transform.localPosition;var weaponLocalRotation=target.weapon.transform.localRotation;
            var muscleIndices=Enumerable.Range(0,HumanTrait.MuscleCount).Where(i=>{int b=HumanTrait.BoneFromMuscle(i);return b==(int)HumanBodyBones.Spine||b==(int)HumanBodyBones.Chest||b==(int)HumanBodyBones.UpperChest||b==(int)HumanBodyBones.Neck||b==(int)HumanBodyBones.Head||b==(int)HumanBodyBones.LeftShoulder||b==(int)HumanBodyBones.RightShoulder||b==(int)HumanBodyBones.LeftUpperArm||b==(int)HumanBodyBones.RightUpperArm||b==(int)HumanBodyBones.LeftLowerArm||b==(int)HumanBodyBones.RightLowerArm||b==(int)HumanBodyBones.LeftHand||b==(int)HumanBodyBones.RightHand;}).ToArray();
            var reloadBlend=AnimationUtility.GetEditorCurve(oldReload,EditorCurveBinding.FloatCurve("",typeof(Animator),"WeaponReloadWeight"));
            var sampledPose=new HumanPose();var output=new List<AnimationClip>();var weaponOutput=new List<AnimationClip>();float maxReach=0;
            target.Begin(oldReload);
            foreach(bool reload in new[]{false,true})
            {
                var input=reload?proof.reload:proof.idle;float duration=Mathf.Max(.1f,input.length);int frames=reload?Mathf.CeilToInt(duration*30):1;var bodyTracks=new Curves();var weaponTracks=new Curves();
                for(int f=0;f<=frames;f++)
                {
                    float time=duration*f/frames;input.SampleAnimation(source,time);target.Sample(reload?time*oldReload.length/duration:0);handler.GetHumanPose(ref baseline);handler.SetHumanPose(ref baseline);
                    var gunDelta=alignment*gun.transform.rotation*Quaternion.Inverse(sourceGunIdle.rotation)*Quaternion.Inverse(alignment);
                    var desiredRotation=gunDelta*targetGunIdle.rotation;
                    var desiredPosition=targetGunIdle.position+alignment*(gun.transform.position-sourceGunIdle.position)*scale;
                    // Translate the whole authored action into the body's reachable space,
                    // rather than stretching bones or adding a runtime correction solver.
                    for(int pass=0;pass<8;pass++)for(int side=0;side<2;side++)
                    {
                        bool left=side==0;var wrist=left?sourceLeft:sourceHand;
                        var goal=desiredPosition+desiredRotation*(relativeFrame*Quaternion.Inverse(gun.transform.rotation)*(wrist.position-gun.transform.position)*scale+(left?leftCalibration:rightCalibration));
                        var arm=target.animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                        var elbow=target.animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);var hand=left?destinationLeft:destinationHand;
                        float reach=Vector3.Distance(arm.position,elbow.position)+Vector3.Distance(elbow.position,hand.position)-.002f;
                        var delta=goal-arm.position;if(delta.magnitude>reach)desiredPosition-=delta.normalized*(delta.magnitude-reach);
                    }
                    for(int side=1;side>=0;side--)
                    {
                        bool left=side==0;var sourceWrist=left?sourceLeft:sourceHand;var start=left?sourceLeftIdle:sourceRightIdle;var finish=left?destinationLeftIdle:destinationRightIdle;var elbow=left?sourceLeftElbow:sourceElbow;
                        var goal=desiredPosition+desiredRotation*(relativeFrame*Quaternion.Inverse(gun.transform.rotation)*(sourceWrist.position-gun.transform.position)*scale+(left?leftCalibration:rightCalibration));
                        var pole=targetElbows[side]+alignment*(elbow.position-sourceElbows[side])*scale;
                        var delta=alignment*sourceWrist.rotation*Quaternion.Inverse(start.rotation)*Quaternion.Inverse(alignment);
                        var arm=target.animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);var mid=target.animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);var hand=left?destinationLeft:destinationHand;
                        maxReach=Mathf.Max(maxReach,Solve(arm,mid,hand,goal,pole));hand.rotation=delta*finish.rotation;

                    }
                    foreach(var finger in fingers)finger.target.rotation=finger.targetHand.rotation*Quaternion.Inverse(finger.handBasis)*Quaternion.Inverse(finger.sourceHand.rotation)*finger.source.rotation*finger.basis;
                    handler.GetHumanPose(ref sampledPose);
                    float weight=reload?Mathf.Clamp01(reloadBlend?.Evaluate(time*oldReload.length/duration)??Mathf.Min(Mathf.Clamp01(time/.1f),Mathf.Clamp01((duration-time)/.15f))):0;


                    // Match the root frame to the authored equipped prefab's local placement.
                    var rootRotation=desiredRotation*Quaternion.Inverse(weaponLocalRotation);
                    var rootPosition=desiredPosition-rootRotation*Vector3.Scale(weaponRoot.lossyScale,weaponLocalPosition);
                    weaponRoot.SetPositionAndRotation(rootPosition,rootRotation);
                    foreach(int index in muscleIndices)bodyTracks.Add(EditorCurveBinding.FloatCurve("",typeof(Animator),HumanTrait.MuscleName[index]),time,sampledPose.muscles[index]);
                    foreach(var finger in fingers)bodyTracks.Transform(finger.target,root,time);weaponTracks.Transform(weaponRoot,root,time);
                    bodyTracks.Add(EditorCurveBinding.FloatCurve("",typeof(Animator),"WeaponReloadWeight"),time,weight);
                }
                output.Add(Save(bodyTracks.Finish(reload?"Reload":"Idle",!reload,duration),folder+"/"+(reload?"Reload":"Idle")+".anim"));
                weaponOutput.Add(Save(weaponTracks.Finish(reload?"ReloadWeapon":"HoldWeapon",!reload,duration),folder+"/"+(reload?"ReloadWeapon":"HoldWeapon")+".anim"));
            }
            var replacements=new Dictionary<AnimationClip,AnimationClip>();replacements[oldIdle]=output[0];replacements[oldReload]=output[1];replacements[oldHold]=weaponOutput[0];replacements[oldWeaponReload]=weaponOutput[1];
            definition.thirdPerson=Override(baselineDefinition.thirdPerson,replacements,folder+"/Body.overrideController");
            var partCurves=new Curves();int partTracks=0;string prefix=AnimationUtility.CalculateTransformPath(gun.transform,rig.animator.transform)+"/";
            foreach(var binding in AnimationUtility.GetCurveBindings(proof.reload).Where(b=>b.path.StartsWith(prefix)))
            {
                string path=binding.path.Substring(prefix.Length);
                var partsRoot=entry.thirdPersonPrefab.animator.transform;
                var node=partsRoot.Find(path)?partsRoot.Find(path):partsRoot.Find("SourceWeapon/"+path);
                if(!node)continue;
                path=AnimationUtility.CalculateTransformPath(node,partsRoot);
                if(path.Contains("Sight")||path.Contains("Socket")||path.Contains("Optic"))continue;
                var b=binding;b.path=path;var curve=AnimationUtility.GetEditorCurve(proof.reload,binding);
                for(int frame=0,frames=Mathf.CeilToInt(proof.reload.length*30);frame<=frames;frame++){float t=proof.reload.length*frame/frames;partCurves.Add(b,t,curve.Evaluate(t));}partTracks++;
            }
            if(partTracks>0){var parts=Save(partCurves.Finish("ReloadParts",false,proof.reload.length),folder+"/ReloadParts.anim");var oldParts=ArenaWeaponAnimationSource.State(proof.thirdPersonSourceParts,"Base Layer","Reload");definition.thirdPersonParts=Override(proof.thirdPersonSourceParts,new Dictionary<AnimationClip,AnimationClip>{{oldParts,parts}},folder+"/Parts.overrideController");}
            definition.name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(definition));EditorUtility.SetDirty(definition);AssetDatabase.SaveAssets();return new{id,humanMotion=output.All(c=>c.humanMotion),parts=partTracks,reachClamped=maxReach};
        }
        finally{Object.DestroyImmediate(source);Object.DestroyImmediate(baselineDefinition);}
    }
    static float Solve(Transform arm,Transform mid,Transform hand,Vector3 goal,Vector3 pole)
    {
        var start=arm.position;float a=Vector3.Distance(start,mid.position),b=Vector3.Distance(mid.position,hand.position);var axis=goal-start;
        float d=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.001f);axis.Normalize();var bend=Vector3.ProjectOnPlane(pole-start,axis).normalized;if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(Vector3.down,axis).normalized;
        float along=(a*a-b*b+d*d)/(2*d);var elbow=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));arm.rotation=Quaternion.FromToRotation(mid.position-start,elbow-start)*arm.rotation;mid.rotation=Quaternion.FromToRotation(hand.position-mid.position,start+axis*d-mid.position)*mid.rotation;return Vector3.Distance(hand.position,goal);
    }
}
