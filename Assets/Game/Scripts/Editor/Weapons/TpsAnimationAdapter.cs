using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Authoring-time retarget only. Gameplay keeps its humanoid body and existing layered controller.
public static class TpsAnimationAdapter
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
            var bindings=new List<EditorCurveBinding>(tracks.Count);var curves=new List<AnimationCurve>(tracks.Count);
            foreach(var track in tracks)
            {
                var keys=track.Value;
                // Constant channels need only endpoints. Keep moving channels sampled linearly.
                if(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<.00001f)) keys=new List<Keyframe>{new Keyframe(0,keys[0].value),new Keyframe(duration,keys[0].value)};
                var curve=new AnimationCurve(Reduce(keys,track.Key.propertyName.StartsWith("m_LocalPosition")?.0001f:.0004f));
                for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
                bindings.Add(track.Key);curves.Add(curve);
            }
            // Apply together: setting each channel separately repeatedly rebuilds
            // the native Humanoid binding data during large authoring batches.
            AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());
            // Transform() already selects a continuous quaternion hemisphere.
            // EnsureQuaternionContinuity re-smooths tangents and overshoots held poses.
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);return clip;
        }
    }
    // Match the accepted FPS hand surfaces, rather than treating the old TPS
    // placeholder wrist rotation as a grip calibration. All fitting is authoring-only.
    sealed class HandSurfaceFit
    {
        readonly SkinnedMeshRenderer source, target;
        readonly Transform[] sourceBones,targetBones;
        readonly string[] sourceNames,targetNames;
        readonly Vector3[] sourceVertices, targetVertices;
        readonly BoneWeight[] sourceWeights, targetWeights;
        readonly Matrix4x4[] sourceBind, targetBind;
        readonly List<(int source,int target,string side)> points=new();
        readonly Transform[] fingers;
        readonly Transform left,right;
        readonly Dictionary<string,Quaternion> meshRotations=new();
        public Quaternion MeshRotation(string bone)=>meshRotations.TryGetValue(bone,out var rotation)?rotation:throw new InvalidOperationException("Missing hand mesh conversion "+bone);
        public float Rms {get;private set;}
        static Vector3 Skin(int index,Vector3[] vertices,BoneWeight[] weights,Matrix4x4[] bind,Transform[] bones)
        {
            var w=weights[index];var v=vertices[index];
            return (bones[w.boneIndex0].localToWorldMatrix*bind[w.boneIndex0]).MultiplyPoint3x4(v)*w.weight0
                +(bones[w.boneIndex1].localToWorldMatrix*bind[w.boneIndex1]).MultiplyPoint3x4(v)*w.weight1
                +(bones[w.boneIndex2].localToWorldMatrix*bind[w.boneIndex2]).MultiplyPoint3x4(v)*w.weight2
                +(bones[w.boneIndex3].localToWorldMatrix*bind[w.boneIndex3]).MultiplyPoint3x4(v)*w.weight3;
        }
        public HandSurfaceFit(SkinnedMeshRenderer input,SkinnedMeshRenderer output,Transform[] targetFingers,Transform leftHand,Transform rightHand)
        {
            source=input;target=output;fingers=targetFingers;left=leftHand;right=rightHand;
            sourceBones=source.bones;targetBones=target.bones;sourceNames=sourceBones.Select(b=>b.name).ToArray();targetNames=targetBones.Select(b=>b.name).ToArray();
            sourceVertices=source.sharedMesh.vertices;targetVertices=target.sharedMesh.vertices;
            sourceWeights=source.sharedMesh.boneWeights;targetWeights=target.sharedMesh.boneWeights;
            sourceBind=source.sharedMesh.bindposes;targetBind=target.sharedMesh.bindposes;
            var sourceUv=source.sharedMesh.uv;var targetUv=target.sharedMesh.uv;
            string TargetName(string name,string side)
            {
                string suffix=side=="r"?"1":"";
                if(name.StartsWith("hand_"))return "Hand_"+(side=="r"?"R":"L");
                foreach(var pair in new[]{("thumb","Thumb"),("index","IndexFinger"),("ring","Finger")})
                    if(name.StartsWith(pair.Item1+"_"))return pair.Item2+"_"+name.Split('_')[1]+suffix;
                return null;
            }
            Matrix4x4 BindWorld(SkinnedMeshRenderer skin,Matrix4x4[] bind,string name)
            {
                int index=Array.IndexOf(skin==source?sourceNames:targetNames,name);if(index<0)throw new InvalidOperationException("Missing surface bind bone "+name);
                return skin.transform.localToWorldMatrix*bind[index].inverse;
            }
            bool IsRight(string name)=>name.EndsWith("_R")||((name.StartsWith("Thumb_")||name.StartsWith("IndexFinger_")||name.StartsWith("Finger_"))&&name.EndsWith("1")&&name.Length>name.LastIndexOf('_')+3);
            string LeftName(string name)
            {
                if(name.EndsWith("_R"))return name.Substring(0,name.Length-1)+"L";
                return IsRight(name)?name.Substring(0,name.Length-1):name;
            }
            string RightName(string name)=>name.EndsWith("_L")?name.Substring(0,name.Length-1)+"R":name+"1";
            string SourceName(string original)
            {
                bool right=IsRight(original);string name=LeftName(original),suffix=right?"_r":"_l";
                foreach(var pair in new[]{("Clavicle_L","clavicle"),("Shoulder_L","upperarm"),("Elbow_L","lowerarm"),("Hand_L","hand")})if(name==pair.Item1)return pair.Item2+suffix;
                foreach(var pair in new[]{("Thumb","thumb"),("IndexFinger","index"),("Finger","ring")})for(int j=1;j<=(pair.Item1=="Thumb"?3:4);j++)if(name==pair.Item1+"_0"+j)return pair.Item2+"_0"+Mathf.Min(j,3)+suffix;
                return "spine_03";
            }
            string Next(string original)
            {
                bool right=IsRight(original);string name=LeftName(original),next=null;
                if(name=="Clavicle_L")next="Shoulder_L";else if(name=="Shoulder_L")next="Elbow_L";else if(name=="Elbow_L")next="Hand_L";else if(name=="Hand_L")next="IndexFinger_01";
                foreach(string finger in new[]{"Thumb","IndexFinger","Finger"})for(int j=1;j<(finger=="Thumb"?3:4);j++)if(name==finger+"_0"+j)next=finger+"_0"+(j+1);
                return next!=null&&right?RightName(next):next;
            }
            Vector3 T(string name)=>BindWorld(target,targetBind,name).GetColumn(3);
            Vector3 S(string name)
            {
                var point=(Vector3)BindWorld(source,sourceBind,SourceName(name)).GetColumn(3);
                if(LeftName(name)=="Thumb_03"||LeftName(name)=="IndexFinger_04"||LeftName(name)=="Finger_04")
                {
                    string previous=SourceName(name).Replace("_03_","_02_");var parent=(Vector3)BindWorld(source,sourceBind,previous).GetColumn(3);point+=(point-parent)*.8f;
                }
                return point;
            }
            var oldNormal=Vector3.Cross(T("IndexFinger_01")-T("Hand_L"),T("Finger_01")-T("Hand_L")).normalized;
            var newNormal=Vector3.Cross(S("IndexFinger_01")-S("Hand_L"),S("Finger_01")-S("Hand_L")).normalized;
            var conversions=new Dictionary<int,Matrix4x4>();
            Matrix4x4 Conversion(int bone)
            {
                if(conversions.TryGetValue(bone,out var cached))return cached;
                string name=targetNames[bone],next=Next(name),leftName=LeftName(name);
                if(!(leftName=="Hand_L"||leftName=="Elbow_L"||leftName=="Shoulder_L"||leftName=="Clavicle_L"||leftName.StartsWith("Thumb_")||leftName.StartsWith("IndexFinger_")||leftName.StartsWith("Finger_")))return Matrix4x4.identity;
                bool right=IsRight(name);string hand=right?"Hand_R":"Hand_L",index=right?"IndexFinger_011":"IndexFinger_01",ring=right?"Finger_011":"Finger_01";
                var originalNormal=right?Vector3.Cross(T(index)-T(hand),T(ring)-T(hand)).normalized:oldNormal;
                var fittedNormal=right?Vector3.Cross(S(index)-S(hand),S(ring)-S(hand)).normalized:newNormal;
                string parent=targetBones[bone].parent.name;
                var oldAxis=next!=null?T(next)-T(name):T(name)-T(parent);
                var newAxis=next!=null?S(next)-S(name):S(name)-S(parent);
                var matrix=Matrix4x4.TRS(S(name),Quaternion.LookRotation(newAxis,fittedNormal),Vector3.one)*Matrix4x4.TRS(T(name),Quaternion.LookRotation(oldAxis,originalNormal),Vector3.one).inverse;
                meshRotations[name]=matrix.rotation;conversions[bone]=matrix;return matrix;
            }
            Vector3 Project(int index)
            {
                var w=targetWeights[index];var point=target.transform.TransformPoint(targetVertices[index]);
                return Conversion(w.boneIndex0).MultiplyPoint3x4(point)*w.weight0+Conversion(w.boneIndex1).MultiplyPoint3x4(point)*w.weight1+Conversion(w.boneIndex2).MultiplyPoint3x4(point)*w.weight2+Conversion(w.boneIndex3).MultiplyPoint3x4(point)*w.weight3;
            }
            // Bone bind axes alone do not describe the mesh's UE4 conversion.
            // Preserve the original skin's palm/finger frames as well; otherwise a
            // surface fitter has to rediscover large, often unrepresentable twists.
            for(int bone=0;bone<targetBones.Length;bone++)if(targetNames[bone].StartsWith("Hand_")||targetNames[bone].StartsWith("Thumb_")||targetNames[bone].StartsWith("IndexFinger_")||targetNames[bone].StartsWith("Finger_"))Conversion(bone);
            foreach(string side in new[]{"l","r"})
            {
                var groups=Enumerable.Range(0,sourceVertices.Length).Where(i=>sourceNames[sourceWeights[i].boneIndex0].EndsWith("_"+side))
                    .GroupBy(i=>sourceNames[sourceWeights[i].boneIndex0]).Where(g=>TargetName(g.Key,side)!=null);
                foreach(var group in groups)
                {
                    string boneName=TargetName(group.Key,side),leftName=TargetName(group.Key,"l");
                    var candidates=new List<(int source,int target)>();
                    foreach(int index in group)
                    {
                        // The source builder mirrors the left skin (including its UVs).
                        // Recover the original left surface vertex first, then mirror its
                        // position to identify the corresponding TPS right-hand vertex.
                        bool Tip(string name)=>group.Key.StartsWith("index_03")||group.Key.StartsWith("ring_03")?name==leftName||name==leftName.Replace("_03","_04"):name==leftName;
                        var matches=Enumerable.Range(0,targetVertices.Length).Where(i=>Tip(targetNames[targetWeights[i].boneIndex0])&&(targetUv[i]-sourceUv[index]).sqrMagnitude<1e-10f).ToArray();
                        if(matches.Length==0)continue;
                        // Palette-atlas UVs are shared by many vertices. Resolve the
                        // ambiguity with the same bind palm/finger frames used by the
                        // scaled FPS mesh builder, rather than picking the first UV hit.
                        var sourcePoint=sourceVertices[index];if(side=="r")sourcePoint.x=-sourcePoint.x;
                        sourcePoint=source.transform.TransformPoint(sourcePoint);
                        int match=matches.OrderBy(i=>(Project(i)-sourcePoint).sqrMagnitude).First();
                        if(side=="r")
                        {
                            string mirrorBone=targetNames[targetWeights[match].boneIndex0]+"1";if(mirrorBone=="Hand_L1")mirrorBone="Hand_R";
                            var mirrored=targetVertices[match];mirrored.x=-mirrored.x;
                            match=Enumerable.Range(0,targetVertices.Length).Where(i=>targetNames[targetWeights[i].boneIndex0]==mirrorBone)
                                .OrderBy(i=>(targetVertices[i]-mirrored).sqrMagnitude).First();
                        }
                        candidates.Add((index,match));
                    }
                    if(candidates.Count==0)throw new InvalidOperationException("No surface landmarks for "+group.Key);
                    var chosen=new List<(int source,int target)>{candidates[0]};
                    while(chosen.Count<Mathf.Min(6,candidates.Count))
                    {
                        var remaining=candidates.Where(p=>!chosen.Contains(p)).ToArray();if(remaining.Length==0)break;
                        var next=remaining.OrderByDescending(p=>chosen.Min(c=>(targetVertices[p.target]-targetVertices[c.target]).sqrMagnitude)).First();chosen.Add(next);
                    }
                    points.AddRange(chosen.Select(p=>(p.source,p.target,side)));
                }
            }
        }
        public void Fit(Func<Vector3,Vector3> goal)
        {
            var goals=points.Select(p=>goal(Skin(p.source,sourceVertices,sourceWeights,sourceBind,sourceBones))).ToArray();
            foreach(string side in new[]{"l","r"})
            {
                var hand=side=="l"?left:right;var start=hand.position;var indices=Enumerable.Range(0,points.Count).Where(i=>points[i].side==side).ToArray();
                var matrices=new Matrix4x4[targetBones.Length];
                Vector3 Point(int index)
                {
                    var w=targetWeights[index];var v=targetVertices[index];
                    return matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                }
                float Loss()
                {
                    for(int b=0;b<targetBones.Length;b++)matrices[b]=targetBones[b].localToWorldMatrix*targetBind[b];
                    float sum=0;foreach(int i in indices)sum+=(Point(points[i].target)-goals[i]).sqrMagnitude;return sum/indices.Length;
                }
                void Rotate(Transform bone,float angle,bool world)
                {
                    foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward})
                    {
                        var q=world?bone.rotation:bone.localRotation;var best=q;float error=Loss();
                        foreach(float sign in new[]{-1f,1f})
                        {
                            var candidate=q*Quaternion.AngleAxis(sign*angle,axis);if(world)bone.rotation=candidate;else bone.localRotation=candidate;
                            float value=Loss();if(value<error){error=value;best=candidate;}
                        }
                        if(world)bone.rotation=best;else bone.localRotation=best;
                    }
                }
                var joints=fingers.Where(t=>t.IsChildOf(hand)).ToArray();
                foreach(float step in new[]{24f,12f,6f,3f,1.5f,.75f,.35f})for(int pass=0;pass<2;pass++)
                {
                    Rotate(hand,step,true);
                    foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward})
                    {
                        var p=hand.position;var best=p;float error=Loss();
                        foreach(float sign in new[]{-1f,1f}){var candidate=p+axis*step*.0006f;if((candidate-start).sqrMagnitude>.0144f)continue;hand.position=candidate;float value=Loss();if(value<error){error=value;best=candidate;}}
                        hand.position=best;
                    }
                    foreach(var joint in joints)Rotate(joint,step,false);
                }
            }
            Rms=Mathf.Sqrt(points.Select((p,i)=>(Skin(p.target,targetVertices,targetWeights,targetBind,targetBones)-goals[i]).sqrMagnitude).Average());
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
    [MenuItem("Game/Weapons/Retarget selected weapon motions to TPS")]
    static void Selected()
    {
        foreach(var weapon in Selection.objects.OfType<SoldierWeaponDefinition>())Debug.Log(Build(weapon.catalogId));
    }
    public static object Build(string id)
    {
        string path=PolygonWeaponConversionBuilder.Root+"/OriginalProportions/"+id+"-polygon-ue4/Conversion.asset";
        if(File.Exists(path))return BuildProof(path);
        var proof=AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{PolygonWeaponConversionBuilder.Root+"/OriginalProportions"}).Select(AssetDatabase.GUIDToAssetPath).First(p=>AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(p).converted.weapon.catalogId==id);
        return BuildProof(proof);
    }
    public static object BuildProof(string proofPath, bool rebuildParts=true)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode only.");
        var proof=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(proofPath);
        if(!proof||!proof.originalProportions||!proof.idle||!proof.reload)throw new InvalidOperationException("A complete scaled FPS proof is required.");
        var entry=proof.converted;var definition=entry.weapon;string id=definition.catalogId;string folder=Root+"/"+id;Folder(folder);
        if(!proof.thirdPersonSourceController){proof.thirdPersonSourceController=definition.thirdPerson;proof.thirdPersonSourceParts=definition.thirdPersonParts?definition.thirdPersonParts:definition.firstPersonParts;EditorUtility.SetDirty(proof);}
        // Fit against the same mechanism rest pose that gameplay will evaluate.
        // Old TPS part clips can use a different receiver orientation than the FPS source.
        M249SocketRepair.Repair(proof);
        // Mechanism and body channels can be authored independently. A normal full build refreshes both.
        int partTracks=rebuildParts||!definition.thirdPersonParts?TpsPartsAdapter.Build(proof,folder):0;
        var baselineDefinition=Object.Instantiate(definition);baselineDefinition.thirdPersonAnimatedGrips=false;baselineDefinition.thirdPerson=proof.thirdPersonSourceController;baselineDefinition.thirdPersonParts=definition.thirdPersonParts;
        var baselineEntry=new WeaponCatalog.Entry{weapon=baselineDefinition,firstPersonPrefab=entry.firstPersonPrefab,thirdPersonPrefab=entry.thirdPersonPrefab};
        var view=entry.firstPersonPrefab;
        if(!view.sharedFirstPersonRig)throw new InvalidOperationException(id+" requires the shared FPS rig.");
        var source=Object.Instantiate(view.sharedFirstPersonRig);var rig=source.GetComponent<SoldierSharedFirstPersonRig>();
        var mount=string.IsNullOrEmpty(view.sharedWeaponMountPath)?rig.weaponMount:source.transform.Find(view.sharedWeaponMountPath);
        var gun=Object.Instantiate(view.gameObject,mount,false);gun.name="SourceWeapon";gun.transform.SetLocalPositionAndRotation(view.mountPosition,view.mountRotation);gun.transform.localScale=view.mountScale;
        using var target=new Mk18PoseFit.Source(baselineEntry,true);
        target.KeepAimFrame=true;
        target.IgnoreHandIK=true;
        try
        {
            var sourceHand=Find(source,"hand_r");var sourceLeft=Find(source,"hand_l");var sourceElbow=Find(source,"lowerarm_r");var sourceLeftElbow=Find(source,"lowerarm_l");
            var destinationHand=target.animator.GetBoneTransform(HumanBodyBones.RightHand);var destinationLeft=target.animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var oldIdle=WeaponAnimationSource.Body(baselineDefinition.thirdPerson,"Idle");var oldReload=WeaponAnimationSource.Body(baselineDefinition.thirdPerson,"Reload");
            var oldHold=WeaponAnimationSource.State(baselineDefinition.thirdPerson,"WeaponHold","Idle");var oldWeaponReload=WeaponAnimationSource.State(baselineDefinition.thirdPerson,"WeaponPose","Reload");
            target.Begin(oldIdle);target.Sample(0);proof.idle.SampleAnimation(source,0);
            var baseline=new HumanPose();using var handler=new HumanPoseHandler(target.animator.avatar,target.animator.transform);handler.GetHumanPose(ref baseline);
            var sourceRightIdle=new Pose(sourceHand.position,sourceHand.rotation);var sourceLeftIdle=new Pose(sourceLeft.position,sourceLeft.rotation);
            var destinationRightIdle=new Pose(destinationHand.position,destinationHand.rotation);var destinationLeftIdle=new Pose(destinationLeft.position,destinationLeft.rotation);
            var sourceElbows=new[]{sourceLeftElbow.position,sourceElbow.position};
            var targetElbows=new[]{target.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position,target.animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position};
            // Explicit physical registration replaces sight-derived axes and wrapper-origin assumptions.
            // The body pose is sampled without the previous weapon's fitted IK targets.
            WeaponGripFrame.Ensure(gun.GetComponent<SoldierWeaponView>(), target.weapon, Find(source,"index_01_r"));
            var sourceFrame=gun.GetComponent<SoldierWeaponView>().gripFrame;
            var targetFrame=target.weapon.gripFrame;
            var receiverRoot=target.player.GetComponent<SoldierWeaponPresentation>().weaponRoot;
            var receiverCorrection=Quaternion.Inverse(targetFrame.rotation);
            receiverRoot.rotation=receiverCorrection*receiverRoot.rotation;
            var alignment=targetFrame.rotation*Quaternion.Inverse(sourceFrame.rotation);
            float scale=targetFrame.lossyScale.magnitude/sourceFrame.lossyScale.magnitude;
            var sourceOrigin=sourceFrame.position;
            var targetOrigin=targetFrame.position;
            Vector3 MapPoint(Vector3 point)=>targetOrigin+alignment*(point-sourceOrigin)*scale;
            var receiver=WeaponGripFrame.Receiver(gun.GetComponent<SoldierWeaponView>());
            var sourceReceiverIdle=receiver();
            var targetReceiver=WeaponGripFrame.Receiver(target.weapon);
            Vector3 rightCalibration=Vector3.zero,leftCalibration=Vector3.zero;
            var sourceSkin=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.bones.Any(b=>b.name=="hand_r"));
            var targetSkin=target.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.bones.Any(b=>b.name=="Hand_R"));
            Quaternion Bind(SkinnedMeshRenderer skin,string name)
            {
                int i=Array.FindIndex(skin.bones,b=>b.name==name);
                if(i<0)throw new InvalidOperationException("Missing bind bone "+name);
                return(skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[i].inverse).rotation;
            }
            var fingers=new List<(Transform source,Transform target,Transform sourceHand,Transform targetHand,Quaternion handBasis,Quaternion basis)>();
            foreach(string side in new[]{"l","r"})foreach(var pair in new[]{("thumb","Thumb"),("index","IndexFinger"),("ring","Finger")})for(int j=1;j<=(pair.Item1=="thumb"?3:4);j++)
            {
                // The UE4 rig has three finger joints; Polygon has a weighted fourth
                // segment. It follows the same source tip joint, with its own bind basis.
                string sourceName=pair.Item1+"_0"+Mathf.Min(j,3)+"_"+side,targetName=pair.Item2+"_0"+j+(side=="r"?"1":"");
                var s=Find(source,sourceName);var t=Find(target.animator.gameObject,targetName);
                var handBasis=Quaternion.Inverse(Bind(sourceSkin,"hand_"+side))*Bind(targetSkin,"Hand_"+(side=="r"?"R":"L"));
                var basis=Quaternion.Inverse(Bind(sourceSkin,sourceName))*Bind(targetSkin,targetName);
                fingers.Add((s,t,side=="r"?sourceHand:sourceLeft,side=="r"?destinationHand:destinationLeft,handBasis,basis));
            }
            var surfaceFit=new HandSurfaceFit(sourceSkin,targetSkin,fingers.Select(f=>f.target).ToArray(),destinationLeft,destinationHand);
            Quaternion HandBasis(string side)=>Quaternion.Inverse(Bind(sourceSkin,"hand_"+side))*surfaceFit.MeshRotation("Hand_"+(side=="r"?"R":"L"))*Bind(targetSkin,"Hand_"+(side=="r"?"R":"L"));
            for(int i=0;i<fingers.Count;i++)
            {
                var finger=fingers[i];string side=finger.sourceHand==sourceHand?"r":"l";
                var basis=Quaternion.Inverse(Bind(sourceSkin,finger.source.name))*surfaceFit.MeshRotation(finger.target.name)*Bind(targetSkin,finger.target.name);
                fingers[i]=(finger.source,finger.target,finger.sourceHand,finger.targetHand,HandBasis(side),basis);
            }
            var rightLocalPosition=destinationHand.localPosition;var leftLocalPosition=destinationLeft.localPosition;
            destinationHand.SetPositionAndRotation(MapPoint(sourceHand.position),
                alignment*sourceHand.rotation*HandBasis("r"));
            destinationLeft.SetPositionAndRotation(MapPoint(sourceLeft.position),
                alignment*sourceLeft.rotation*HandBasis("l"));
            foreach(var finger in fingers)finger.target.rotation=finger.targetHand.rotation*Quaternion.Inverse(finger.handBasis)*Quaternion.Inverse(finger.sourceHand.rotation)*finger.source.rotation*finger.basis;
            surfaceFit.Fit(MapPoint);
            // Preserve the physical finger contact for every weapon after fitting the different glove surfaces.
            foreach(string side in new[]{"r","l"})
                {
                    var wrist=side=="r"?destinationHand:destinationLeft;
                    var sourceKnuckle=Find(source,"index_01_"+side);
                    var targetKnuckle=Find(target.animator.gameObject,"IndexFinger_01"+(side=="r"?"1":""));
                    var goal=MapPoint(sourceKnuckle.position);
                    wrist.position+=goal-targetKnuckle.position;
                }
            // Seat the gun once at the body's neutral firing hand. Neither hand reach nor reload
            // sampling may move this reference afterwards.
            var seating=destinationRightIdle.position-destinationHand.position;
            receiverRoot.position+=seating;targetOrigin+=seating;
            destinationHand.position+=seating;destinationLeft.position+=seating;
            destinationRightIdle=new Pose(destinationHand.position,destinationHand.rotation);
            destinationLeftIdle=new Pose(destinationLeft.position,destinationLeft.rotation);
            rightCalibration=Quaternion.Inverse(alignment*sourceRightIdle.rotation)*(destinationRightIdle.position-MapPoint(sourceRightIdle.position));
            leftCalibration=Quaternion.Inverse(alignment*sourceLeftIdle.rotation)*(destinationLeftIdle.position-MapPoint(sourceLeftIdle.position));
            var fingerCorrections=fingers.ToDictionary(f=>f.target,f=>Quaternion.Inverse(f.targetHand.rotation*Quaternion.Inverse(f.handBasis)*Quaternion.Inverse(f.sourceHand.rotation)*f.source.rotation*f.basis)*f.target.rotation);
            destinationHand.localPosition=rightLocalPosition;destinationLeft.localPosition=leftLocalPosition;
            // The persistent grip IK must use the same fitted pose as the authored
            // clip, otherwise it immediately overwrites the new idle wrists.
            void SetGrip(Transform grip,Pose pose){grip.SetPositionAndRotation(pose.position,pose.rotation);}
            SetGrip(target.right.data.target,destinationRightIdle);SetGrip(target.left.data.target,destinationLeftIdle);
            var fittedRight=new SoldierWeapon.GripPose{position=target.right.data.target.localPosition,rotation=target.right.data.target.localRotation,scale=target.right.data.target.localScale};
            var fittedLeft=new SoldierWeapon.GripPose{position=target.left.data.target.localPosition,rotation=target.left.data.target.localRotation,scale=target.left.data.target.localScale};
            var presentation=target.player.GetComponent<SoldierWeaponPresentation>();var root=target.animator.transform;var weaponRoot=presentation.weaponRoot;
            var sourceElbowContacts=sourceElbows.Select(point=>sourceReceiverIdle.inverse.MultiplyPoint3x4(point)).ToArray();
            var muscleIndices=Enumerable.Range(0,HumanTrait.MuscleCount).Where(i=>{int b=HumanTrait.BoneFromMuscle(i);return b==(int)HumanBodyBones.Spine||b==(int)HumanBodyBones.Chest||b==(int)HumanBodyBones.UpperChest||b==(int)HumanBodyBones.Neck||b==(int)HumanBodyBones.Head||b==(int)HumanBodyBones.LeftShoulder||b==(int)HumanBodyBones.RightShoulder||b==(int)HumanBodyBones.LeftUpperArm||b==(int)HumanBodyBones.RightUpperArm||b==(int)HumanBodyBones.LeftLowerArm||b==(int)HumanBodyBones.RightLowerArm||b==(int)HumanBodyBones.LeftHand||b==(int)HumanBodyBones.RightHand||(b>=(int)HumanBodyBones.LeftThumbProximal&&b<=(int)HumanBodyBones.RightLittleDistal);}).ToArray();
            var reloadBlend=AnimationUtility.GetEditorCurve(oldReload,EditorCurveBinding.FloatCurve("",typeof(Animator),"WeaponReloadWeight"));
            var sampledPose=new HumanPose();var output=new List<AnimationClip>();var weaponOutput=new List<AnimationClip>();float maxReach=0;
            // Locomotion supplies the body baseline only; old TPS reload/idle arm motion is not a transfer source.
            target.Begin(oldIdle);
            bool authoredFire=HasAuthoredHandAction(source,gun.transform,proof.fire);
            for(int action=0;action<(authoredFire?3:2);action++)
            {
                bool reload=action==1,fire=action==2;
                var input=reload?proof.reload:fire?proof.fire:proof.idle;float duration=Mathf.Max(.1f,input.length);int frames=Mathf.Max(1,Mathf.CeilToInt(duration*(action>0?60:30)));var bodyTracks=new Curves();var weaponTracks=new Curves();
                for(int f=0;f<=frames;f++)
                {
                    float time=duration*f/frames;proof.idle.SampleAnimation(source,0);input.SampleAnimation(source,time);target.Sample(0);handler.SetHumanPose(ref baseline);
                    // Sample mechanisms first, then carry the FPS receiver motion into the
                    // calibrated TPS action frame. Hands are transferred in receiver space.
                    string partAction=reload?"Reload":fire?"Fire":"Idle";
                    var partClip=WeaponAnimationSource.State(definition.thirdPersonParts,"Base Layer",partAction);
                    if(partClip)partClip.SampleAnimation(target.weapon.animator.gameObject,time);
                    var currentSource=receiver();
                    var desiredReceiverRotation=alignment*currentSource.rotation;
                    var correction=desiredReceiverRotation*Quaternion.Inverse(targetReceiver().rotation);
                    weaponRoot.rotation=correction*weaponRoot.rotation;
                    weaponRoot.position+=MapPoint(currentSource.GetColumn(3))-targetReceiver().MultiplyPoint3x4(Vector3.zero);
                    var currentTarget=targetReceiver();
                    var handMap=currentTarget*currentSource.inverse;
                    var handAlignment=currentTarget.rotation*Quaternion.Inverse(currentSource.rotation);
                    Vector3 Goal(bool left)=>handMap.MultiplyPoint3x4((left?sourceLeft:sourceHand).position)
                        +(handAlignment*(left?sourceLeft:sourceHand).rotation)*(left?leftCalibration:rightCalibration);
                    // Offline contact retarget: translate the shared action frame only as far as
                    // necessary to put both wrists within the full body's reachable workspace.
                    // The weapon and both hands always move together; mechanisms retain their paths.
                    var reachOffset=ReachOffset(target.animator,Goal(false),Goal(true));
                    weaponRoot.position+=reachOffset;
                    currentTarget=targetReceiver();handMap=currentTarget*currentSource.inverse;
                    for(int side=1;side>=0;side--)
                    {
                        bool left=side==0;var sourceWrist=left?sourceLeft:sourceHand;var start=left?sourceLeftIdle:sourceRightIdle;var finish=left?destinationLeftIdle:destinationRightIdle;var elbow=left?sourceLeftElbow:sourceElbow;
                        var goal=Goal(left);
                        var pole=(left?target.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm):target.animator.GetBoneTransform(HumanBodyBones.RightLowerArm)).position+currentTarget.MultiplyVector(currentSource.inverse.MultiplyPoint3x4(elbow.position)-sourceElbowContacts[side]);
                        var delta=handAlignment*sourceWrist.rotation*Quaternion.Inverse(start.rotation)*Quaternion.Inverse(alignment);
                        var arm=target.animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);var mid=target.animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);var hand=left?destinationLeft:destinationHand;
                        maxReach=Mathf.Max(maxReach,Solve(arm,mid,hand,goal,pole));hand.rotation=delta*finish.rotation;

                    }
                    foreach(var finger in fingers)finger.target.rotation=finger.targetHand.rotation*Quaternion.Inverse(finger.handBasis)*Quaternion.Inverse(finger.sourceHand.rotation)*finger.source.rotation*finger.basis*fingerCorrections[finger.target];
                    handler.GetHumanPose(ref sampledPose);
                    float weight=reload?Mathf.Clamp01(reloadBlend?.Evaluate(time*oldReload.length/duration)??Mathf.Min(Mathf.Clamp01(time/.1f),Mathf.Clamp01((duration-time)/.15f))):0;


                    foreach(int index in muscleIndices)bodyTracks.Add(EditorCurveBinding.FloatCurve("",typeof(Animator),MuscleBinding(index)),time,sampledPose.muscles[index]);
                    // Human-mapped fingers must use muscle curves. Their Transform tracks
                    // are ignored by Humanoid evaluation; only the weighted fourth tips
                    // are additional generic bones.
                    foreach(var finger in fingers)if(finger.target.name.StartsWith("Finger_04")||finger.target.name.StartsWith("IndexFinger_04"))bodyTracks.Transform(finger.target,root,time);weaponTracks.Transform(weaponRoot,root,time);
                    {
                        target.right.data.target.SetPositionAndRotation(destinationHand.position,destinationHand.rotation);
                        target.left.data.target.SetPositionAndRotation(destinationLeft.position,destinationLeft.rotation);
                        weaponTracks.Transform(target.right.data.target,root,time);weaponTracks.Transform(target.left.data.target,root,time);
                    }
                    if(reload)bodyTracks.Add(EditorCurveBinding.FloatCurve("",typeof(Animator),"WeaponReloadWeight"),time,weight);
                    if(fire)bodyTracks.Add(EditorCurveBinding.FloatCurve("",typeof(Animator),"WeaponFireWeight"),time,Mathf.Min(Mathf.Clamp01(time/.06f),Mathf.Clamp01((duration-time)/.12f)));
                }
                string actionName=reload?"Reload":fire?"Fire":"Idle",weaponName=reload?"ReloadWeapon":fire?"FireWeapon":"HoldWeapon";
                output.Add(Save(bodyTracks.Finish(actionName,action==0,duration),folder+"/"+actionName+".anim"));
                weaponOutput.Add(Save(weaponTracks.Finish(weaponName,action==0,duration),folder+"/"+weaponName+".anim"));
            }
            var replacements=new Dictionary<AnimationClip,AnimationClip>();replacements[oldIdle]=output[0];replacements[oldReload]=output[1];replacements[oldHold]=weaponOutput[0];replacements[oldWeaponReload]=weaponOutput[1];
            definition.thirdPersonAnimatedGrips=true;definition.thirdPersonAuthoredFire=authoredFire;
            definition.thirdPerson=Override(baselineDefinition.thirdPerson,replacements,folder+"/Body.overrideController");
            definition.thirdPerson=TpsControllerAdapter.Rebase(definition.thirdPerson,folder+"/Body.overrideController");
            if(authoredFire)TpsFireAdapter.Configure(definition,folder,output[2],weaponOutput[2]);

            var prefabPath=AssetDatabase.GetAssetPath(entry.thirdPersonPrefab);
            var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var hands=prefab.GetComponent<SoldierWeapon>();var placement=hands.thirdPersonHands;
                placement.rightGrip=fittedRight;placement.leftGrip=fittedLeft;hands.thirdPersonHands=placement;
                PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            WeaponGripFrame.Save(gun.GetComponent<SoldierWeaponView>(),AssetDatabase.GetAssetPath(view));
            WeaponGripFrame.Save(target.weapon,AssetDatabase.GetAssetPath(entry.thirdPersonPrefab));
            definition.name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(definition));EditorUtility.SetDirty(definition);AssetDatabase.SaveAssets();return new{id,humanMotion=output.All(c=>c.humanMotion),parts=partTracks,unreachableHandDistance=maxReach,handSurfaceRms=surfaceFit.Rms};
        }
        finally{Object.DestroyImmediate(source);Object.DestroyImmediate(baselineDefinition);}
    }
    static Vector3 ReachOffset(Animator animator,Vector3 rightGoal,Vector3 leftGoal)
    {
        var offset=Vector3.zero;
        for(int pass=0;pass<16;pass++)for(int side=0;side<2;side++)
        {
            bool left=side==0;
            var arm=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
            var elbow=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            float reach=Vector3.Distance(arm.position,elbow.position)+Vector3.Distance(elbow.position,hand.position)-.008f;
            var delta=(left?leftGoal:rightGoal)+offset-arm.position;
            if(delta.magnitude>reach)offset-=delta.normalized*(delta.magnitude-reach);
        }
        return offset;
    }
    static bool HasAuthoredHandAction(GameObject source,Transform gun,AnimationClip clip)
    {
        if(!clip)return false;
        var hands=new[]{Find(source,"hand_l"),Find(source,"hand_r")};
        clip.SampleAnimation(source,0);
        var positions=hands.Select(h=>gun.InverseTransformPoint(h.position)).ToArray();
        var rotations=hands.Select(h=>Quaternion.Inverse(gun.rotation)*h.rotation).ToArray();
        // Recoil moves a held weapon and hands together. A bolt or pump cycle
        // moves a hand relative to the weapon and must retain that authored path.
        for(int frame=1;frame<=24;frame++)
        {
            clip.SampleAnimation(source,clip.length*frame/24);
            for(int hand=0;hand<hands.Length;hand++)
                if(Vector3.Distance(positions[hand],gun.InverseTransformPoint(hands[hand].position))>.035f
                    ||Quaternion.Angle(rotations[hand],Quaternion.Inverse(gun.rotation)*hands[hand].rotation)>20f)return true;
        }
        return false;
    }
    public static string MuscleBinding(int index)
    {
        string name=HumanTrait.MuscleName[index];int bone=HumanTrait.BoneFromMuscle(index);
        if(bone<(int)HumanBodyBones.LeftThumbProximal||bone>(int)HumanBodyBones.RightLittleDistal)return name;
        // Imported Humanoid clips use hand namespaces for finger channels;
        // HumanTrait's display name ("Left Thumb 1 Stretched") is not a binding.
        var words=name.Split(' ');return words[0]+"Hand."+words[1]+"."+string.Join(" ",words.Skip(2));
    }
    static float Solve(Transform arm,Transform mid,Transform hand,Vector3 goal,Vector3 pole)
    {
        var start=arm.position;float a=Vector3.Distance(start,mid.position),b=Vector3.Distance(mid.position,hand.position);var axis=goal-start;
        float d=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.001f);axis.Normalize();var bend=Vector3.ProjectOnPlane(pole-start,axis).normalized;if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(Vector3.down,axis).normalized;
        float along=(a*a-b*b+d*d)/(2*d);var elbow=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));arm.rotation=Quaternion.FromToRotation(mid.position-start,elbow-start)*arm.rotation;mid.rotation=Quaternion.FromToRotation(hand.position-mid.position,start+axis*d-mid.position)*mid.rotation;return Vector3.Distance(hand.position,goal);
    }
}
