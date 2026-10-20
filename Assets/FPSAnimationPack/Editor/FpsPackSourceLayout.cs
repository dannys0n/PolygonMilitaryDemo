using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Native Unreal settings and montage segments, extracted without changing the source assets.
[Serializable] public class FpsPackSourceLayout
{
    [Serializable] public class Segment { public string source; public float start, @from, to, rate; public int loops; }
    [Serializable] public class Visibility { public float time; public string bone; public bool hide; }
    [Serializable] public class Action { public string source,file,skeleton,rigFile,camera; public float duration; public Segment[] segments; public Visibility[] visibility; }
    [Serializable] public class Link { public string key,character,weapon; }
    [Serializable] public class Placement { public float[] translation,rotation,scale; }
    [Serializable] public class Variant { public string rig,pose,skeleton; public Link[] links; public Placement placement; }
    [Serializable] public class Preset { public string id,family,mesh,viewmodel; public Variant[] variants; }
    public Action[] actions;
    public Preset[] presets;
    public static FpsPackSourceLayout Read() => JsonUtility.FromJson<FpsPackSourceLayout>(File.ReadAllText(FpsPackPreviewCatalog.Root+"source-layout.json"));
}

public static class FpsPackActionBuilder
{
    [Serializable] public class Manifest { public FpsPackPreviewCatalog.Entry[] assets; }
    [Serializable] public class Rig { public string file,skeleton,label; public int weightedBones,missingHelpers; }
    [Serializable] public class Rigs { public Rig[] rigs; }

