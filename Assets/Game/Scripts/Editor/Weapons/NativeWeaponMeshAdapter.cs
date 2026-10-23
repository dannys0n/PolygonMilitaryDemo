using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Rebind compatible Polygon gun geometry to the native animated part skeleton.
// Geometry is generated once; there is no runtime mesh fitting or second rig.
public static class NativeWeaponMeshAdapter
{
 sealed class Geometry
 {
  public readonly List<Vector3> vertices=new(),normals=new();public readonly List<Vector2> uv=new();public readonly List<BoneWeight> weights=new();
  public readonly List<Material> materials=new();public readonly List<List<int>> triangles=new();
  public void Add(Mesh mesh,Material[] mats,Matrix4x4 frame,Func<BoneWeight,BoneWeight> weight)
  {
   int start=vertices.Count;var v=mesh.vertices;var n=mesh.normals;var tex=mesh.uv;var w=mesh.boneWeights;
   for(int i=0;i<v.Length;i++){vertices.Add(frame.MultiplyPoint3x4(v[i]));normals.Add(n.Length==v.Length?frame.inverse.transpose.MultiplyVector(n[i]).normalized:Vector3.up);uv.Add(tex.Length==v.Length?tex[i]:Vector2.zero);weights.Add(weight(w.Length==v.Length?w[i]:new BoneWeight{weight0=1}));}
   for(int sub=0;sub<mesh.subMeshCount;sub++){var material=mats[Mathf.Min(sub,mats.Length-1)];int m=materials.IndexOf(material);if(m<0){m=materials.Count;materials.Add(material);triangles.Add(new());}triangles[m].AddRange(mesh.GetTriangles(sub).Select(i=>i+start));}
  }
  public GameObject Save(Transform gun,Transform[] bones,string folder,string name)
  {
   if(vertices.Count==0)return null;var mesh=new Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.boneWeights=weights.ToArray();mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*gun.localToWorldMatrix).ToArray();mesh.subMeshCount=triangles.Count;for(int i=0;i<triangles.Count;i++)mesh.SetTriangles(triangles[i],i);mesh.RecalculateBounds();
   string path=folder+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){old.Clear(false);EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);
   var node=new GameObject(name);node.transform.SetParent(gun,false);var skin=node.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterials=materials.ToArray();skin.bones=bones;skin.rootBone=bones[0];skin.localBounds=new Bounds(mesh.bounds.center,Vector3.one*3);skin.updateWhenOffscreen=true;return node;
  }
 }
 static Transform Find(GameObject go,string name)=>go.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
 static bool Visible(Renderer r)=>r.enabled&&!r.forceRenderingOff&&r.gameObject.activeInHierarchy;
 static Vector3 Center(IEnumerable<Vector3> points){var p=points.ToArray();return p.Length==0?Vector3.zero:p.Aggregate(Vector3.zero,(s,v)=>s+v)/p.Length;}
 static string Semantic(string old)
 {
  var n=old.ToLowerInvariant();if(n=="bone"||n=="main"||n=="root")return "Root";
  if(n=="drum_open")return "CraneArm";if(n=="drum")return "Drum";if(n=="reload_drm_bullets")return "Ejector";if(n=="reload_trigger")return "CylinderRelease";
  if(n=="open")return "Top";if(n=="reload")return "Rail";if(n=="bullet_holder")return "Feed_Tray";
  if(n.Contains("magazine")||n=="mag")return "Mag";if(n.Contains("trigger"))return "Trigger";
  if(n.Contains("slider")||n.Contains("charge")||n.Contains("bolt"))return "Bolt";return null;
 }
 public static NativePartFit Apply(GameObject fps,WeaponCatalog.Entry reference,string key,string folder)
 {
  var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/AnimationTests/Clips/"+key+"_Idle.anim");idle.SampleAnimation(fps,0);Find(fps,"SourceArms").SetLocalPositionAndRotation(new Vector3(0,-.1f,0),Quaternion.Euler(0,180,0));
  var gun=Find(fps,"SourceWeapon");var nativeSkins=gun.GetComponentsInChildren<SkinnedMeshRenderer>(true);var nativeBones=nativeSkins.SelectMany(s=>s.bones).Distinct().ToArray();if(nativeBones.Length==0)throw new Exception("Native weapon has no part skeleton");var root=nativeBones.FirstOrDefault(b=>b.name=="Root"||b.name=="Body")??nativeBones[0];
  var nativePoints=new Dictionary<Transform,List<Vector3>>();
  foreach(var skin in nativeSkins){var baked=new Mesh();skin.BakeMesh(baked);var v=baked.vertices;var w=skin.sharedMesh.boneWeights;for(int i=0;i<v.Length;i++){var b=skin.bones[w[i].boneIndex0];if(!nativePoints.TryGetValue(b,out var p))nativePoints[b]=p=new();p.Add(skin.transform.TransformPoint(v[i]));}Object.DestroyImmediate(baked);}
  // The physical TPS grip remains valid after the shared FPS skeleton is replaced.
  using var source=new Mk18PoseFit.Source(reference,true);source.Begin(WeaponAnimationSource.Body(reference.weapon.thirdPerson,"Idle"));source.Sample(0);
  var skins=source.weapon.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(Visible).Where(r=>!new[]{"scope","glass","dot","lens"}.Any(n=>r.name.ToLowerInvariant().Contains(n))).ToArray();
  var hand=source.animator.GetBoneTransform(HumanBodyBones.RightHand);var targetHand=Find(fps,"hand_r");var axis=(source.weapon.frontSight.position-source.weapon.rearSight.position).normalized;var rotation=Quaternion.Inverse(Quaternion.LookRotation(axis,source.weapon.rearSight.up));
  var originalPoints=new List<Vector3>();foreach(var s in skins){var m=new Mesh();s.BakeMesh(m);originalPoints.AddRange(m.vertices.Select(Matrix4x4.TRS(s.transform.position,s.transform.rotation,Vector3.one).MultiplyPoint3x4));Object.DestroyImmediate(m);}
  var filters=source.weapon.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.TryGetComponent<Renderer>(out var r)&&Visible(r)).ToArray();
  foreach(var filter in filters)originalPoints.AddRange(filter.sharedMesh.vertices.Select(filter.transform.TransformPoint));
  if(originalPoints.Count==0)throw new Exception("No Polygon gun geometry: "+reference.weapon.displayName);
  float oldLength=originalPoints.Max(p=>(rotation*p).z)-originalPoints.Min(p=>(rotation*p).z);var np=nativePoints.SelectMany(p=>p.Value).ToArray();float scale=Mathf.Clamp((np.Max(p=>p.z)-np.Min(p=>p.z))/oldLength,.7f,1.35f);
  // Fit the grip region, rather than making barrel/stock length decide hand placement.
  if(key=="RPG7_UE4")scale=1f;else scale=Mathf.Min(scale,1.1f);
  Vector3 seat=key=="RPG7_UE4"?new Vector3(0,-.055f,.015f):key=="SVD_UE4"?new Vector3(0,-.045f,.015f):key=="MPS5_UE4"?new Vector3(0,-.025f,.005f):key=="Striker-V_UE4"?new Vector3(0,-.025f,0):key=="MGX5_UE4"?new Vector3(0,.010f,0):Vector3.zero;
  var baseRotation=rotation;float baseScale=scale;var frameAdjustment=Matrix4x4.identity;
  if(key=="RPG7_UE4")
  {
   // These are measured grip centers in the reference gun frame, rather than
   // source-prefab coordinates (whose root may itself be rotated).
   bool military=reference.weapon.catalogId=="military-rpg";
   var right=military?new Vector3(-.011f,-.160f,-.206f):new Vector3(-.011f,-.161f,-.211f);
   var left=military?new Vector3(-.015f,-.165f,-.083f):new Vector3(-.016f,-.160f,-.082f);
   var rightTarget=new Vector3(0,-.165f,-.155f);var leftTarget=new Vector3(0,-.174f,.021f);
   float ratio=Vector3.Distance(rightTarget,leftTarget)/Vector3.Distance(right,left);
   var turn=Quaternion.FromToRotation(right-left,rightTarget-leftTarget);
   frameAdjustment=gun.localToWorldMatrix*Matrix4x4.TRS(rightTarget,turn,Vector3.one*ratio)*Matrix4x4.Translate(-right)*gun.worldToLocalMatrix;
   rotation=frameAdjustment.rotation*baseRotation;scale*=ratio;
  }
  Matrix4x4 Frame()=>frameAdjustment*Matrix4x4.TRS(targetHand.position+Vector3.up*.008f+seat,baseRotation,Vector3.one*baseScale)*Matrix4x4.Translate(-hand.position);
  Vector3 Map(Vector3 p)=>Frame().MultiplyPoint3x4(p);
  if(key=="Viper-357_UE4")
  {
   var trigger=skins.SelectMany(s=>{var m=new Mesh();s.BakeMesh(m);var verts=m.vertices;var weights=s.sharedMesh.boneWeights;var result=Enumerable.Range(0,verts.Length).Where(i=>s.bones[weights[i].boneIndex0].name=="trigger").Select(i=>Map(Matrix4x4.TRS(s.transform.position,s.transform.rotation,Vector3.one).MultiplyPoint3x4(verts[i]))).ToArray();Object.DestroyImmediate(m);return result;}).ToArray();
   var nativeTrigger=nativePoints.First(p=>p.Key.name=="Trigger").Value;
   seat+=Center(nativeTrigger)-Center(trigger);
  }
  var groups=new Dictionary<Transform,List<Vector3>>();foreach(var s in skins){var m=new Mesh();s.BakeMesh(m);var v=m.vertices;var w=s.sharedMesh.boneWeights;for(int i=0;i<v.Length;i++){var b=s.bones[w[i].boneIndex0];if(!groups.TryGetValue(b,out var p))groups[b]=p=new();p.Add(Map(Matrix4x4.TRS(s.transform.position,s.transform.rotation,Vector3.one).MultiplyPoint3x4(v[i])));}Object.DestroyImmediate(m);}
  foreach(var filter in filters)groups[filter.transform]=filter.sharedMesh.vertices.Select(v=>Map(filter.transform.TransformPoint(v))).ToList();
  var mapping=new Dictionary<Transform,Transform>();var log=new List<string>{reference.weapon.displayName+" -> "+key,"Uniform model fit: "+scale.ToString("F4")};
  foreach(var group in groups)
  {
   string semantic=Semantic(group.Key.name);if(group.Key.name=="Bone.002")semantic="Mag";if(group.Key.name=="Bone.003")semantic="Bolt";if(group.Key.name=="Bone.004")semantic="ChargingHandle";Transform match=null;
   if(key=="MPS5_UE4"&&group.Key.name=="Bone.003")semantic="ChargingHandle";
   if(key=="MPS5_UE4"&&group.Key.name=="Bone.004")semantic="Bolt";
   string[] aliases=semantic=="Mag"?new[]{"Mag","Magazine","Bag"}:semantic=="Bolt"?new[]{"Bolt","ChargingHandle","Charger","Slider"}:semantic=="Root"?new[]{root.name}:new[]{semantic??""};
   foreach(string name in aliases){match=nativeBones.FirstOrDefault(b=>b.name==name);if(match)break;}
   if(!match)
   {var center=Center(group.Value);var candidates=nativePoints.Where(p=>p.Key!=root&&p.Value.Count>5&&!p.Key.name.StartsWith("Cartridge")&&!p.Key.name.StartsWith("Bullet")&&!p.Key.name.StartsWith("bullet")).ToArray();match=root;}
   // No spatial fallback for mechanisms: ammo must never drive a feed tray or lid.
   if(key=="MGX5_UE4"&&group.Key.name.StartsWith("Bone.")&&int.TryParse(group.Key.name.Substring(5),out int belt))match=nativeBones.FirstOrDefault(b=>b.name==(belt==6?"bullet":"bullet_"+((belt-6)/2).ToString("D3")))??root;
   if(key=="Striker-V_UE4"&&group.Key.name=="Bone.006")match=nativeBones.First(b=>b.name=="ActiveCharging");
   if(key=="Striker-V_UE4"&&(group.Key.name=="Bone.003"||group.Key.name=="Bone.004"))match=nativeBones.First(b=>b.name=="StaticCharging");
   if(group.Key.name.StartsWith("bullet_",StringComparison.OrdinalIgnoreCase)&&int.TryParse(group.Key.name.Split('_')[1].Split(' ')[0],out int bullet))
   {
    // Python and Viper enumerate their chambers from different starting angles.
    // This correspondence puts the insertion at the player-view lower-left hole.
    match=nativeBones.FirstOrDefault(b=>b.name=="Cartridge"+((bullet-1+2)%6))??match;
   }   if(key=="RPG7_UE4"&&(group.Key.name=="Bone.002"||group.Key.name.ToLowerInvariant().Contains("rocket")||source.weapon.loadedProjectile&&group.Key.IsChildOf(source.weapon.loadedProjectile.transform)))match=nativeBones.First(b=>b.name=="Rocket");
   mapping[group.Key]=match;log.Add(group.Key.name+" -> "+match.name);
  }
  void Socket(string name,Transform original)
  {
   if(!original)return;var node=new GameObject("Fitted"+name).transform;node.SetParent(root,false);
   node.SetPositionAndRotation(Map(original.position),rotation*original.rotation);
  }
  Socket("RearSight",source.weapon.rearSight);Socket("FrontSight",source.weapon.frontSight);Socket("OpticMount",source.weapon.opticMount);
  Socket("MuzzleFlash",source.weapon.effects.muzzleFlash);Socket("ShellEjection",source.weapon.effects.shellEjection);
  foreach(var mount in source.weapon.attachments)Socket(mount.kind+"Mount",mount.socket);
  // Keep the Polygon pivots. Rebinding directly to a native cylinder/lid pivot
  // makes the mesh orbit the wrong hinge during reload.
  var partRoot=new GameObject("PolygonParts").transform;partRoot.SetParent(gun,false);
  var polygonBones=groups.Keys.OrderBy(b=>AnimationUtility.CalculateTransformPath(b,source.weapon.transform).Count(c=>c=='/')).ToArray();
  var targetBones=new Dictionary<Transform,Transform>();var fit=new NativePartFit();
  foreach(var old in polygonBones)
  {
   var parent=old.parent;while(parent&&!targetBones.ContainsKey(parent))parent=parent.parent;
   var node=new GameObject(old.name).transform;node.SetParent(parent?targetBones[parent]:partRoot,false);node.SetPositionAndRotation(Map(old.position),rotation*old.rotation);targetBones[old]=node;
   fit.Add(mapping[old],node,gun,gun,parent&&mapping[parent]==mapping[old]);
  }
  var renderBones=polygonBones.Select(b=>targetBones[b]).ToArray();
  fit.ConfigureHand(fps,gun,nativePoints,groups,mapping,targetBones);
  var body=new Geometry();var projectile=new Geometry();

  foreach(var s in skins)
  {
   var mesh=new Mesh();s.BakeMesh(mesh);mesh.uv=s.sharedMesh.uv;mesh.boneWeights=s.sharedMesh.boneWeights;var mapped=(key=="RPG7_UE4"&&s.name.ToLowerInvariant().Contains("rocket"))?projectile:body;
   int B(int i)=>Mathf.Max(0,Array.IndexOf(polygonBones,s.bones[i]));
   BoneWeight W(BoneWeight w){w.boneIndex0=B(w.boneIndex0);w.boneIndex1=B(w.boneIndex1);w.boneIndex2=B(w.boneIndex2);w.boneIndex3=B(w.boneIndex3);return w;}
   var frame=gun.worldToLocalMatrix*Frame()*Matrix4x4.TRS(s.transform.position,s.transform.rotation,Vector3.one);
   mapped.Add(mesh,s.sharedMaterials,frame,W);Object.DestroyImmediate(mesh);
  }
  foreach(var filter in filters)
  {
   bool round=key=="RPG7_UE4"&&(filter.name.ToLowerInvariant().Contains("rocket")||source.weapon.loadedProjectile&&filter.transform.IsChildOf(source.weapon.loadedProjectile.transform));
   int index=Array.IndexOf(polygonBones,filter.transform);
   var frame=gun.worldToLocalMatrix*Frame()*filter.transform.localToWorldMatrix;
   (round?projectile:body).Add(filter.sharedMesh,filter.GetComponent<Renderer>().sharedMaterials,frame,_=>new BoneWeight{boneIndex0=index,weight0=1});
  }
  foreach(var renderer in gun.GetComponentsInChildren<Renderer>(true))Object.DestroyImmediate(renderer);
  body.Save(gun,renderBones,folder,"AdaptedPolygonWeapon");projectile.Save(gun,renderBones,folder,"LoadedRocket");File.WriteAllLines(folder+"/MeshFit.txt",log);return fit;
 }
}
