using System;using System.Linq;using System.Collections.Generic;using System.IO;using UnityEditor;using UnityEditor.Animations;using UnityEngine;using UnityEngine.Animations.Rigging;using Object=UnityEngine.Object;
// Offline source-to-Polygon baking. Runtime remains the existing infantry animation graph.
public static class ArenaPolygonAnimationBuilder
{
 const string Root="Assets/Game/AnimationTests/Polygon";
 const string Source="Assets/Game/AnimationTests";
 static ArenaWeaponCatalog original,tests;static readonly List<string> report=new();
 static readonly Dictionary<string,string> Match=new(){
 {"PDW90_UE4","polygon-rp9"},{"RPG7_UE4","polygon-rocket_launcher"},{"Striker-V_UE4","polygon-buzzsub"},{"SVD_UE4","legacy-svd"},{"Val_UE4","legacy-ak"},{"Viper-357_UE4","polygon-revolver"},{"X18_UE4","polygon-lock"},{"AKX12_UE4","legacy-ak12"},{"AKX200_UE4","polygon-assault57"},{"Colibri_UE4","polygon-pistol9"},{"DGL50_UE4","polygon-nsp"},{"Drake-12_UE4","polygon-clustershotgun"},{"G3_UE4","legacy-fal"},{"Kar98K_UE4","polygon-hunterrifle"},{"KXG12_UE4","polygon-shotgun3"},{"L96X_UE4","polygon-hunterrifle"},{"M14_UE4","legacy-fal"},{"M1911_UE4","polygon-old_pistol"},{"MGX5_UE4","polygon-machinegun"},{"Mk14EBR_UE4","legacy-fal"},{"MPS5_UE4","polygon-submachine5"},{"MX16A4_UE4","polygon-mkb16"}};
 static Transform Find(GameObject root,string n)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==n);
 static T Save<T>(T asset,string path)where T:Object{Directory.CreateDirectory(Path.GetDirectoryName(path));var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(asset,path);return asset;}
 static GameObject Clone(GameObject prefab){var go=Object.Instantiate(prefab);if(PrefabUtility.IsPartOfPrefabInstance(go))PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);return go;}
 static AnimationClip Clip(string key,string action)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(Source+"/Clips/"+key+"_"+action+".anim");
 static AnimationClip Idle(RuntimeAnimatorController c)=>c.animationClips.FirstOrDefault(c=>c.name.EndsWith("Idle_Baked")||c.name.Contains("Idle_Pose"))??c.animationClips.First(c=>c.name.Contains("Idle"));
 sealed class Pose
 {
  readonly Transform[] bones;readonly Vector3[] p,s;readonly Quaternion[] q;
  public Pose(GameObject go){bones=go.GetComponentsInChildren<Transform>(true);p=bones.Select(t=>t.localPosition).ToArray();q=bones.Select(t=>t.localRotation).ToArray();s=bones.Select(t=>t.localScale).ToArray();}
  public void Restore(){for(int i=0;i<bones.Length;i++){bones[i].localPosition=p[i];bones[i].localRotation=q[i];bones[i].localScale=s[i];}}
 }
 sealed class Curves
 {
  readonly Dictionary<EditorCurveBinding,List<Keyframe>> data=new();
  public void Value(string path,Type type,string field,float time,float value){if(!float.IsFinite(value))throw new InvalidOperationException("Invalid baked pose");var b=EditorCurveBinding.FloatCurve(path,type,field);if(!data.TryGetValue(b,out var keys))data[b]=keys=new();if(keys.Count>0&&Mathf.Approximately(keys[keys.Count-1].time,time))keys[keys.Count-1]=new Keyframe(time,value);else keys.Add(new Keyframe(time,value));}
  public void Bone(Transform t,Transform root,float time){string path=AnimationUtility.CalculateTransformPath(t,root);var p=t.localPosition;var q=t.localRotation;string[] f={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int i=0;i<7;i++)Value(path,typeof(Transform),f[i],time,v[i]);}
  public AnimationClip Finish(string name,bool loop){var clip=new AnimationClip{name=name,frameRate=30};var bindings=data.Keys.ToArray();var curves=bindings.Select(b=>{var keys=data[b];var c=new AnimationCurve(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<.000001f)?new[]{keys[0],keys[keys.Count-1]}:keys.ToArray());for(int k=0;k<c.length;k++){AnimationUtility.SetKeyLeftTangentMode(c,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,k,AnimationUtility.TangentMode.Linear);}return c;}).ToArray();AnimationUtility.SetEditorCurves(clip,bindings,curves);clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);return Save(clip,Root+"/Clips/"+name+".anim");}
 }
 static void Solve(Transform arm,Transform mid,Transform hand,Vector3 target,Quaternion rotation,Vector3 hint)
 {
  Vector3 origin=arm.position,dir=(target-origin).normalized;float a=Vector3.Distance(origin,mid.position),b=Vector3.Distance(mid.position,hand.position);float distance=Mathf.Clamp(Vector3.Distance(origin,target),Mathf.Abs(a-b)+.001f,a+b-.001f);
  Vector3 bend=Vector3.ProjectOnPlane(hint-origin,dir).normalized;if(bend.sqrMagnitude<.001f)bend=Vector3.ProjectOnPlane(mid.position-origin,dir).normalized;
  float along=(a*a-b*b+distance*distance)/(2*distance);Vector3 elbow=origin+dir*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
  arm.rotation=Quaternion.FromToRotation(mid.position-origin,elbow-origin)*arm.rotation;mid.rotation=Quaternion.FromToRotation(hand.position-mid.position,origin+dir*distance-mid.position)*mid.rotation;hand.rotation=rotation;
 }
 sealed class Pair{public Transform source,target;public Quaternion offset;}
 static List<Pair> Pairs(GameObject source,GameObject target,Quaternion axes)
 {
  var names=new Dictionary<string,string>{{"clavicle_l","Clavicle_L"},{"clavicle_r","Clavicle_R"},{"upperarm_l","Shoulder_L"},{"upperarm_r","Shoulder_R"},{"lowerarm_l","Elbow_L"},{"lowerarm_r","Elbow_R"},{"hand_l","Hand_L"},{"hand_r","Hand_R"}};
  foreach(string side in new[]{"l","r"})
  {
   string suffix=side=="l"?"":"1";
   for(int i=1;i<=3;i++)
   {
    names["thumb_0"+i+"_"+side]="Thumb_0"+i+suffix;
    names["index_0"+i+"_"+side]="IndexFinger_0"+(i+1)+suffix;
    names["middle_0"+i+"_"+side]="Finger_0"+(i+1)+suffix;
   }
  }
  return names.Select(k=>new Pair{source=Find(source,k.Key),target=Find(target,k.Value)}).Where(p=>p.source&&p.target).OrderBy(p=>AnimationUtility.CalculateTransformPath(p.target,target.transform).Count(c=>c=='/')).Select(p=>{p.offset=Quaternion.Inverse(axes*p.source.rotation)*p.target.rotation;return p;}).ToList();
 }
 static SoldierWeapon.HandPlacement CalibratedHands(SoldierWeapon.HandPlacement original,GameObject source)
 {
  var gun=Find(source,"ik_hand_gun");var right=Find(source,"hand_r");var left=Find(source,"hand_l");
  var frame=gun.rotation*Quaternion.Euler(0,180,0);
  // Keep the existing trigger-hand contact, but use the source weapon's support
  // grip spacing instead of inheriting an unrelated rifle's left-hand position.
  original.leftGrip.position=original.rightGrip.position+Quaternion.Inverse(frame)*(left.position-right.position);
  return original;
 }
 static AnimationClip Bake(string key,string action,GameObject source,GameObject player,ArenaWeaponCatalog.Entry reference)
 {
  var l=player.GetComponent<SoldierWeaponLoadout>();var fp=l.presentation.view.armsAnimator;var motion=l.firstPersonMount;var body=fp.transform.Find("ViewMotion/PolygonSoldier");
  Idle(reference.weapon.firstPerson).SampleAnimation(fp.gameObject,0);Clip(key,"Idle").SampleAnimation(source,0);
  CalibratedHands(reference.thirdPersonPrefab.GetComponent<SoldierWeapon>().firstPersonHands,source).Apply(l.firstPersonGripRig.leftHand,l.firstPersonGripRig.rightHand);
  var sg=Find(source,"ik_hand_gun");var sr=Find(source,"hand_r");var sl=Find(source,"hand_l");var hands=new[]{Find(fp.gameObject,"Hand_L"),Find(fp.gameObject,"Hand_R")};var sourceHands=new[]{sl,sr};var arms=new[]{Find(fp.gameObject,"Shoulder_L"),Find(fp.gameObject,"Shoulder_R")};var mids=new[]{Find(fp.gameObject,"Elbow_L"),Find(fp.gameObject,"Elbow_R")};var sourceMids=new[]{Find(source,"lowerarm_l"),Find(source,"lowerarm_r")};
  // Keep each existing weapon's calibrated hip/grip frame, then transfer motion deltas.
  var targetGrip=new[]{l.firstPersonGripRig.leftHand.data.target,l.firstPersonGripRig.rightHand.data.target};
  for(int i=0;i<2;i++)Solve(arms[i],mids[i],hands[i],targetGrip[i].position,targetGrip[i].rotation,mids[i].position);
  Vector3 gunStart=motion.position,sourceStart=sg.position;Quaternion axes=motion.rotation*Quaternion.Inverse(sg.rotation*Quaternion.Euler(0,180,0)),motionStart=motion.rotation,sourceRotation=sg.rotation;
  var pairs=Pairs(source,fp.gameObject,axes);Vector3[] handOffset=new Vector3[2],hintOffset=new Vector3[2];Quaternion[] handOffsetQ=new Quaternion[2];
  for(int i=0;i<2;i++)
  {
   handOffset[i]=hands[i].position-(gunStart+axes*(sourceHands[i].position-sourceStart));
   handOffsetQ[i]=Quaternion.Inverse(axes*sourceHands[i].rotation)*hands[i].rotation;
   hintOffset[i]=mids[i].position-(gunStart+axes*(sourceMids[i].position-sourceStart));
  }
  var pose=new Pose(fp.gameObject);var bones=fp.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.bones).Where(t=>t).Concat(new[]{body.Find("Root"),motion}).Distinct().ToArray();var curves=new Curves();var input=Clip(key,action);bool grenade=action=="Grenade";
  if(grenade)input=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Animations/Weapons/Clips/Grenades/Grenade_UE4_Throw.anim");
  float length=grenade?3.2f:input.length;int frames=Mathf.Max(1,Mathf.CeilToInt(length*30));float reach=0;
  for(int f=0;f<=frames;f++)
  {
   float t=length*f/frames;pose.Restore();input.SampleAnimation(source.gameObject,input.length*f/frames);
   foreach(var p in pairs)p.target.rotation=axes*p.source.rotation*p.offset;
   if(!grenade)motion.SetPositionAndRotation(gunStart+axes*(sg.position-sourceStart),axes*sg.rotation*Quaternion.Inverse(sourceRotation)*Quaternion.Inverse(axes)*motionStart);
   for(int i=0;i<2;i++)
   {
    // Relative hand travel follows the moving gun; the native grip offset stays fixed.
    var dq=motion.rotation*Quaternion.Inverse(motionStart);
    var goal=motion.position+axes*(sourceHands[i].position-sg.position)+dq*handOffset[i];
    if(grenade)goal=gunStart+axes*(sourceHands[i].position-sourceStart)+handOffset[i];
    var hint=motion.position+axes*(sourceMids[i].position-sg.position)+dq*hintOffset[i];
    Solve(arms[i],mids[i],hands[i],goal,axes*sourceHands[i].rotation*handOffsetQ[i],hint);reach=Mathf.Max(reach,Vector3.Distance(goal,hands[i].position));
   }
   foreach(var bone in bones)curves.Bone(bone,fp.transform,t);
   float weight=action=="Reload"?Mathf.Min(Mathf.Clamp01(t/.08f),Mathf.Clamp01((length-t)/.12f)):0;
   curves.Value("",typeof(Animator),"WeaponReloadWeight",t,weight);
  }
  var result=curves.Finish(key+"_Polygon_"+action,action=="Idle");report.Add(key+" "+action+" frames="+frames+" reach="+reach);return result;
 }
 static RuntimeAnimatorController BakeThirdPerson(string key,GameObject source,GameObject player,ArenaWeaponCatalog.Entry reference)
 {
  var p=player.GetComponent<SoldierWeaponPresentation>();var animator=p.characterAnimator;var motion=p.weaponRoot;
  var original=reference.weapon.thirdPerson;
  var idle=original.animationClips.FirstOrDefault(c=>c.humanMotion&&c.name.Contains("ThirdPerson_Idle"))??original.animationClips.First(c=>c.humanMotion);
  idle.SampleAnimation(animator.gameObject,0);
  var hold=original.animationClips.FirstOrDefault(c=>c.name.EndsWith("WeaponHold"));if(hold)hold.SampleAnimation(animator.gameObject,0);
  Clip(key,"Idle").SampleAnimation(source,0);
  CalibratedHands(reference.thirdPersonPrefab.GetComponent<SoldierWeapon>().thirdPersonHands,source).Apply(p.supportHand,p.firingHand);
  var sg=Find(source,"ik_hand_gun");
  var sourceHands=new[]{Find(source,"hand_l"),Find(source,"hand_r")};var sourceMids=new[]{Find(source,"lowerarm_l"),Find(source,"lowerarm_r")};
  var arms=new[]{animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),animator.GetBoneTransform(HumanBodyBones.RightUpperArm)};
  var mids=new[]{animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),animator.GetBoneTransform(HumanBodyBones.RightLowerArm)};
  var hands=new[]{animator.GetBoneTransform(HumanBodyBones.LeftHand),animator.GetBoneTransform(HumanBodyBones.RightHand)};
  var targets=new[]{p.supportHand.data.target,p.firingHand.data.target};
  for(int i=0;i<2;i++)Solve(arms[i],mids[i],hands[i],targets[i].position,targets[i].rotation,mids[i].position);
  var sourceStart=sg.position;var sourceQ=sg.rotation;var targetStart=motion.position;var targetQ=motion.rotation;
  var axes=motion.rotation*Quaternion.Inverse(sg.rotation*Quaternion.Euler(0,180,0));var pairs=Pairs(source,animator.gameObject,axes);
  var offsets=new Vector3[2];var hintOffsets=new Vector3[2];var rotations=new Quaternion[2];
  for(int i=0;i<2;i++){offsets[i]=hands[i].position-targetStart-axes*(sourceHands[i].position-sourceStart);hintOffsets[i]=mids[i].position-targetStart-axes*(sourceMids[i].position-sourceStart);rotations[i]=Quaternion.Inverse(axes*sourceHands[i].rotation)*hands[i].rotation;}
  var pose=new Pose(animator.gameObject);var human=new HumanPose();var replacements=new Dictionary<string,AnimationClip>();
  using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
  foreach(string action in new[]{"Reload","Grenade"})
  {
   var input=action=="Reload"?Clip(key,"Reload"):AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Animations/Weapons/Clips/Grenades/Grenade_UE4_Throw.anim");
   float length=action=="Grenade"?3.2f:input.length;int frames=Mathf.CeilToInt(length*30);var curves=new Curves();var weaponCurves=new Curves();
   for(int f=0;f<=frames;f++)
   {
    float time=length*f/frames;pose.Restore();input.SampleAnimation(source,input.length*f/frames);
    foreach(var pair in pairs)pair.target.rotation=axes*pair.source.rotation*pair.offset;
    if(action=="Reload")motion.SetPositionAndRotation(targetStart+axes*(sg.position-sourceStart),axes*sg.rotation*Quaternion.Inverse(sourceQ)*Quaternion.Inverse(axes)*targetQ);
    var dq=motion.rotation*Quaternion.Inverse(targetQ);
    for(int side=0;side<2;side++)
    {
     var goal=motion.position+axes*(sourceHands[side].position-sg.position)+dq*offsets[side];
     if(action=="Grenade")goal=targetStart+axes*(sourceHands[side].position-sourceStart)+offsets[side];
     Solve(arms[side],mids[side],hands[side],goal,axes*sourceHands[side].rotation*rotations[side],motion.position+axes*(sourceMids[side].position-sg.position)+dq*hintOffsets[side]);
    }
    handler.GetHumanPose(ref human);
    for(int m=0;m<HumanTrait.MuscleCount;m++)curves.Value("",typeof(Animator),HumanTrait.MuscleName[m],time,human.muscles[m]);
    if(action=="Reload")
    {
     curves.Value("",typeof(Animator),"WeaponReloadWeight",time,Mathf.Min(Mathf.Clamp01(time/.08f),Mathf.Clamp01((length-time)/.12f)));
     weaponCurves.Bone(motion,animator.transform,time);
    }
   }
   replacements[action]=curves.Finish(key+"_PolygonTPS_"+action,false);
   if(action=="Reload")replacements["WeaponReload"]=weaponCurves.Finish(key+"_PolygonTPS_WeaponReload",false);
  }
  var output=new AnimatorOverrideController(original);var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();output.GetOverrides(overrides);
  output.ApplyOverrides(overrides.Select(pair=>{string n=(pair.Value?pair.Value:pair.Key).name;AnimationClip clip=n.EndsWith("ThirdPerson_Reload")?replacements["Reload"]:n.EndsWith("WeaponReload")?replacements["WeaponReload"]:n=="GrenadeThrow_TPS"?replacements["Grenade"]:pair.Value;return new KeyValuePair<AnimationClip,AnimationClip>(pair.Key,clip);}).ToList());
  output.name=key+"_PolygonTPS";return Save(output,Root+"/Controllers/"+output.name+".overrideController");
 }
 static Transform Part(GameObject root,params string[] names)
 {
  foreach(var name in names){var match=Find(root,name);if(match)return match;}return null;
 }
 static RuntimeAnimatorController BakeParts(string key,GameObject source,ArenaWeaponCatalog.Entry reference)
 {
  var gun=Clone(reference.firstPersonPrefab.gameObject);
  try
  {
   var controller=reference.weapon.firstPersonParts;
   var idle=controller.animationClips.FirstOrDefault(c=>c.name.EndsWith("Idle"))??controller.animationClips.First();idle.SampleAnimation(gun,0);
   var srcGun=Find(source,"SourceWeapon").gameObject;Clip(key,"Idle").SampleAnimation(source,0);
   var mapping=new List<(Transform s,Transform t)>();
   void Add(string[] sn,string[] tn){var a=Part(srcGun,sn);var b=Part(gun,tn);if(a&&b&&!mapping.Any(m=>m.t==b))mapping.Add((a,b));}
   Add(new[]{"Mag","Magazine","Bag"},new[]{"Mag","Magazine","magazine"});
   Add(new[]{"Bolt","Slider","Charge","Charger","ChargingHandle","Pump"},new[]{"Slider","Bolt","Charge","Pump"});
   Add(new[]{"BoltRelease","ReleaseHandle"},new[]{"BoltRelease","Bolt_Catch"});
   Add(new[]{"Trigger"},new[]{"Trigger"});
   var sp=mapping.Select(m=>srcGun.transform.InverseTransformPoint(m.s.position)).ToArray();var sq=mapping.Select(m=>Quaternion.Inverse(srcGun.transform.rotation)*m.s.rotation).ToArray();
   var tp=mapping.Select(m=>m.t.localPosition).ToArray();var tq=mapping.Select(m=>m.t.localRotation).ToArray();var pose=new Pose(gun);var replacements=new Dictionary<string,AnimationClip>();
   foreach(string action in new[]{"Idle","Fire","Reload"})
   {
    var input=Clip(key,action);var curves=new Curves();int frames=Mathf.Max(1,Mathf.CeilToInt(input.length*30));float travel=0;
    for(int f=0;f<=frames;f++)
    {
     pose.Restore();input.SampleAnimation(source,input.length*f/frames);float time=input.length*f/frames;
     for(int j=0;j<mapping.Count;j++)
     {
      var pair=mapping[j];var displacement=srcGun.transform.TransformVector(srcGun.transform.InverseTransformPoint(pair.s.position)-sp[j]);
      // Source gun faces -Z locally; transfer its actual world-space displacement.
      pair.t.localPosition=tp[j]+pair.t.parent.InverseTransformVector(displacement);
      var delta=(Quaternion.Inverse(srcGun.transform.rotation)*pair.s.rotation)*Quaternion.Inverse(sq[j]);
      pair.t.localRotation=Quaternion.Inverse(pair.t.parent.rotation)*srcGun.transform.rotation*delta*Quaternion.Inverse(srcGun.transform.rotation)*pair.t.parent.rotation*tq[j];
      travel=Mathf.Max(travel,displacement.magnitude);
     }
     // Preserve the existing default pose for every animated part, then replace mapped channels.
     foreach(var path in AnimationUtility.GetCurveBindings(idle).Where(b=>b.type==typeof(Transform)).Select(b=>b.path).Distinct())
     {var t=gun.transform.Find(path);if(t)curves.Bone(t,gun.transform,time);}
     foreach(var m in mapping)curves.Bone(m.t,gun.transform,time);
    }
    replacements[action]=curves.Finish(key+"_PolygonParts_"+action,action=="Idle");report.Add(key+" "+action+" mappedParts="+mapping.Count+" travel="+travel);
   }
   var basis=new AnimatorOverrideController(controller);var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();basis.GetOverrides(pairs);
   basis.ApplyOverrides(pairs.Select(p=>{string n=p.Value?p.Value.name:p.Key.name;string action=n.Contains("Reload")?"Reload":n.Contains("Fire")?"Fire":"Idle";return new KeyValuePair<AnimationClip,AnimationClip>(p.Key,replacements[action]);}).ToList());basis.name=key+"_PolygonParts";
   return Save(basis,Root+"/Controllers/"+basis.name+".overrideController");
  }
  finally{Object.DestroyImmediate(gun);}
 }
 static RuntimeAnimatorController Override(string name,RuntimeAnimatorController original,Dictionary<string,AnimationClip> replace)
 {
  var old=new List<KeyValuePair<AnimationClip,AnimationClip>>();RuntimeAnimatorController basis=original;
  if(original is AnimatorOverrideController o){o.GetOverrides(old);basis=o.runtimeAnimatorController;}else old.AddRange(original.animationClips.Distinct().Select(c=>new KeyValuePair<AnimationClip,AnimationClip>(c,c)));
  var output=new AnimatorOverrideController(basis){name=name};
  output.ApplyOverrides(old.Select(p=>{string n=(p.Value?p.Value:p.Key).name;AnimationClip chosen=null;
   if(n.Contains("GrenadeThrow_FPS"))replace.TryGetValue("Grenade",out chosen);
   else if(n=="Equip_FirstPerson")replace.TryGetValue("Equip_FirstPerson",out chosen);
   else if(n=="Unequip_FirstPerson")replace.TryGetValue("Unequip_FirstPerson",out chosen);
   else if(n.Contains("Reload_Empty")||n.Contains("FirstPerson_Reload"))replace.TryGetValue("Reload",out chosen);
   else if(n.Contains("Idle_Pose")||n.Contains("Aim_Pose")||n.EndsWith("Idle_Baked")||n.EndsWith("Aim_Baked"))replace.TryGetValue("Idle",out chosen);
   else if((name.StartsWith("Kar98")||name.StartsWith("L96")||name.StartsWith("KXG"))&&(n.Contains("AR_Fire")||n.Contains("Aim_Fire")||n.Contains("Handgun_Fire")))replace.TryGetValue("Fire",out chosen);
   return new KeyValuePair<AnimationClip,AnimationClip>(p.Key,chosen?chosen:p.Value);
  }).ToList());return Save(output,Root+"/Controllers/"+name+".overrideController");
 }
 public static object PruneUnusedClips()
 {
  var used=new HashSet<string>(AssetDatabase.GetDependencies(Source+"/AnimationTestCatalog.asset",true));int removed=0;
  foreach(var path in AssetDatabase.FindAssets("t:AnimationClip",new[]{Root+"/Clips"}).Select(AssetDatabase.GUIDToAssetPath))
   if(!used.Contains(path)&&AssetDatabase.DeleteAsset(path))removed++;
  return removed;
 }
 [MenuItem("Tools/Arena Shooter/Animation Tests/Bake Polygon alternatives")]
 public static void Rebuild()=>Main();
 public static object Main()
 {
  if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before baking.");
  original=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");tests=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>(Source+"/AnimationTestCatalog.asset");report.Clear();Directory.CreateDirectory(Root+"/Prefabs");var entries=tests.entries.Where(e=>!e.weapon.catalogId.StartsWith("polygon-animation-")).ToList();
  foreach(var source in tests.entries.Where(e=>e.weapon.catalogId.StartsWith("animation-test-")&&e.weapon.catalogId.EndsWith("_UE4")))
  {
   string key=source.weapon.catalogId.Replace("animation-test-","");if(!Match.TryGetValue(key,out string id))throw new Exception("No equivalent for "+key);var reference=original.entries.First(e=>e.weapon.catalogId==id);
   var rig=Clone(source.firstPersonPrefab.gameObject);var player=Clone(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Players/Player_Polygon_Local.prefab"));
   try
   {
    var baked=new Dictionary<string,AnimationClip>();foreach(string action in new[]{"Idle","Reload","Grenade"}.Concat(key.StartsWith("Kar98")||key.StartsWith("L96")||key.StartsWith("KXG")?new[]{"Fire"}:Array.Empty<string>()))baked[action]=Bake(key,action,rig,player,reference);
    var def=Object.Instantiate(reference.weapon);def.catalogId="polygon-animation-"+key;def.displayName=reference.weapon.displayName+" (UE4 Alt: "+source.weapon.displayName.Replace(" (UE4)","")+")";def.showFirstPersonHands=true;def.firstPerson=Override(key+"_PolygonArms",reference.weapon.firstPerson,baked);def.firstPersonParts=BakeParts(key,rig,reference);def.thirdPerson=BakeThirdPerson(key,rig,player,reference);def.reloadDuration=baked["Reload"].length;def.useAuthoredReload=true;def.reloadSwayWeight=def.reloadWalkWeight=0;def.equipDuration=reference.weapon.equipDuration;def.unequipDuration=reference.weapon.unequipDuration;
    def=Save(def,Root+"/Definitions/"+key+".asset");
    var fp=Clone(reference.firstPersonPrefab.gameObject);var tp=Clone(reference.thirdPersonPrefab.gameObject);
    try
    {
     fp.name=reference.weapon.displayName+"_UE4_Alt_FPS";tp.name=reference.weapon.displayName+"_UE4_Alt_TPS";var fv=fp.GetComponent<SoldierWeaponView>();fv.animationOverride=null;fv.independentFirstPersonFrame=false;fv.grenadeView=null;fv.mountName=fp.name;
     var weapon=tp.GetComponent<SoldierWeapon>();weapon.definition=def;
     Clip(key,"Idle").SampleAnimation(rig,0);
     weapon.firstPersonHands=CalibratedHands(weapon.firstPersonHands,rig);weapon.thirdPersonHands=CalibratedHands(weapon.thirdPersonHands,rig);
     var f=PrefabUtility.SaveAsPrefabAsset(fp,Root+"/Prefabs/"+key+"_FPS.prefab").GetComponent<SoldierWeaponView>();var t=PrefabUtility.SaveAsPrefabAsset(tp,Root+"/Prefabs/"+key+"_TPS.prefab").GetComponent<SoldierWeaponView>();
     entries.Add(new ArenaWeaponCatalog.Entry{weapon=def,basePrefab=reference.basePrefab,firstPersonPrefab=f,thirdPersonPrefab=t,polished=false,sights=reference.sights,attachments=reference.attachments,defaultSight=reference.defaultSight,animationStyle="UE4 baked Polygon arms"});
    }
    finally{Object.DestroyImmediate(fp);Object.DestroyImmediate(tp);}
   }
   finally{Object.DestroyImmediate(rig);Object.DestroyImmediate(player);}
  }
  tests.entries=entries.ToArray();EditorUtility.SetDirty(tests);AssetDatabase.SaveAssets();PruneUnusedClips();Directory.CreateDirectory("Library/AnimationTests");File.WriteAllLines("Library/AnimationTests/PolygonBake.txt",report);return "Baked "+(entries.Count-original.entries.Length)+" test entries";
 }
}
