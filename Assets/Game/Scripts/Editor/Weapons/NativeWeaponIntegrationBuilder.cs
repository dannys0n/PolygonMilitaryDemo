using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

// Offline native UE4 authoring. One animator owns the fitted skin and gun parts.
public static class NativeWeaponIntegrationBuilder
{
 const string Root=PolygonWeaponConversionBuilder.Root;
 const string SourceCatalog="Assets/Game/AnimationTests/AnimationTestCatalog.asset";
 const string Production="Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset";
 public static readonly Dictionary<string,string> Matches=new()
 {
  {"legacy-svd","SVD_UE4"},{"polygon-rp9","PDW90_UE4"},{"polygon-submachine5","MPS5_UE4"},
  {"polygon-buzzsub","Striker-V_UE4"},{"polygon-revolver","Viper-357_UE4"},
  {"polygon-machinegun","MGX5_UE4"},{"polygon-rocket_launcher","RPG7_UE4"},{"military-rpg","RPG7_UE4"}
 };
 public static string[] NativeKeys=>AssetDatabase.LoadAssetAtPath<WeaponCatalog>(SourceCatalog).entries
  .Where(e=>e.weapon.catalogId.StartsWith("animation-test-")&&e.weapon.catalogId.EndsWith("_UE4"))
  .Select(e=>e.weapon.catalogId.Substring("animation-test-".Length)).ToArray();
 static T Save<T>(T value,string path)where T:Object
 {value.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){if(old is Mesh m)m.Clear(false);EditorUtility.CopySerialized(value,old);if(old is Mesh updated)updated.UploadMeshData(false);Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(value,path);return value;}
 static GameObject Clone(GameObject prefab)
 {var go=Object.Instantiate(prefab);if(PrefabUtility.IsPartOfPrefabInstance(go))PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);return go;}
 static Transform Find(GameObject go,string name)=>go.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
 static AnimationClip Clip(string key,string action)=>AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/AnimationTests/Clips/"+key+"_"+action+".anim");
 static WeaponCatalog.Entry Native(string key)=>AssetDatabase.LoadAssetAtPath<WeaponCatalog>(SourceCatalog).entries.First(e=>e.weapon.catalogId=="animation-test-"+key);
 static WeaponCatalog.Entry Reference(WeaponCatalog.Entry native)=>AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Production).entries.First(e=>e.weapon.firstPerson==native.weapon.firstPerson&&e.weapon.category==native.weapon.category);
 static void FittedArms(GameObject fps,bool originalProportions=false)
 {
  var arms=Find(fps,"SourceArms");var gun=Find(fps,"SourceWeapon");foreach(var skin in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>!s.transform.IsChildOf(gun)))Object.DestroyImmediate(skin);
  var accepted=AssetDatabase.LoadAssetAtPath<GameObject>(originalProportions?Ue4RigProofBuilder.OriginalPreviewRigPath:Ue4RigProofBuilder.PreviewRigPath).GetComponentInChildren<SkinnedMeshRenderer>(true);var node=new GameObject("PolygonArms_UE4Skin");node.transform.SetParent(arms,false);var fitted=node.AddComponent<SkinnedMeshRenderer>();var bones=arms.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());fitted.sharedMesh=accepted.sharedMesh;fitted.sharedMaterials=accepted.sharedMaterials;fitted.bones=accepted.bones.Select(b=>bones[b.name]).ToArray();fitted.rootBone=bones[accepted.rootBone.name];fitted.updateWhenOffscreen=true;fitted.localBounds=new Bounds(new Vector3(0,1.3f,0),Vector3.one*3);fitted.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
 }
 static bool Arm(string name)=>name.StartsWith("clavicle_")||name.StartsWith("upperarm_")||name.StartsWith("lowerarm_")||name.StartsWith("hand_")||new[]{"thumb_","index_","middle_","ring_","pinky_"}.Any(name.StartsWith);
 sealed class Tracks
 {
  readonly Dictionary<EditorCurveBinding,List<Keyframe>> data=new();
  public void Bone(Transform bone,Transform root,float time)
  {string path=AnimationUtility.CalculateTransformPath(bone,root);var p=bone.localPosition;var q=bone.localRotation;var s=bone.localScale;for(int i=0;i<10;i++){var b=EditorCurveBinding.FloatCurve(path,typeof(Transform),i<3?"m_LocalPosition."+"xyz"[i]:i<7?"m_LocalRotation."+"xyzw"[i-3]:"m_LocalScale."+"xyz"[i-7]);if(!data.TryGetValue(b,out var keys))data[b]=keys=new();keys.Add(new Keyframe(time,i<3?p[i]:i<7?q[i-3]:s[i-7]));}}
  public AnimationClip Finish(string folder,string name,bool loop)
  {var clip=new AnimationClip{frameRate=60};foreach(var pair in data){var keys=pair.Value;var c=new AnimationCurve(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<1e-6f)?new[]{keys.First(),keys.Last()}:keys.ToArray());for(int k=0;k<c.length;k++){AnimationUtility.SetKeyLeftTangentMode(c,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,pair.Key,c);}clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);return Save(clip,folder+"/"+name+".anim");}
 }
 sealed class Pose
 {
  readonly Transform[] bones;readonly Vector3[] p,s;readonly Quaternion[] q;
  public Pose(GameObject go){bones=go.GetComponentsInChildren<Transform>(true);p=bones.Select(t=>t.localPosition).ToArray();q=bones.Select(t=>t.localRotation).ToArray();s=bones.Select(t=>t.localScale).ToArray();}
  public void Restore(){for(int i=0;i<bones.Length;i++){bones[i].SetLocalPositionAndRotation(p[i],q[i]);bones[i].localScale=s[i];}}
 }
 static AnimationClip BakeFps(GameObject fps,string key,string action,string folder,WeaponCatalog.Entry reference,NativePartFit fit=null,float reloadTimeScale=1f)
 {
  bool ads=action=="AimFire";
  var input=Clip(key,ads?"Fire":action);if(!input)throw new Exception(key+" has no "+action);
  var arms=Find(fps,"SourceArms");var gun=Find(fps,"SourceWeapon");var pose=new Pose(fps);
  var sample=arms.GetComponentsInChildren<Transform>(true);var tracks=new Tracks();
  bool firing=action=="Fire"||ads,idle=action=="Idle";
  bool pythonAkRecoil=ads&&reference.weapon.catalogId=="polygon-revolver";
  bool polygonRecoil=pythonAkRecoil||reference.weapon.category==SoldierWeaponDefinition.WeaponCategory.Rifle
    ||reference.weapon.category==SoldierWeaponDefinition.WeaponCategory.SMG
    ||reference.weapon.category==SoldierWeaponDefinition.WeaponCategory.LMG||key=="SVD_UE4";
  Vector3 armPosition=new Vector3(0,-.1f+(reference.weapon.catalogId=="polygon-machinegun"?.035f:0),0);

  bool nativeAkAds=ads&&key.StartsWith("AKX",StringComparison.Ordinal);
  bool akRecoil=nativeAkAds||reference.animationStyle.StartsWith("AK");
  string recoilId=pythonAkRecoil||akRecoil?"legacy-ak12":reference.weapon.category==SoldierWeaponDefinition.WeaponCategory.Pistol?"legacy-mk23":"legacy-mk18";
  var recoilFolder=PolygonWeaponConversionBuilder.Folder(recoilId);var recoil=AssetDatabase.LoadAssetAtPath<AnimationClip>(recoilFolder+(nativeAkAds?"/AimFire_UE4.anim":"/Fire_UE4.anim"));var rest=AssetDatabase.LoadAssetAtPath<AnimationClip>(recoilFolder+"/Idle_UE4.anim");
  Vector3 P(AnimationClip c,float t)=>new Vector3(Read(c,"m_LocalPosition.x",t,0),Read(c,"m_LocalPosition.y",t,0),Read(c,"m_LocalPosition.z",t,0));
  Quaternion Q(AnimationClip c,float t)=>new Quaternion(Read(c,"m_LocalRotation.x",t,0),Read(c,"m_LocalRotation.y",t,0),Read(c,"m_LocalRotation.z",t,0),Read(c,"m_LocalRotation.w",t,1));
  float Read(AnimationClip c,string property,float time,float fallback)=>AnimationUtility.GetEditorCurve(c,EditorCurveBinding.FloatCurve("SourceWeapon",typeof(Transform),property))?.Evaluate(time)??fallback;
  float duration=idle?.1f:firing?Mathf.Max(input.length,recoil.length):input.length*Mathf.Max(.1f,reloadTimeScale);int frames=idle?1:Mathf.Max(1,Mathf.CeilToInt(duration*60));
  pose.Restore();Clip(key,"Idle").SampleAnimation(fps,0);arms.SetLocalPositionAndRotation(armPosition,Quaternion.Euler(0,180,0));fit?.Sample();var pivot=gun.position;
  var idleArmPose=sample.Where(t=>t!=arms&&(t==gun||!t.IsChildOf(gun))).ToDictionary(t=>t,t=>(t.localPosition,t.localRotation,t.localScale));
  var thumbCorrections=new List<(Transform joint,Quaternion correction)>();
  if(key=="Colibri_UE4")foreach(string side in new[]{"l","r"})
  {
   var joint=Find(fps,"thumb_01_"+side);var tip=Find(fps,"thumb_03_"+side);var point=gun.InverseTransformPoint(tip.position);point.y=Mathf.Min(point.y,-.030f);
   var turn=Quaternion.RotateTowards(Quaternion.identity,Quaternion.FromToRotation(tip.position-joint.position,gun.TransformPoint(point)-joint.position),35);
   var corrected=Quaternion.Inverse(joint.parent.rotation)*turn*joint.rotation;thumbCorrections.Add((joint,corrected*Quaternion.Inverse(joint.localRotation)));
  }
  var ammoRest=gun.GetComponentsInChildren<Transform>(true).Where(b=>b.name=="Clip"||b.name=="Cartridge").ToDictionary(b=>b,b=>(b.localPosition,b.localRotation));
  for(int f=0;f<=frames;f++)
  {
   float t=duration*f/frames;pose.Restore();input.SampleAnimation(fps,Mathf.Min(firing||idle?t:t/Mathf.Max(.1f,reloadTimeScale),input.length));arms.SetLocalPositionAndRotation(armPosition,Quaternion.Euler(0,180,0));
   if(firing&&polygonRecoil)foreach(var pair in idleArmPose){pair.Key.SetLocalPositionAndRotation(pair.Value.localPosition,pair.Value.localRotation);pair.Key.localScale=pair.Value.localScale;}
   if(firing){var delta=P(recoil,Mathf.Min(t,recoil.length))-P(rest,0);var rotation=Q(recoil,Mathf.Min(t,recoil.length))*Quaternion.Inverse(Q(rest,0));arms.SetPositionAndRotation(pivot+rotation*(arms.position-pivot)+delta,rotation*arms.rotation);}
   // The Unreal demo hides these reload props in its animation blueprint.
   // Bake visibility into the clip so the Unity rig needs no matching runtime logic.
   float phase=duration>0?t/duration:0;
   if(key=="Kar98K_UE4")foreach(var pair in ammoRest)
   {
    bool moving=Vector3.Distance(pair.Key.localPosition,pair.Value.localPosition)>.002f||Quaternion.Angle(pair.Key.localRotation,pair.Value.localRotation)>1;
    // The source parks loose rounds below the rifle between insertions.
    // A fitted hand's proportions must not decide whether its cartridge exists.
    bool held=pair.Key.localPosition.y>-.30f;
    bool visible=pair.Key.name=="Clip"?action=="Reload"&&moving&&held:(action=="Fire"||action=="Reload"&&held)&&moving;
    pair.Key.localScale=visible?Vector3.one:Vector3.zero;
   }
   if(key=="MGX5_UE4"&&action=="Reload"&&phase>.23f&&phase<.64f)
    foreach(var bone in gun.GetComponentsInChildren<Transform>(true).Where(b=>b.name=="bullet"||b.name.StartsWith("bullet_")))bone.localScale=Vector3.zero;

   float grip=action=="Reload"?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.12f,phase))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,.88f,phase)):1;
   foreach(var thumb in thumbCorrections)thumb.joint.localRotation=Quaternion.Slerp(Quaternion.identity,thumb.correction,grip)*thumb.joint.localRotation;
   fit?.Sample(!firing||!polygonRecoil);
   foreach(var bone in sample)tracks.Bone(bone,fps.transform,t);
  }
  pose.Restore();return tracks.Finish(folder,action+"_UE4",idle);
 }
 static List<Vector3> Points(Transform gun)
 {
  var points=new List<Vector3>();foreach(var skin in gun.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy)){var mesh=new Mesh();skin.BakeMesh(mesh);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;for(int i=0;i<vertices.Length;i++){string bone=weights.Length==vertices.Length?skin.bones[weights[i].boneIndex0].name.ToLowerInvariant():"";if(bone.Contains("bullet")||bone.Contains("cartridge")||bone=="clip"||bone=="rocket")continue;points.Add(Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one).MultiplyPoint3x4(vertices[i]));}Object.DestroyImmediate(mesh);}foreach(var filter in gun.GetComponentsInChildren<MeshFilter>(true))if(filter.TryGetComponent<Renderer>(out var renderer)&&renderer.enabled&&renderer.gameObject.activeInHierarchy)points.AddRange(filter.sharedMesh.vertices.Select(filter.transform.TransformPoint));return points;
 }
 static Transform Marker(Transform parent,string name,Vector3 worldPosition,Quaternion worldRotation)
 {var node=new GameObject(name).transform;node.SetParent(parent,false);node.SetPositionAndRotation(worldPosition,worldRotation);return node;}
 static void Sockets(SoldierWeaponView view,Transform gun,Transform hand,WeaponCatalog.Entry reference,string key)
 {
  var sourceRear=view.rearSight;var sourceFront=view.frontSight;var sourceOptic=view.opticMount;
  var points=Points(gun);if(points.Count==0)throw new Exception("No gun geometry");var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
  var tip=points.Where(p=>p.z>bounds.max.z-.025f).ToArray();var muzzle=tip.Aggregate(Vector3.zero,(s,p)=>s+p)/tip.Length;muzzle.z=bounds.max.z+.002f;
  bool pistol=reference.weapon.category==SoldierWeaponDefinition.WeaponCategory.Pistol;
  float x=muzzle.x+(key=="RPG7_UE4"?-.052f:0);float rearZ=Mathf.Clamp(hand.position.z+(pistol?.095f:.14f),bounds.min.z+.035f,bounds.max.z-.1f);float frontZ=Mathf.Lerp(rearZ,bounds.max.z,.80f);
  // Locate the physical posts near the receiver and barrel; ammunition geometry
  // is excluded above, so a floating reload prop cannot become the sight line.
  Vector3 Post(float z,float width){var band=points.Where(p=>Mathf.Abs(p.x-x)<.018f&&Mathf.Abs(p.z-z)<width).ToArray();if(band.Length==0)return new Vector3(x,bounds.max.y,z);float top=band.Max(p=>p.y);var cap=band.Where(p=>p.y>top-.004f).ToArray();return new Vector3(x,top-.004f,cap.Average(p=>p.z));}
  var rear=Post(rearZ,pistol?.035f:.065f);var front=Post(frontZ,pistol?.035f:.075f);rearZ=rear.z;frontZ=front.z;
  float Height(float z){var band=points.Where(p=>Mathf.Abs(p.x-x)<.027f&&Mathf.Abs(p.z-z)<.055f).Select(p=>p.y).OrderBy(y=>y).ToArray();return band.Length>0?band.Last():bounds.max.y;}
  float sightY=Mathf.Max(Height(rearZ),Height(frontZ))+.002f;
  var socketRoot=Marker(gun,"AttachmentSockets",gun.position,Quaternion.identity);var bodyBone=gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Root"||t.name=="Body");if(bodyBone)socketRoot.SetParent(bodyBone,true);
  view.effects=view.gameObject.AddComponent<SoldierWeaponSockets>();view.effects.muzzleFlash=Marker(socketRoot,"MuzzleFlash",muzzle,Quaternion.identity);
  view.effects.shellEjection=Marker(socketRoot,"ShellEjection",new Vector3(x+Mathf.Max(.025f,bounds.size.x*.22f),Mathf.Min(sightY-.035f,bounds.max.y),rearZ+.075f),Quaternion.LookRotation(Vector3.right,Vector3.up));
  view.rearSight=Marker(socketRoot,"RearSight",rear,Quaternion.identity);view.frontSight=Marker(socketRoot,"FrontSight",front,Quaternion.identity);
  float receiverZ=Mathf.Clamp(rearZ+.08f,bounds.min.z+.12f,bounds.max.z-.15f);
  var top=points.Where(p=>Mathf.Abs(p.x-x)<.025f&&Mathf.Abs(p.z-receiverZ)<.06f).Select(p=>p.y).OrderBy(y=>y).ToArray();float railY=top.Length>0?top.GroupBy(y=>Mathf.RoundToInt(y/.002f)).OrderByDescending(g=>g.Count()).First().Average():sightY-.025f;
  view.opticMount=Marker(socketRoot,"OpticMount",new Vector3(x,railY+.001f,receiverZ),Quaternion.identity);
  // Native source prefabs own their editable geometry-calibrated references.
  // Polygon adapters use their own source weapon's fitted markers below.
  if(!gun.Find("PolygonParts"))
  {
   var authored=gun.Find("SightReferences");
   if(authored){var r=sourceRear?sourceRear:authored.Find("RearSight");var f=sourceFront?sourceFront:authored.Find("FrontSight");var o=sourceOptic?sourceOptic:authored.Find("OpticMount");if(r)view.rearSight.SetPositionAndRotation(r.position,r.rotation);if(f)view.frontSight.SetPositionAndRotation(f.position,f.rotation);if(o)view.opticMount.SetPositionAndRotation(o.position,o.rotation);}
  }
  Transform Fitted(string name)=>gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Fitted"+name);
  void Restore(Transform socket,string name){var authored=Fitted(name);if(authored)socket.SetPositionAndRotation(authored.position,authored.rotation);}
  Restore(view.rearSight,"RearSight");Restore(view.frontSight,"FrontSight");Restore(view.opticMount,"OpticMount");Restore(view.effects.muzzleFlash,"MuzzleFlash");Restore(view.effects.shellEjection,"ShellEjection");
  var optic=view.opticMount.gameObject.AddComponent<WeaponAttachmentSocket>();optic.maximumSightFootprint=bounds.size.z<.55f?WeaponSightDefinition.RailFootprint.Compact:WeaponSightDefinition.RailFootprint.Long;
  if(reference.firstPersonPrefab.opticMount&&reference.firstPersonPrefab.opticMount.TryGetComponent<WeaponAttachmentSocket>(out var original))
  {optic.supportedSightMounts=original.supportedSightMounts;optic.builtInSight=original.builtInSight;optic.placements=(WeaponAttachmentSocket.Placement[])original.placements.Clone();}
  if(!gun.Find("PolygonParts")&&sourceOptic&&sourceOptic.TryGetComponent<WeaponAttachmentSocket>(out var nativeSocket))
  {optic.supportedSightMounts=nativeSocket.supportedSightMounts;optic.maximumSightFootprint=nativeSocket.maximumSightFootprint;optic.placements=(WeaponAttachmentSocket.Placement[])nativeSocket.placements.Clone();}

  view.attachments=Enum.GetValues(typeof(WeaponAttachmentDefinition.MountKind)).Cast<WeaponAttachmentDefinition.MountKind>().Select(kind=>
  {Vector3 p=kind==WeaponAttachmentDefinition.MountKind.Muzzle?muzzle:kind==WeaponAttachmentDefinition.MountKind.RearIron?view.rearSight.position:kind==WeaponAttachmentDefinition.MountKind.FrontIron?view.frontSight.position:kind==WeaponAttachmentDefinition.MountKind.Grip?new Vector3(x,muzzle.y-.025f,receiverZ+.17f):view.opticMount.position;
   if(kind==WeaponAttachmentDefinition.MountKind.Rail||kind==WeaponAttachmentDefinition.MountKind.RailLeft)p=new Vector3(x+(kind==WeaponAttachmentDefinition.MountKind.Rail?.035f:-.035f),muzzle.y,receiverZ+.18f);
   var socket=Marker(socketRoot,kind+"Mount",p,Quaternion.identity);Restore(socket,kind+"Mount");socket.gameObject.AddComponent<WeaponAttachmentSocket>();return new SoldierWeaponView.AttachmentSocket{kind=kind,socket=socket,builtIn=Array.Empty<Renderer>()};}).ToArray();
  // Pistol sights follow the slide, while the Desert Eagle front sight stays on its fixed barrel.
  if(!gun.Find("PolygonParts")&&(key=="X18_UE4"||key=="M1911_UE4"||key=="DGL50_UE4"))
  {
   var slide=gun.Find("Root/Slider");if(slide){view.rearSight.SetParent(slide,true);if(key!="DGL50_UE4")view.frontSight.SetParent(slide,true);
    foreach(var a in view.attachments.Where(a=>a.kind==WeaponAttachmentDefinition.MountKind.RearIron||a.kind==WeaponAttachmentDefinition.MountKind.FrontIron&&key!="DGL50_UE4"))a.socket.SetParent(slide,true);}
  }
  // An optic and rear iron belong to the feed-cover rail, not the receiver.
  // Parenting to the physical mechanism also follows authored reload motion in TPS.
  if(key=="MGX5_UE4")
  {
   var rail=gun.Find("PolygonParts/main/open")??gun.Find("Root/Top");
   if(rail)
   {
    view.opticMount.SetParent(rail,true);view.rearSight.SetParent(rail,true);
    foreach(var socket in view.attachments.Where(a=>a.kind==WeaponAttachmentDefinition.MountKind.RearIron||a.kind==WeaponAttachmentDefinition.MountKind.SightRiser))socket.socket.SetParent(rail,true);
   }
  }

 }
 static AnimationClip Parts(AnimationClip input,string inputRoot,string folder,string name)
 {
  var clip=new AnimationClip{frameRate=input.frameRate};foreach(var b in AnimationUtility.GetCurveBindings(input).Where(b=>b.path.StartsWith(inputRoot+"/"))){var target=b;target.path="SourceWeapon"+b.path.Substring(inputRoot.Length);AnimationUtility.SetEditorCurve(clip,target,AnimationUtility.GetEditorCurve(input,b));}clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=name.Contains("Idle");AnimationUtility.SetAnimationClipSettings(clip,settings);return Save(clip,folder+"/"+name+".anim");
 }
 static Sprite Icon(GameObject model,string folder)
 {
  var layers=model.GetComponentsInChildren<Transform>(true).ToArray();var originalLayers=layers.Select(t=>t.gameObject.layer).ToArray();foreach(var t in layers)t.gameObject.layer=31;
  var points=Points(Find(model,"SourceWeapon"));var b=new Bounds(points[0],Vector3.zero);foreach(var p in points)b.Encapsulate(p);
  var node=new GameObject("Weapon icon camera");var camera=node.AddComponent<Camera>();camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=Mathf.Max(b.size.y*.57f,b.size.z*.28f);camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.transform.position=b.center+Vector3.left*2;camera.transform.LookAt(b.center);var rt=new RenderTexture(512,256,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;var tex=new Texture2D(512,256,TextureFormat.RGBA32,false);
  try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,512,256),0,0);var pixels=tex.GetPixels32();for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,255,255,pixels[i].a);tex.SetPixels32(pixels);tex.Apply();string path=folder+"/Icon.png";File.WriteAllBytes(path,tex.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);}
  finally{camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);Object.DestroyImmediate(node);for(int i=0;i<layers.Length;i++)layers[i].gameObject.layer=originalLayers[i];}
 }
 static GameObject Rocket(Transform gun,string folder)
 {
  var existing=gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="LoadedRocket");if(existing)return existing.gameObject;
  var rocket=gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Rocket");if(!rocket)return null;
  GameObject result=null;foreach(var skin in gun.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToArray())
  {
   var src=skin.sharedMesh;var weights=src.boneWeights;bool Round(int v)=>skin.bones[weights[v].boneIndex0]==rocket||skin.bones[weights[v].boneIndex0].IsChildOf(rocket);
   var a=Object.Instantiate(src);var b=Object.Instantiate(src);int count=0;
   for(int sub=0;sub<src.subMeshCount;sub++){var body=new List<int>();var round=new List<int>();var tris=src.GetTriangles(sub);for(int i=0;i<tris.Length;i+=3){var list=Round(tris[i])&&Round(tris[i+1])&&Round(tris[i+2])?round:body;list.Add(tris[i]);list.Add(tris[i+1]);list.Add(tris[i+2]);}count+=round.Count;a.SetTriangles(body,sub);b.SetTriangles(round,sub);}
   if(count==0){Object.DestroyImmediate(a);Object.DestroyImmediate(b);continue;}
   skin.sharedMesh=Save(a,folder+"/LauncherBody.asset");var node=new GameObject("LoadedRocket");node.transform.SetParent(skin.transform.parent,false);node.transform.SetLocalPositionAndRotation(skin.transform.localPosition,skin.transform.localRotation);node.transform.localScale=skin.transform.localScale;var copy=node.AddComponent<SkinnedMeshRenderer>();copy.sharedMesh=Save(b,folder+"/LoadedRocket.asset");copy.sharedMaterials=skin.sharedMaterials;copy.bones=skin.bones;copy.rootBone=skin.rootBone;copy.localBounds=skin.localBounds;copy.updateWhenOffscreen=true;result=node;
  }
  return result;
 }
 static AnimatorController PartsController(string folder,AnimationClip idle,AnimationClip fire,AnimationClip reload)
 {
  string path=folder+"/Parts.controller";var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);if(!c)c=AnimatorController.CreateAnimatorControllerAtPath(path);else{c.layers=Array.Empty<AnimatorControllerLayer>();c.parameters=Array.Empty<AnimatorControllerParameter>();foreach(var a in AssetDatabase.LoadAllAssetsAtPath(path).Where(a=>a&&a!=c))Object.DestroyImmediate(a,true);c.AddLayer("Base Layer");}
  c.AddParameter("Fire",AnimatorControllerParameterType.Trigger);c.AddParameter("Reload",AnimatorControllerParameterType.Trigger);var sm=c.layers[0].stateMachine;var i=sm.AddState("Idle");i.motion=idle;sm.defaultState=i;foreach(var pair in new[]{("Fire",fire),("Reload",reload)}){var s=sm.AddState(pair.Item1);s.motion=pair.Item2;var t=sm.AddAnyStateTransition(s);t.hasExitTime=false;t.duration=0;t.canTransitionToSelf=pair.Item1=="Fire";t.AddCondition(AnimatorConditionMode.If,0,pair.Item1);t=s.AddTransition(i);t.hasExitTime=true;t.exitTime=1;t.duration=.04f;}return c;
 }
 static void Data(GameObject tps,SoldierWeaponView view,SoldierWeaponDefinition definition,WeaponCatalog.Entry reference,SoldierWeaponView fps)
 {
  var weapon=tps.AddComponent<SoldierWeapon>();EditorUtility.CopySerialized(reference.thirdPersonPrefab.GetComponent<SoldierWeapon>(),weapon);weapon.definition=definition;
  var sights=tps.AddComponent<SoldierWeaponSights>();sights.defaultSight=reference.sights.First(s=>s&&!s.prefab);sights.compatibleSights=reference.sights;sights.rearSight=view.rearSight;sights.frontSight=view.frontSight;sights.thirdPersonMount=view.opticMount;sights.referenceCameraRotation=sights.neutralCameraRotation=Quaternion.identity;sights.cameraReferenceConfigured=true;
  var attachments=tps.AddComponent<SoldierWeaponAttachments>();attachments.mounts=view.attachments.Select(a=>new SoldierWeaponAttachments.Mount{kind=a.kind,thirdPerson=a.socket,compatible=reference.attachments.Where(d=>d&&d.mount==a.kind).ToArray(),builtInFirstPerson=Array.Empty<Renderer>(),builtInThirdPerson=a.builtIn??Array.Empty<Renderer>()}).ToArray();
  Mk18GameplayBuilder.CalibrateSights(tps,fps);
 }
 static void Body(WeaponAnimationConversionProof proof,GameObject fps,string folder,Transform fpsGun)
 {
  var body=Clone(AssetDatabase.LoadAssetAtPath<GameObject>(PolygonWeaponConversionBuilder.BodyTemplate));body.name=proof.converted.weapon.displayName+"_Body";
  try
  {
   body.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);foreach(var a in body.GetComponentsInChildren<Animator>(true))Object.DestroyImmediate(a);
   var arms=Find(body,"SourceArms");var skin=body.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.bones.Any(b=>b.name=="hand_r"));var bind=skin.sharedMesh.bindposes;
   foreach(int b in Enumerable.Range(0,skin.bones.Length).OrderBy(i=>AnimationUtility.CalculateTransformPath(skin.bones[i],arms).Count(c=>c=='/'))){var frame=skin.transform.localToWorldMatrix*bind[b].inverse;skin.bones[b].SetPositionAndRotation(frame.GetColumn(3),frame.rotation);}
   var gun=Clone(fpsGun.gameObject);gun.name="SourceWeapon";gun.transform.SetParent(body.transform,false);
   if(proof.converted.defaultSight&&proof.converted.defaultSight.prefab){var mount=gun.transform.Find(AnimationUtility.CalculateTransformPath(fps.GetComponent<SoldierWeaponView>().opticMount,fpsGun));if(mount){var optic=Clone(proof.converted.defaultSight.prefab.gameObject);optic.name="DefaultSightPreview";optic.transform.SetParent(mount,false);optic.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);optic.transform.localScale=proof.converted.defaultSight.prefab.transform.localScale;mount.GetComponent<WeaponAttachmentSocket>().Place(proof.converted.defaultSight,optic.transform);}}
   var fpsView=fps.GetComponent<SoldierWeaponView>();
   var sightPaths=new[]{fpsView.rearSight,fpsView.frontSight,fpsView.opticMount}.Where(t=>t).Select(t=>AnimationUtility.CalculateTransformPath(t,fpsGun)).ToHashSet();
   var target=body.GetComponentsInChildren<Transform>(true).Where(t=>t.IsChildOf(arms)&&Arm(t.name)).OrderBy(t=>AnimationUtility.CalculateTransformPath(t,arms).Count(c=>c=='/')).ToArray();var source=fps.GetComponentsInChildren<Transform>(true).Where(t=>t.IsChildOf(Find(fps,"SourceArms"))).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());var standing=new Pose(body);
   foreach(string action in new[]{"Idle","Reload"})
   {
    var input=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Gameplay_"+action+"_UE4.anim");var tracks=new Tracks();int frames=action=="Idle"?1:Mathf.CeilToInt(input.length*60);
    string gunPrefix=AnimationUtility.CalculateTransformPath(fpsGun,fps.transform)+"/";
    var animatedParts=AnimationUtility.GetCurveBindings(input).Where(b=>b.type==typeof(Transform)&&b.path.StartsWith(gunPrefix)).Select(b=>b.path.Substring(gunPrefix.Length)).Distinct().Select(path=>(source:fpsGun.Find(path),target:gun.transform.Find(path))).Where(p=>p.source&&p.target).ToArray();
    for(int f=0;f<=frames;f++)
    {float time=input.length*f/frames;standing.Restore();input.SampleAnimation(fps,time);foreach(var t in target)t.rotation=source[t.name].rotation;
     var offset=Find(body,"hand_r").position-source["hand_r"].position;gun.transform.SetPositionAndRotation(fpsGun.position+offset,fpsGun.rotation);gun.transform.localScale=fpsGun.lossyScale;
     foreach(var part in animatedParts){part.target.SetLocalPositionAndRotation(part.source.localPosition,part.source.localRotation);part.target.localScale=part.source.localScale;}
     foreach(var t in target)tracks.Bone(t,body.transform,time);foreach(var t in gun.GetComponentsInChildren<Transform>(true).Where(t=>!sightPaths.Contains(AnimationUtility.CalculateTransformPath(t,gun.transform))))tracks.Bone(t,body.transform,time);
    }
    var clip=tracks.Finish(folder,"TPS_"+action+"_UE4",action=="Idle");if(action=="Idle")proof.bodyIdle=clip;else proof.bodyReload=clip;
   }
   proof.bodyIdle.SampleAnimation(body,0);float floor=skin.bounds.min.y;if(float.IsFinite(floor))body.transform.position+=Vector3.up*(-.03f-floor);foreach(var c in body.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(c);proof.sharedBodyPrefab=null;proof.sharedBodyWeaponPrefab=null;proof.thirdPersonProof=PrefabUtility.SaveAsPrefabAsset(body,proof.thirdPersonProof?AssetDatabase.GetAssetPath(proof.thirdPersonProof):folder+"/Body.prefab");
  }
  finally{Object.DestroyImmediate(body);}
 }
 static void StripUnusedPartControls(GameObject fps,params AnimationClip[] clips)
 {
  var gun=Find(fps,"SourceWeapon");var root=gun.Cast<Transform>().FirstOrDefault(t=>t.name=="Root"||t.name=="Body");
  if(root)foreach(var child in root.Cast<Transform>().Where(t=>!t.name.StartsWith("Fitted")).ToArray())Object.DestroyImmediate(child.gameObject);
  foreach(var child in gun.Cast<Transform>().Where(t=>t!=root&&t.name!="PolygonParts"&&!t.GetComponent<Renderer>()).ToArray())Object.DestroyImmediate(child.gameObject);
  var paths=fps.GetComponentsInChildren<Transform>(true).Select(t=>AnimationUtility.CalculateTransformPath(t,fps.transform)).ToHashSet();
  foreach(var clip in clips)foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>!paths.Contains(b.path)))AnimationUtility.SetEditorCurve(clip,binding,null);
 }
 public static object Build(string key,string polygonId=null,bool originalProportions=false,float reloadTimeScale=1f)
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)throw new Exception("Build in Edit Mode after compilation");
  string promotedId=polygonId==null?"ue4-original-proportions-"+key.ToLowerInvariant():"ue4-original-proportions-"+polygonId+"-polygon-ue4";
  var promoted=AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Production).entries.FirstOrDefault(e=>e.polished&&e.weapon.catalogId==promotedId);
  if(promoted!=null)throw new InvalidOperationException("This weapon is promoted. Edit its production definition, prefabs and animation assets directly: "+AssetDatabase.GetAssetPath(promoted.weapon));
  if(polygonId=="polygon-machinegun")M249PartsBuilder.PrepareFeedTray();
  var native=Native(key);var reference=polygonId==null?Reference(native):AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Production).entries.First(e=>e.weapon.catalogId==polygonId);
  if(originalProportions&&polygonId==null)throw new ArgumentException("A source Polygon weapon is required for a new scaled adaptation.");
  string folder=originalProportions?Root+"/OriginalProportions/"+polygonId+"-polygon-ue4":polygonId==null?Root+"/Native/"+key:PolygonWeaponConversionBuilder.Folder(polygonId);Directory.CreateDirectory(folder);AssetDatabase.Refresh();
  var fitted=AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Game/AnimationTests/UE4RigProof/UE4RigProofCatalog.asset").entries.First(e=>e.weapon.catalogId=="ue4-rig-proof-"+(key=="MGX5_UE4"?"mg5":key.ToLowerInvariant()));
  var previous=AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(folder+"/Conversion.asset");var fps=Clone(native.firstPersonPrefab.gameObject);GameObject tps=null;
  try
  {
   fps.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);foreach(var a in fps.GetComponentsInChildren<Animator>(true))Object.DestroyImmediate(a);var view=fps.GetComponent<SoldierWeaponView>();if(view.effects)Object.DestroyImmediate(view.effects);foreach(var script in fps.GetComponents<SoldierAuthoredWeaponMotion>())Object.DestroyImmediate(script);FittedArms(fps,originalProportions);
   foreach(Transform child in fps.transform.Cast<Transform>().ToArray())if(child.name!="SourceArms")Object.DestroyImmediate(child.gameObject);
   var fit=polygonId!=null?NativeWeaponMeshAdapter.Apply(fps,reference,key,folder):null;
   var idle=BakeFps(fps,key,"Idle",folder,reference,fit);var reload=BakeFps(fps,key,"Reload",folder,reference,fit,reloadTimeScale);var fire=BakeFps(fps,key,"Fire",folder,reference,fit);var aimFire=key.StartsWith("AKX",StringComparison.Ordinal)||reference.weapon.catalogId=="polygon-revolver"?BakeFps(fps,key,"AimFire",folder,reference,fit):fire;if(fit!=null)StripUnusedPartControls(fps,idle,reload,fire,aimFire);idle.SampleAnimation(fps,0);var gun=Find(fps,"SourceWeapon");var hand=Find(fps,"hand_r");if(key=="RPG7_UE4")view.loadedProjectile=Rocket(gun,folder);
   view.animator=fps.AddComponent<Animator>();view.animationOverride=Mk18GameplayBuilder.Controller(reference.weapon,idle,reload,fire,aimFire,folder,true,SharedAdsRecoil.Supports(polygonId==null?native.weapon:reference.weapon));view.animator.runtimeAnimatorController=view.animationOverride;view.mountPosition=new Vector3(0,-1.55f,0);view.mountRotation=Quaternion.identity;view.mountScale=Vector3.one;view.independentFirstPersonFrame=true;view.grenadeView=fitted.firstPersonPrefab.grenadeView;Sockets(view,gun,hand,reference,key);WeaponIronParts.Configure(view,polygonId??key,folder);Mk18GameplayBuilder.PrepareView(view);view.sightPlacementRoot=gun;
   var definition=Object.Instantiate(polygonId==null?native.weapon:reference.weapon);definition.catalogId=originalProportions?"ue4-original-proportions-"+polygonId+"-polygon-ue4":polygonId==null?fitted.weapon.catalogId:polygonId+"-polygon-ue4";definition.displayName=polygonId==null?native.weapon.displayName:reference.weapon.displayName+" (Polygon UE4)";if(originalProportions)definition.displayName=reference.weapon.displayName+" (Scaled)";definition.showFirstPersonHands=originalProportions;definition.reloadDuration=reload.length;if(polygonId==null)AnimationTestBuilder.ConfigureRoundReload(definition,key.Replace("_UE4",""));definition.useAuthoredReload=true;definition.cameraPositionWeight=reference.weapon.cameraPositionWeight;definition.cameraRotationWeight=reference.weapon.cameraRotationWeight;definition.viewOffset=definition.hipFireOffset=definition.aimOffset=definition.viewRotationOffset=definition.hipFireRotationOffset=definition.aimRotationOffset=Vector3.zero;if(key=="Viper-357_UE4")definition.shellCasing=null;definition=Save(definition,previous?AssetDatabase.GetAssetPath(previous.converted.weapon):polygonId==null?AssetDatabase.GetAssetPath(fitted.weapon):folder+"/Weapon.asset");
   tps=new GameObject(definition.displayName+"_TPS");var targetGun=Clone(gun.gameObject);targetGun.name="SourceWeapon";targetGun.transform.SetParent(tps.transform,false);var grip=reference.thirdPersonPrefab.GetComponent<SoldierWeapon>().thirdPersonHands.rightGrip.position;var handInGun=gun.InverseTransformPoint(hand.position);targetGun.transform.localRotation=gun.rotation;targetGun.transform.localScale=gun.lossyScale;targetGun.transform.localPosition=grip-targetGun.transform.localRotation*Vector3.Scale(targetGun.transform.localScale,handInGun);
   var tp=tps.AddComponent<SoldierWeaponView>();tp.mountPosition=reference.thirdPersonPrefab.mountPosition;tp.mountRotation=reference.thirdPersonPrefab.mountRotation;tp.mountScale=reference.thirdPersonPrefab.mountScale;
   Transform Remap(Transform s)=>targetGun.transform.Find(AnimationUtility.CalculateTransformPath(s,gun));tp.opticMount=Remap(view.opticMount);tp.rearSight=Remap(view.rearSight);tp.frontSight=Remap(view.frontSight);if(view.loadedProjectile)tp.loadedProjectile=Remap(view.loadedProjectile.transform).gameObject;tp.effects=tps.AddComponent<SoldierWeaponSockets>();tp.effects.muzzleFlash=Remap(view.effects.muzzleFlash);tp.effects.shellEjection=Remap(view.effects.shellEjection);tp.attachments=view.attachments.Select(a=>new SoldierWeaponView.AttachmentSocket{kind=a.kind,socket=Remap(a.socket),builtIn=(a.builtIn??Array.Empty<Renderer>()).Select(r=>Remap(r.transform).GetComponent<Renderer>()).ToArray()}).ToArray();
   string gunPath=AnimationUtility.CalculateTransformPath(gun,fps.transform); // raw clips precede the motion-carrier prefix.
   gunPath=gunPath.Substring("ViewMotion/WeaponMotion/".Length);
   var parts=PartsController(folder,Parts(idle,gunPath,folder,"PartsIdle"),Parts(fire,gunPath,folder,"PartsFire"),Parts(reload,gunPath,folder,"PartsReload"));definition.firstPersonParts=parts;tp.animator=tps.AddComponent<Animator>();tp.animator.runtimeAnimatorController=parts;Data(tps,tp,definition,reference,view);
   if(originalProportions&&reference.defaultSight)tps.GetComponent<SoldierWeaponSights>().defaultSight=reference.defaultSight;
   // Native models can supply removable factory optics and their own mount-compatible choices.
   if(polygonId==null){var choices=tps.GetComponent<SoldierWeaponSights>();choices.compatibleSights=reference.sights.Where(s=>s&&!s.prefab).Concat(native.sights.Where(s=>s&&s.prefab)).Distinct().ToArray();if(native.defaultSight&&native.defaultSight.prefab&&choices.Supports(native.defaultSight))choices.defaultSight=native.defaultSight;}
   string fpPath=previous?AssetDatabase.GetAssetPath(previous.converted.firstPersonPrefab):polygonId==null?AssetDatabase.GetAssetPath(fitted.firstPersonPrefab):folder+"/FPS.prefab";string tpPath=previous?AssetDatabase.GetAssetPath(previous.converted.thirdPersonPrefab):polygonId==null?AssetDatabase.GetAssetPath(fitted.thirdPersonPrefab):folder+"/TPS.prefab";
   var fp=PrefabUtility.SaveAsPrefabAsset(fps,fpPath).GetComponent<SoldierWeaponView>();var third=PrefabUtility.SaveAsPrefabAsset(tps,tpPath).GetComponent<SoldierWeaponView>();if(polygonId==null)definition.icon=Icon(tps,folder);EditorUtility.SetDirty(definition);
   var proof=previous?Object.Instantiate(previous):ScriptableObject.CreateInstance<WeaponAnimationConversionProof>();proof.originalProportions=originalProportions;proof.source=polygonId==null?native:reference;proof.converted=new WeaponCatalog.Entry{weapon=definition,basePrefab=third.gameObject,firstPersonPrefab=fp,thirdPersonPrefab=third,polished=false,sights=tps.GetComponent<SoldierWeaponSights>().compatibleSights,attachments=reference.attachments,defaultSight=tps.GetComponent<SoldierWeaponSights>().defaultSight,animationStyle="Native UE4 "+key+" / fitted Polygon skin / authored idle hip pose"};proof.idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Gameplay_"+idle.name+".anim");proof.reload=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Gameplay_"+reload.name+".anim");proof.fire=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Gameplay_"+fire.name+".anim");proof.aimFire=WeaponAnimationSource.State(view.animationOverride,"Base Layer","Fire",1);
   if(!originalProportions)Body(proof,fps,folder,gun);proof=Save(proof,folder+"/Conversion.asset");if(polygonId==null){var review=AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Game/AnimationTests/UE4RigProof/UE4RigProofCatalog.asset");review.entries=review.entries.Select(e=>e.weapon.catalogId==definition.catalogId?proof.converted:e).ToArray();EditorUtility.SetDirty(review);}AssetDatabase.SaveAssets();return new{key,polygonId,folder,reload=reload.length};
  }
  finally{Object.DestroyImmediate(fps);if(tps)Object.DestroyImmediate(tps);}
 }
}