    [MenuItem("Tools/FPS Animation Pack/Rebuild authored actions")]
    public static void Build() => BuildOnly(null);
    public static void BuildOnly(HashSet<string> selectedSources)
    {
        var layout=FpsPackSourceLayout.Read();
        var original=JsonUtility.FromJson<Manifest>("{\"assets\":"+File.ReadAllText(FpsPackPreviewCatalog.Root+"export-manifest.json")+"}").assets;
        var bySource=original.ToDictionary(e=>e.source);
        var records=new List<FpsPackPreviewCatalog.Entry>();
        var cache=new Dictionary<string,AnimationClip>();
        AnimationClip Load(string source)
        {
            if(!cache.TryGetValue(source,out var clip))
            {
                clip=FpsPackPreview.LoadClip(bySource[source].Path);
                if(!clip)throw new Exception("Montage references a sequence with no clip: "+source);
                cache[source]=clip;
            }
            return clip;
        }
        try
        {
            AssetDatabase.StartAssetEditing();
            int progress=0;
            foreach(var action in layout.actions)
            {
                if(selectedSources!=null && !selectedSources.Contains(action.source))
                {records.Add(Record(action));continue;}
                if(EditorUtility.DisplayCancelableProgressBar("Rebuilding source actions",action.file,(float)progress++/layout.actions.Length))throw new OperationCanceledException("Action rebuild cancelled; original FBX assets are unchanged.");
                var clip=new AnimationClip {name=Path.GetFileNameWithoutExtension(action.file),frameRate=60,legacy=false};
                var bindings=action.segments.SelectMany(s=>AnimationUtility.GetCurveBindings(Load(s.source))).Distinct().ToArray();
                var curves=new List<AnimationCurve>();
                var outputBindings=new List<EditorCurveBinding>();
                foreach(var binding in bindings)
                {
                    var keys=new List<Keyframe>();
                    foreach(var segment in action.segments)
                    {
                        var curve=AnimationUtility.GetEditorCurve(Load(segment.source),binding);
                        if(curve==null)continue;
                        float span=segment.to-segment.@from;
                        for(int loop=0;loop<segment.loops;loop++)
                        {
                            float start=segment.start+loop*span/segment.rate;
                            var sourceKeys=curve.keys.Where(k=>k.time>segment.@from+0.000001f && k.time<segment.to-0.000001f).ToList();
                            // Keep evaluated trim boundaries as well as every original authored key.
                            sourceKeys.Insert(0,Boundary(curve,segment.@from));
                            sourceKeys.Add(Boundary(curve,segment.to));
                            foreach(var sourceKey in sourceKeys)
                            {
                                var key=sourceKey;
                                key.time=start+(key.time-segment.@from)/segment.rate;
                                key.inTangent*=segment.rate;key.outTangent*=segment.rate;
                                if(keys.Count>0 && key.time<=keys[keys.Count-1].time+0.000001f)
                                {
                                    // Preserve the preceding segment's endpoint right up to an actual discontinuity.
                                    var previous=keys[keys.Count-1];
                                    if(Mathf.Abs(previous.value-key.value)>0.00001f && previous.time>0)
                                    {previous.time=key.time-0.00001f;previous.outTangent=float.PositiveInfinity;keys[keys.Count-1]=previous;}
                                    else
                                    {
                                        // Equal endpoint values can still have different incoming/outgoing velocities.
                                        // Keep the previous segment's arrival tangent and the new segment's departure tangent.
                                        key.inTangent=previous.inTangent;key.inWeight=previous.inWeight;
                                        key.weightedMode=(previous.weightedMode & WeightedMode.In)|(key.weightedMode & WeightedMode.Out);
                                        keys.RemoveAt(keys.Count-1);
                                    }
                                }
                                keys.Add(key);
                            }
                        }
                    }
                    if(keys.Count>0){outputBindings.Add(binding);curves.Add(Compact(keys));}
                }
                AnimationUtility.SetEditorCurves(clip,outputBindings.ToArray(),curves.ToArray());
                // Imported quaternion curves already have source continuity. Recomputing tangents
                // here would smooth across authored montage discontinuities and alter preceding frames.
                BakeVisibility(clip,action);
                var settings=AnimationUtility.GetAnimationClipSettings(clip);
                settings.startTime=0;settings.stopTime=action.duration;settings.loopTime=false;
                AnimationUtility.SetAnimationClipSettings(clip,settings);
                string path=FpsPackPreviewCatalog.Root+action.file;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);EditorUtility.SetDirty(existing);}
                else AssetDatabase.CreateAsset(clip,path);
                records.Add(Record(action));
            }
        }
        finally {AssetDatabase.StopAssetEditing();EditorUtility.ClearProgressBar();}
        File.WriteAllText(FpsPackPreviewCatalog.Root+"action-manifest.json",JsonUtility.ToJson(new Manifest {assets=records.ToArray()},true));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BuildMannequinCompatibility(original);
        AssetDatabase.SaveAssets();
        Debug.Log("FPS_AUTHORED_ACTIONS_BUILT "+records.Count);
    }
    static FpsPackPreviewCatalog.Entry Record(FpsPackSourceLayout.Action action) => new FpsPackPreviewCatalog.Entry {file=action.file,source=action.source,type="AnimMontage",skeleton=action.skeleton,success=true,rigFile=action.rigFile};
    static AnimationCurve Compact(List<Keyframe> keys)
    {
        // Native FBX baking keys every helper bone every frame, even while completely stationary.
        // Retain moving curves verbatim; remove only constant channels below sub-micron precision.
        float value=keys[0].value;
        if(keys.Count>2 && keys.All(k=>Mathf.Abs(k.value-value)<.000001f && Mathf.Abs(k.inTangent)<.000001f && Mathf.Abs(k.outTangent)<.000001f))
            return AnimationCurve.Constant(keys[0].time,keys.Last().time,value);
        return new AnimationCurve(keys.ToArray());
    }
    static Keyframe Boundary(AnimationCurve curve,float time)
    {
        foreach(var key in curve.keys)if(Mathf.Abs(key.time-time)<.000001f)return key;
        const float delta=.00001f;
        float slope=(curve.Evaluate(time+delta)-curve.Evaluate(time-delta))/(2*delta);
        return new Keyframe(time,curve.Evaluate(time),slope,slope);
    }
    static void BakeVisibility(AnimationClip clip,FpsPackSourceLayout.Action action)
    {
        if(action.visibility==null)return;
        foreach(var group in action.visibility.GroupBy(v=>v.bone))
        {
            var bindings=AnimationUtility.GetCurveBindings(clip);
            var path=bindings.Select(b=>b.path).FirstOrDefault(p=>p.Split('/').Last().Equals(group.Key,StringComparison.OrdinalIgnoreCase));
            if(path==null)throw new Exception(action.source+": visibility bone not found: "+group.Key);
            foreach(string axis in new[]{"x","y","z"})
            {
                var binding=EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalScale."+axis);
                var original=AnimationUtility.GetEditorCurve(clip,binding);
                float Scale(float t)=>original?.Evaluate(t)??1;
                var keys=new List<Keyframe>{new Keyframe(0,Scale(0))};
                foreach(var change in group.OrderBy(v=>v.time))
                    keys.Add(new Keyframe(Mathf.Clamp(change.time,0,action.duration),change.hide?0:Scale(change.time)));
                keys.Add(new Keyframe(action.duration,keys.Last().value));
                var curve=new AnimationCurve(keys.GroupBy(k=>k.time).Select(g=>g.Last()).ToArray());
                for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Constant);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Constant);}
                AnimationUtility.SetEditorCurve(clip,binding,curve);
            }
        }
    }
    static void BuildMannequinCompatibility(FpsPackPreviewCatalog.Entry[] original)
    {
        var mesh=original.Single(e=>e.file.EndsWith("Mannequin_UEFN/SKM_UEFN_Mannequin.fbx"));
        var reference=original.First(e=>e.file=="Animations/UE5/AKX/A_UE5_FP_AKX_Reload_Empty.fbx");
        var clip=FpsPackPreview.LoadClip(reference.Path);
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(mesh.Path));
        try
        {
            var animated=new HashSet<string>(AnimationUtility.GetCurveBindings(clip).Select(b=>b.path));
            var weighted=new HashSet<Transform>();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                using var weights=skin.sharedMesh.GetAllBoneWeights();
                foreach(var weight in weights)
                    if(weight.weight>0.00001f)weighted.Add(skin.bones[weight.boneIndex]);
            }
            var missing=weighted.Where(b=>!animated.Contains(AnimationUtility.CalculateTransformPath(b,model.transform))).Select(b=>b.name).ToArray();
            if(missing.Length>0)throw new Exception("UEFN weighted bones not animated by UE5: "+string.Join(",",missing));
            int helpers=animated.Count(p=>p.Length>0 && !model.transform.Find(p));
            // This is an explicit, verified preview compatibility alias; the native manifest stays unchanged.
            var result=new Rigs {rigs=new[]{new Rig {file=mesh.file,skeleton=reference.skeleton,label="UE5 / UEFN full mannequin",weightedBones=weighted.Count,missingHelpers=helpers}}};
            File.WriteAllText(FpsPackPreviewCatalog.Root+"preview-rigs.json",JsonUtility.ToJson(result,true));
            AssetDatabase.ImportAsset(FpsPackPreviewCatalog.Root+"preview-rigs.json");
        }
        finally {UnityEngine.Object.DestroyImmediate(model);}
    }
}
