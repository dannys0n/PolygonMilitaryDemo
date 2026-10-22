using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Bake weapon mechanisms only; the existing player rig, hand poses and firing clips remain unchanged.
public static class M249PartsBuilder
{
 const string ClipFolder="Assets/Game/Animations/Weapons/Clips";
 const string ControllerPath="Assets/Game/Animations/Weapons/Overrides/M249_WeaponParts.overrideController";
 static T Save<T>(T value,string path)where T:Object
 {value.name=System.IO.Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(value,path);return value;}
 static Transform Bone(GameObject go,string name)=>go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>s.bones).FirstOrDefault(b=>b&&b.name==name);
 [MenuItem("Tools/Game/Weapons/Rebuild M249 mechanisms")]
 static void Rebuild()=>Build();
 // The source tray was baked into the receiver despite its empty named transform.
 // Rebind its intact plate and roller islands once; no mesh splitting runs in gameplay.
 public static object PrepareFeedTray()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode");
  const string path="Assets/Game/Prefabs/Weapons/Bases/M249_Base.prefab";
  var go=PrefabUtility.LoadPrefabContents(path);
  try
  {
   var skin=go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s=>s.name=="M249");
   var mesh=skin.sharedMesh;int main=Array.FindIndex(skin.bones,b=>b.name=="main");
   int existing=Array.FindIndex(skin.bones,b=>b.name=="Bullet_holder");
   if(existing>=0&&mesh.boneWeights.Count(w=>w.boneIndex0==existing&&w.weight0>.99f)==750)return new{rebound=0,alreadyPrepared=true};
   var vertices=mesh.vertices;var weights=mesh.boneWeights;var triangles=mesh.triangles;
   var parents=Enumerable.Range(0,vertices.Length).ToArray();
   int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
   void Union(int a,int b){a=Find(a);b=Find(b);if(a!=b)parents[a]=b;}
   var weld=new Dictionary<Vector3Int,int>();
   for(int i=0;i<vertices.Length;i++){var key=Vector3Int.RoundToInt(vertices[i]*100000);if(weld.TryGetValue(key,out int other))Union(i,other);else weld[key]=i;}
   for(int i=0;i<triangles.Length;i+=3){Union(triangles[i],triangles[i+1]);Union(triangles[i],triangles[i+2]);}
   var groups=Enumerable.Range(0,vertices.Length).Where(i=>weights[i].boneIndex0==main).GroupBy(Find).ToArray();
   var plate=groups.Single(g=>g.Count()==620).ToArray();var bounds=new Bounds(vertices[plate[0]],Vector3.zero);foreach(int i in plate)bounds.Encapsulate(vertices[i]);
   var roller=groups.Single(g=>g.Count()==130&&bounds.Contains(g.Aggregate(Vector3.zero,(sum,i)=>sum+vertices[i])/g.Count())).ToArray();
   var tray=go.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Bullet_holder");var hinge=Bone(go,"open");
   tray.SetParent(hinge.parent,true);tray.SetPositionAndRotation(hinge.position,hinge.parent.rotation);
   int index=existing>=0?existing:skin.bones.Length;var selected=plate.Concat(roller).ToArray();foreach(int i in selected)weights[i]=new BoneWeight{boneIndex0=index,weight0=1};
   var edited=Object.Instantiate(mesh);edited.name=mesh.name.Replace("(Clone)","");var binds=mesh.bindposes.ToList();while(binds.Count<=index)binds.Add(Matrix4x4.identity);binds[index]=tray.worldToLocalMatrix*skin.transform.localToWorldMatrix;edited.bindposes=binds.ToArray();edited.boneWeights=weights;
   mesh.Clear(false);EditorUtility.CopySerialized(edited,mesh);Object.DestroyImmediate(edited);EditorUtility.SetDirty(mesh);
   if(existing<0)skin.bones=skin.bones.Concat(new[]{tray}).ToArray();
   PrefabUtility.SaveAsPrefabAsset(go,path);AssetDatabase.SaveAssets();return new{rebound=selected.Length,alreadyPrepared=false};
  }
  finally{PrefabUtility.UnloadPrefabContents(go);}
 }
 public static object Build()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Bake in Edit Mode");
  PrepareFeedTray();
  var catalog=AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
  var entry=catalog.entries.First(e=>e.weapon.catalogId=="polygon-machinegun");var definition=entry.weapon;
  var proof=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(PolygonWeaponConversionBuilder.Folder("polygon-machinegun")+"/Conversion.asset");
  var original=Object.Instantiate(entry.firstPersonPrefab.gameObject);var source=Object.Instantiate(proof.converted.firstPersonPrefab.gameObject);
  AnimationClip idle=null,reload=null;
  try
  {
   proof.idle.SampleAnimation(source,0);
   var names=new[]{"open","reload","magazine","Bullet_holder"}.Concat(Enumerable.Range(0,12).Select(i=>"Bone."+(6+i*2).ToString("D3"))).ToArray();
   var pairs=names.Select(n=>(target:Bone(original,n),source:Bone(source,n))).Where(p=>p.target&&p.source).ToArray();
   if(pairs.Length!=names.Length)throw new InvalidOperationException("Missing M249 mechanisms");
   var rest=pairs.Select(p=>(p:p.target.localPosition,q:p.target.localRotation,s:p.target.localScale,sp:p.source.localPosition,sq:p.source.localRotation,ss:p.source.localScale)).ToArray();
   // The imported Polygon skeleton is in centimeters while the adapted rig is baked in meters.
   float scale=Bone(original,"open").localPosition.magnitude/Bone(source,"open").localPosition.magnitude;
   var tracks=new Dictionary<EditorCurveBinding,List<Keyframe>>();
   float duration=definition.reloadDuration;int frames=Mathf.CeilToInt(duration*60);
   for(int f=0;f<=frames;f++)
   {
    float time=duration*f/frames;if(f==0||f==frames)proof.idle.SampleAnimation(source,0);else proof.reload.SampleAnimation(source,proof.reload.length*f/frames);
    for(int i=0;i<pairs.Length;i++)
    {
     var pair=pairs[i];var r=rest[i];var p=r.p+(pair.source.localPosition-r.sp)*scale;var q=pair.source.localRotation*Quaternion.Inverse(r.sq)*r.q;
     Vector3 Ratio(Vector3 a,Vector3 b)=>new Vector3(Mathf.Abs(b.x)>1e-6f?a.x/b.x:1,Mathf.Abs(b.y)>1e-6f?a.y/b.y:1,Mathf.Abs(b.z)>1e-6f?a.z/b.z:1);
     var s=Vector3.Scale(r.s,Ratio(pair.source.localScale,r.ss));
     for(int axis=0;axis<10;axis++)
     {
      var binding=EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pair.target,original.transform),typeof(Transform),axis<3?"m_LocalPosition."+"xyz"[axis]:axis<7?"m_LocalRotation."+"xyzw"[axis-3]:"m_LocalScale."+"xyz"[axis-7]);
      if(!tracks.TryGetValue(binding,out var keys))tracks[binding]=keys=new List<Keyframe>();
      keys.Add(new Keyframe(time,axis<3?p[axis]:axis<7?q[axis-3]:s[axis-7]));
     }
    }
   }
   var baseline=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Animations/Weapons/Overrides/M4_WeaponParts.overrideController");
   reload=Object.Instantiate(WeaponAnimationSource.State(baseline,"Base Layer","Reload"));
   idle=Object.Instantiate(WeaponAnimationSource.State(baseline,"Base Layer","Idle"));
   reload.frameRate=idle.frameRate=60;
   foreach(var track in tracks)
   {
    var values=track.Value;var curve=new AnimationCurve(values.All(k=>Mathf.Abs(k.value-values[0].value)<1e-6f)?new[]{values[0],values[values.Count-1]}:values.ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(reload,track.Key,curve);
    AnimationUtility.SetEditorCurve(idle,track.Key,AnimationCurve.Constant(0,.1f,track.Value[0].value));
   }
   reload.EnsureQuaternionContinuity();idle.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(idle);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(idle,settings);
   reload=Save(reload,ClipFolder+"/M249_Parts_Reload.anim");idle=Save(idle,ClipFolder+"/M249_Parts_Idle.anim");
   var old=definition.firstPersonParts;var controller=old is AnimatorOverrideController existing?Object.Instantiate(existing):new AnimatorOverrideController(old);
   var oldReload=WeaponAnimationSource.State(old,"Base Layer","Reload");var oldIdle=WeaponAnimationSource.State(old,"Base Layer","Idle");var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();controller.GetOverrides(overrides);
   controller.ApplyOverrides(overrides.Select(p=>new KeyValuePair<AnimationClip,AnimationClip>(p.Key,p.Value==oldReload||p.Key==oldReload?reload:p.Value==oldIdle||p.Key==oldIdle?idle:p.Value)).ToList());
   definition.firstPersonParts=Save(controller,ControllerPath);EditorUtility.SetDirty(definition);
   const string basePath="Assets/Game/Prefabs/Weapons/Bases/M249_Base.prefab";
   var weaponBase=PrefabUtility.LoadPrefabContents(basePath);
   try
   {
    var cover=Bone(weaponBase,"open");
    foreach(var socket in weaponBase.GetComponentsInChildren<Transform>(true).Where(t=>new[]{"OpticMount","RearSight","RearIronMount","SightRiserMount"}.Contains(t.name)).ToArray())socket.SetParent(cover,true);
    PrefabUtility.SaveAsPrefabAsset(weaponBase,basePath);
   }
   finally{PrefabUtility.UnloadPrefabContents(weaponBase);}
   AssetDatabase.SaveAssets();return new{duration,scale,parts=pairs.Length};
  }
  finally{Object.DestroyImmediate(original);Object.DestroyImmediate(source);}
 }
}
