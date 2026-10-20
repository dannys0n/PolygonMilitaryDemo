using System;using System.Collections.Generic;using System.Linq;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;

// Retarget downloaded clips onto the unchanged UE4 comparison skeleton, offline.
// No mesh fitting, bind-pose edits, bone scaling, or runtime retarget component.
public static class ArenaDownloadedUe4Retarget
{
    public static void Apply(GameObject root,string output)
    {
        var source=root.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        bool benelli=source.ContainsKey("hand_R");
        Transform Bone(string stem,string side)=>source[stem+(benelli?"_"+side.ToUpperInvariant():"_L."+side.ToUpperInvariant())];
        Transform Finger(string family,int segment,string side)
        {
            if(family=="pinky")family="pink";
            var name=benelli?(family=="thumb"?"thumb_1_"+side.ToUpperInvariant():family+"_"+side.ToUpperInvariant()):family+"_L."+side.ToUpperInvariant();
            var bone=source[name];for(int i=1;i<segment;i++)bone=bone.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith(family+"_"))??bone;return bone;
        }
        var rig=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ArenaUe4RigProofBuilder.OriginalPreviewRigPath));
        rig.name="PolygonUE4Rig";rig.transform.SetParent(root.transform,false);
        rig.transform.localPosition=new Vector3(0,-48.5f,0);rig.transform.localRotation=Quaternion.Euler(0,180,0);rig.transform.localScale=Vector3.one*50;
        foreach(var animator in rig.GetComponentsInChildren<Animator>(true))Object.DestroyImmediate(animator);
        var target=rig.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
        var mapping=new Dictionary<Transform,Transform>();
        foreach(string side in new[]{"l","r"})
        {
            mapping[target["hand_"+side]]=Bone("hand",side);
            foreach(string family in new[]{"thumb","index","middle","ring","pinky"})for(int i=1;i<=3;i++)
                mapping[target[family+"_0"+i+"_"+side]]=Finger(family,i,side);
        }
        Vector3 Normal(Transform hand,Transform index,Transform ring)=>Vector3.Cross(index.position-hand.position,ring.position-hand.position).normalized;
        Quaternion TargetFrame(Transform bone)
        {
            string side=bone.name.EndsWith("_r")?"r":"l";var next=bone.Cast<Transform>().FirstOrDefault(t=>!t.name.Contains("twist"));
            var direction=bone.name.StartsWith("hand_")?target["index_01_"+side].position-bone.position:next?next.position-bone.position:bone.position-bone.parent.position;
            return Quaternion.LookRotation(direction,Normal(target["hand_"+side],target["index_01_"+side],target["ring_01_"+side]));
        }
        Quaternion SourceFrame(Transform bone,string side)
        {
            var next=bone.Cast<Transform>().FirstOrDefault(t=>!t.name.Contains("twist"));
            var direction=bone==Bone("hand",side)?Finger("index",1,side).position-bone.position:next?next.position-bone.position:bone.position-bone.parent.position;
            return Quaternion.LookRotation(direction,Normal(Bone("hand",side),Finger("index",1,side),Finger("ring",1,side)));
        }
        var rest=target.Values.ToDictionary(t=>t,t=>(position:t.localPosition,rotation:t.localRotation,scale:t.localScale));
        var offsets=mapping.Keys.ToDictionary(t=>t,t=>Quaternion.Inverse(TargetFrame(t))*t.rotation);
        void Solve(string side,Vector3 goal,Vector3 pole)
        {
            var upper=target["upperarm_"+side];var lower=target["lowerarm_"+side];var hand=target["hand_"+side];
            var start=upper.position;float a=Vector3.Distance(start,lower.position),b=Vector3.Distance(lower.position,hand.position);
            var axis=goal-start;float distance=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f);axis.Normalize();
            var bend=Vector3.ProjectOnPlane(pole-start,axis).normalized;if(bend.sqrMagnitude<.1f)bend=Vector3.ProjectOnPlane(Vector3.down,axis).normalized;
            float along=(a*a-b*b+distance*distance)/(2*distance);var elbow=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-start,elbow-start)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,start+axis*distance-lower.position)*lower.rotation;
        }
        var animatorRoot=root.GetComponent<Animator>();var controller=animatorRoot.runtimeAnimatorController;
        var overrides=new AnimatorOverrideController(controller);var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();
        var animated=target.Values.Where(t=>t.name.StartsWith("upperarm_")||t.name.StartsWith("lowerarm_")||mapping.ContainsKey(t)).ToArray();
        foreach(var clip in controller.animationClips.Distinct())
        {
            var result=Object.Instantiate(clip);result.name=clip.name+"_UE4SharedRig";
            var curves=animated.ToDictionary(t=>t,t=>new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()});
            int frames=Mathf.Max(1,Mathf.CeilToInt(clip.length*60));
            for(int frame=0;frame<=frames;frame++)
            {
                float time=Mathf.Min(clip.length,frame/60f);clip.SampleAnimation(root,time);
                foreach(var item in rest){item.Key.localPosition=item.Value.position;item.Key.localRotation=item.Value.rotation;item.Key.localScale=item.Value.scale;}
                foreach(string side in new[]{"l","r"})Solve(side,Bone("hand",side).position,Bone("lowerarm",side).position);
                foreach(var item in mapping.OrderBy(p=>Depth(p.Key)))item.Key.rotation=SourceFrame(item.Value,item.Key.name.EndsWith("_r")?"r":"l")*offsets[item.Key];
                foreach(var bone in animated){var q=bone.localRotation;var c=curves[bone];if(frame>0&&c[3][c[3].length-1].value*q.w+c[0][c[0].length-1].value*q.x+c[1][c[1].length-1].value*q.y+c[2][c[2].length-1].value*q.z<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);c[0].AddKey(time,q.x);c[1].AddKey(time,q.y);c[2].AddKey(time,q.z);c[3].AddKey(time,q.w);}
            }
            foreach(var bone in animated)for(int axis=0;axis<4;axis++)AnimationUtility.SetEditorCurve(result,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bone,root.transform),typeof(Transform),"m_LocalRotation."+"xyzw"[axis]),curves[bone][axis]);
            result.EnsureQuaternionContinuity();string path=output+"/"+result.name+".anim";Save(result,path);pairs.Add(new KeyValuePair<AnimationClip,AnimationClip>(clip,AssetDatabase.LoadAssetAtPath<AnimationClip>(path)));
        }
        overrides.ApplyOverrides(pairs);Save(overrides,output+"/SharedUE4.controller");
        var saved=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(output+"/SharedUE4.controller");animatorRoot.runtimeAnimatorController=saved;root.GetComponent<SoldierWeaponView>().animationOverride=saved;
        foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.name=="hardknuckle"||s.name=="sleeve").ToArray())Object.DestroyImmediate(skin);
        var idle=pairs.First(p=>p.Key.name.Contains("Idle")).Value;idle.SampleAnimation(root,0);
    }
    static int Depth(Transform bone){int d=0;while(bone.parent){d++;bone=bone.parent;}return d;}
    static void Save(Object asset,string path)
    {
        var old=AssetDatabase.LoadMainAssetAtPath(path);if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);}else AssetDatabase.CreateAsset(asset,path);
    }
}

