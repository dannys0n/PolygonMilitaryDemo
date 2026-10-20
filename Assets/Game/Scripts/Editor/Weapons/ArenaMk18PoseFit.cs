using System;using System.Collections.Generic;using System.Linq;
using UnityEditor;using UnityEngine;using UnityEngine.Animations;using UnityEngine.Animations.Rigging;using UnityEngine.Playables;
using Object=UnityEngine.Object;

// Editor-only authoring. Evaluate the original rig, then fit the fixed target skin.
// No solver, source skeleton, or runtime IK dependency is added to the baked prefab.
public static class ArenaMk18PoseFit
{
 public sealed class Source : IDisposable
 {
  public bool IsThirdPerson {get;private set;}
  public TwoBoneIKConstraint left,right;
  SoldierWeaponPresentation presentation;SoldierWeaponDefinition definition;AvatarMask weaponMask;
  AnimationLayerMixerPlayable mixer,bodyPose;AnimationClipPlayable hold,reload,bodyBase,bodyIdle;bool bodyReload;
  public GameObject player;public Animator animator;public SoldierWeaponLoadout loadout;public SoldierWeaponView weapon;
  public float MaxGripError {get;private set;}
  public RigBuilder rig;PlayableGraph graph;AnimationClipPlayable playable,parts;AnimationClip clip;AnimationCurve reloadWeight;
  public Source(ArenaWeaponCatalog.Entry entry,bool thirdPerson=false)
  {
   player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Players/Player_Polygon_Local.prefab"));
   loadout=player.GetComponent<SoldierWeaponLoadout>();presentation=loadout.presentation;definition=entry.weapon;IsThirdPerson=thirdPerson;animator=thirdPerson?presentation.characterAnimator:presentation.view.armsAnimator;
   animator.transform.SetParent(null,false);animator.transform.SetPositionAndRotation(thirdPerson?Vector3.zero:definition.viewOffset+definition.hipFireOffset,thirdPerson?Quaternion.identity:Quaternion.Euler(definition.viewRotationOffset+definition.hipFireRotationOffset));
   animator.gameObject.SetActive(true);animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   var prefab=thirdPerson?entry.thirdPersonPrefab:entry.firstPersonPrefab;weapon=Object.Instantiate(prefab,thirdPerson?loadout.thirdPersonMount:loadout.firstPersonMount);weapon.transform.SetLocalPositionAndRotation(prefab.mountPosition,prefab.mountRotation);weapon.transform.localScale=prefab.mountScale;
   left=thirdPerson?presentation.supportHand:loadout.firstPersonGripRig.leftHand;right=thirdPerson?presentation.firingHand:loadout.firstPersonGripRig.rightHand;
   var calibration=entry.thirdPersonPrefab.GetComponent<SoldierWeapon>();(thirdPerson?calibration.thirdPersonHands:calibration.firstPersonHands).Apply(left,right);
   if(thirdPerson)presentation.aimTarget.position=new Vector3(0,1.5f,100);
   rig=animator.GetComponent<RigBuilder>();rig.enabled=true;
   foreach(var layer in rig.layers){layer.active=true;layer.rig.weight=1;}
  }
  public void Begin(AnimationClip input)
  {
   End();MaxGripError=0;clip=input;reloadWeight=AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),"WeaponReloadWeight"));
   graph=PlayableGraph.Create("MK18 original evaluated IK capture");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
   playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
   Playable inputRoot=playable;
   if(IsThirdPerson)
   {
    // These authored weapon clips are upper-body poses. Preserve the standing
    // locomotion base and the same masks used by the production character.
    var controller=ArenaWeaponAnimationSource.Base(definition.thirdPerson);
    bodyBase=AnimationClipPlayable.Create(graph,ArenaWeaponAnimationSource.State(definition.thirdPerson,"Base Layer","Locomotion"));
    bodyIdle=AnimationClipPlayable.Create(graph,ArenaWeaponAnimationSource.Body(definition.thirdPerson,"Idle"));
    bodyReload=input==ArenaWeaponAnimationSource.Body(definition.thirdPerson,"Reload");
    bodyPose=AnimationLayerMixerPlayable.Create(graph,3);bodyPose.ConnectInput(0,bodyBase,0,1);bodyPose.ConnectInput(1,playable,0,1);bodyPose.ConnectInput(2,bodyIdle,0,0);
    var poseMask=controller.layers.First(l=>l.name==(bodyReload?"Reload":"SourceSway")).avatarMask;var idleMask=controller.layers.First(l=>l.name=="SourceSway").avatarMask;
    if(poseMask)bodyPose.SetLayerMaskFromAvatarMask(1,poseMask);if(idleMask)bodyPose.SetLayerMaskFromAvatarMask(2,idleMask);
    inputRoot=bodyPose;
    hold=AnimationClipPlayable.Create(graph,ArenaWeaponAnimationSource.State(definition.thirdPerson,"WeaponPose","Idle"));reload=AnimationClipPlayable.Create(graph,ArenaWeaponAnimationSource.State(definition.thirdPerson,"WeaponPose","Reload"));
    mixer=AnimationLayerMixerPlayable.Create(graph,3);mixer.ConnectInput(0,inputRoot,0,1);mixer.ConnectInput(1,hold,0,1);mixer.ConnectInput(2,reload,0,0);
    weaponMask=new AvatarMask();weaponMask.AddTransformPath(animator.transform,true);for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)weaponMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
    var paths=AnimationUtility.GetCurveBindings(hold.GetAnimationClip()).Concat(AnimationUtility.GetCurveBindings(reload.GetAnimationClip())).Select(b=>b.path).Distinct().ToArray();for(int i=0;i<weaponMask.transformCount;i++){var path=weaponMask.GetTransformPath(i);if(path.StartsWith(animator.name+"/"))path=path.Substring(animator.name.Length+1);if(path==animator.name)path="";weaponMask.SetTransformPath(i,path);weaponMask.SetTransformActive(i,paths.Contains(path));}
    mixer.SetLayerMaskFromAvatarMask(1,weaponMask);mixer.SetLayerMaskFromAvatarMask(2,weaponMask);inputRoot=mixer;
   }
   rig.StartPreview();var root=rig.BuildPreviewGraph(graph,inputRoot);var output=AnimationPlayableOutput.Create(graph,"Evaluated Polygon reference",animator);output.SetSourcePlayable(root);
   if(weapon.animator){var partClip=ArenaWeaponAnimationSource.State(definition.firstPersonParts,"Base Layer",input.name.Contains("Reload")?"Reload":input.name.Contains("Fire")?"Fire":"Idle");parts=AnimationClipPlayable.Create(graph,partClip);var partOutput=AnimationPlayableOutput.Create(graph,"Original weapon parts",weapon.animator);partOutput.SetSourcePlayable(parts);}
   graph.Play();
  }
  public void Sample(float time)
  {
   float weight=1-Mathf.Clamp01(reloadWeight?.Evaluate(time)??0);
   left.weight=right.weight=weight;
   if(IsThirdPerson){float r=1-weight;bodyBase.SetTime(0);bodyIdle.SetTime(0);bodyPose.SetInputWeight(2,bodyReload?1-r:0);presentation.chestAim.weight=presentation.weaponAim.weight=weight;presentation.runWeaponFollow.weight=0;presentation.aimPivot.localPosition=Vector3.Lerp(definition.thirdPersonHipAimPosition,definition.thirdPersonReloadPosition,r);presentation.aimPivot.localRotation=Quaternion.identity;hold.SetTime(0);reload.SetTime(time);reload.SetTime(time);mixer.SetInputWeight(2,r);}
   playable.SetTime(time);playable.SetTime(time);if(parts.IsValid()){parts.SetTime(time);parts.SetTime(time);}rig.UpdatePreviewGraph(graph);graph.Evaluate(0);
   if(weight>.999f)MaxGripError=Mathf.Max(MaxGripError,Mathf.Max(Vector3.Distance(left.data.tip.position,left.data.target.position),Vector3.Distance(right.data.tip.position,right.data.target.position)));
  }
  void End(){if(graph.IsValid())graph.Destroy();if(rig)rig.StopPreview();if(weaponMask){Object.DestroyImmediate(weaponMask);weaponMask=null;}}
  public void Dispose(){End();if(animator)Object.DestroyImmediate(animator.gameObject);if(player)Object.DestroyImmediate(player);}
 }
 struct Point{public int source,target;public string group;public Vector3 goal;}
 public sealed class Fit
 {
  readonly SkinnedMeshRenderer source,target;readonly Vector3[] sv,tv;readonly BoneWeight[] sw,tw;readonly Matrix4x4[] sb,tb;
  readonly List<Point> points=new();readonly Transform[] joints;readonly Dictionary<Transform,Quaternion> rest=new();
  readonly Dictionary<Transform,Quaternion> correction=new();readonly Dictionary<Transform,Transform> pair=new();
  readonly Dictionary<Transform,Vector3> bindPosition=new();
  // Calibrate mesh proportions once in idle. Re-solving wrist placement independently
  // for every reload frame makes the elbow chase changing surface-fit minima.
  readonly Dictionary<Transform,(Vector3 position,Quaternion rotation)> wristCalibration=new();
  Vector3? weaponSeat;
  // The accepted skin has different finger proportions. Keep the trigger contact,
  // rather than assuming a matching wrist origin also means a matching grip.
  public void SeatWeapon(Transform gun,Transform supportArm)
  {
   if(!weaponSeat.HasValue)
   {
    var contact=points.Where(p=>p.group.StartsWith("r/IndexFinger_03")).ToArray();
    if(contact.Length==0)throw new Exception("Missing firing-finger contact landmarks");
    var delta=contact.Aggregate(Vector3.zero,(sum,p)=>sum+Skin(tv[p.target],tw[p.target],target.bones,tb)-p.goal)/contact.Length;
    // The fitted glove's thicker index pad needs a little additional trigger
    // clearance. Author it once in the idle grip frame, including during reload.
    weaponSeat=Quaternion.Inverse(gun.rotation)*(delta+Vector3.up*.008f);
   }
   var correction=gun.rotation*weaponSeat.Value;
   gun.position+=correction;supportArm.position+=correction;
  }
  public void BeginClip(){WorstRms=0;}
  public float WorstRms {get;private set;}public float LastRms {get;private set;}
  static string Mirror(string name){foreach(string n in new[]{"Clavicle","Shoulder","Elbow","Hand"}){if(name==n+"_L")return n+"_R";if(name==n+"_R")return n+"_L";}foreach(string n in new[]{"Thumb","IndexFinger","Finger"})for(int i=1;i<=4;i++){if(name==n+"_0"+i)return name+"1";if(name==n+"_0"+i+"1")return name.Substring(0,name.Length-1);}return name;}
  static Vector3 Skin(Vector3 v,BoneWeight w,Transform[] bones,Matrix4x4[] bind)
  {return (bones[w.boneIndex0].localToWorldMatrix*bind[w.boneIndex0]).MultiplyPoint3x4(v)*w.weight0+(bones[w.boneIndex1].localToWorldMatrix*bind[w.boneIndex1]).MultiplyPoint3x4(v)*w.weight1+(bones[w.boneIndex2].localToWorldMatrix*bind[w.boneIndex2]).MultiplyPoint3x4(v)*w.weight2+(bones[w.boneIndex3].localToWorldMatrix*bind[w.boneIndex3]).MultiplyPoint3x4(v)*w.weight3;}
  public Fit(GameObject sourceRig,GameObject targetRig,SkinnedMeshRenderer reference,SkinnedMeshRenderer fitted,SkinnedMeshRenderer originalTarget=null)
  {
   source=reference;target=fitted;sv=source.sharedMesh.vertices;tv=target.sharedMesh.vertices;sw=source.sharedMesh.boneWeights;tw=target.sharedMesh.boneWeights;sb=source.sharedMesh.bindposes;tb=target.sharedMesh.bindposes;
   var all=targetRig.GetComponentsInChildren<Transform>(true);var src=sourceRig.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
   foreach(var t in all){bindPosition[t]=t.localPosition;}
   string Name(string n)
   {
    if(n.StartsWith("hand_"))return "Hand_"+(n.EndsWith("l")?"L":"R");
    foreach(string finger in new[]{"thumb","index","middle","ring","pinky"})for(int i=1;i<=3;i++)foreach(string side in new[]{"l","r"})if(n==finger+"_0"+i+"_"+side)return (finger=="thumb"?"Thumb":finger=="index"?"IndexFinger":"Finger")+"_0"+i+(side=="r"?"1":"");
    return null;
   }
   // Use actual skin bind matrices, not the prefab's saved gripping pose.
   var sourceBind=source.bones.Select((b,i)=>(b,m:source.transform.localToWorldMatrix*sb[i].inverse)).ToDictionary(x=>x.b,x=>x.m.rotation);
   foreach(var t in all){string n=Name(t.name);if(n!=null&&src.TryGetValue(n,out var s)){pair[t]=s;correction[t]=Quaternion.Inverse(sourceBind.TryGetValue(s,out var q)?q:s.rotation)*t.rotation;rest[t]=t.localRotation;}}
   joints=all.Where(t=>pair.ContainsKey(t)&&!t.name.StartsWith("hand_")).OrderBy(t=>AnimationUtility.CalculateTransformPath(t,targetRig.transform).Count(c=>c=='/')).ToArray();
   var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Players/Player_Polygon_Local.prefab").GetComponent<SoldierFirstPersonView>().armsAnimator.GetComponentInChildren<SkinnedMeshRenderer>(true);
   var left=new SortedSet<int>();var world=sv.Select(v=>asset.transform.TransformPoint(v)).ToArray();for(int sub=0;sub<source.sharedMesh.subMeshCount;sub++){var tris=source.sharedMesh.GetTriangles(sub);for(int i=0;i<tris.Length;i+=3)if(Mathf.Max(world[tris[i]].x,Mathf.Max(world[tris[i+1]].x,world[tris[i+2]].x))<.0001f)for(int k=0;k<3;k++)left.Add(tris[i+k]);}
   if(originalTarget==null&&tv.Length!=left.Count*2)throw new Exception("Fitted/source topology no longer matches. Do not infer vertex correspondences.");
   var bodyVertices=originalTarget?originalTarget.sharedMesh.vertices:null;var bodyWeights=originalTarget?originalTarget.sharedMesh.boneWeights:null;var bodyBind=originalTarget?originalTarget.sharedMesh.bindposes:null;
   var bodyMap=new Dictionary<int,int>();
   int TargetVertex(int sourceId,int fallback)
   {
    if(!originalTarget)return fallback;if(bodyMap.TryGetValue(sourceId,out int cached))return cached;
    var weight=sw[sourceId];string name=source.bones[weight.boneIndex0].name;var local=sb[weight.boneIndex0].MultiplyPoint3x4(sv[sourceId]);int best=-1;float distance=float.PositiveInfinity;
    for(int i=0;i<bodyVertices.Length;i++){int bone=bodyWeights[i].boneIndex0;if(originalTarget.bones[bone].name!=name)continue;float d=(bodyBind[bone].MultiplyPoint3x4(bodyVertices[i])-local).sqrMagnitude;if(d<distance){distance=d;best=i;}}
    if(best<0||distance>.0025f)throw new Exception("Cannot match body hand landmark "+name);bodyMap[sourceId]=best;return best;
   }
   var ids=left.ToArray();var groups=new Dictionary<string,List<(int source,int target)>>();
   for(int side=0;side<2;side++)for(int i=0;i<ids.Length;i++)
   {
    int sourceId=ids[i];string bone=source.bones[sw[sourceId].boneIndex0].name;if(!bone.StartsWith("Hand_")&&!bone.StartsWith("Thumb_")&&!bone.StartsWith("IndexFinger_")&&!bone.StartsWith("Finger_"))continue;
    if(side==1){var mirror=sv[sourceId];mirror.x=-mirror.x;string mirrored=Mirror(bone);float best=float.PositiveInfinity;int match=-1;for(int j=0;j<sv.Length;j++)if(source.bones[sw[j].boneIndex0].name==mirrored){float d=(sv[j]-mirror).sqrMagnitude;if(d<best){best=d;match=j;}}if(match<0)throw new Exception("Missing mirrored source hand");sourceId=match;}
    string key=(side==0?"l":"r")+"/"+bone;if(!groups.TryGetValue(key,out var values))groups[key]=values=new();values.Add((sourceId,TargetVertex(sourceId,i+side*ids.Length)));
   }
   // Spread landmarks over each finger surface; do not fit just a single tip.
   foreach(var g in groups){var candidates=g.Value.Distinct().ToList();var chosen=new List<(int source,int target)>{candidates[0]};while(chosen.Count<Mathf.Min(6,candidates.Count)){var next=candidates.Where(p=>!chosen.Contains(p)).OrderByDescending(p=>chosen.Min(c=>(tv[p.target]-tv[c.target]).sqrMagnitude)).First();chosen.Add(next);}foreach(var p in chosen)points.Add(new Point{source=p.source,target=p.target,group=g.Key});}
  }
  public void RestoreHandOffset(Transform hand)=>hand.localPosition=bindPosition[hand];
  public void Initialize(){foreach(var p in pair)p.Key.rotation=p.Value.rotation*correction[p.Key];}
  public void Apply(Func<Vector3,Vector3> goal)
  {
   for(int i=0;i<points.Count;i++){var p=points[i];p.goal=goal(Skin(sv[p.source],sw[p.source],source.bones,sb));points[i]=p;}
   foreach(string side in new[]{"l","r"})
   {
    var subset=points.Where(p=>p.group.StartsWith(side+"/")).ToArray();var hand=pair.Keys.First(t=>t.name=="hand_"+side);var handStart=hand.position;var handRotation=hand.rotation;
     bool calibrating=!wristCalibration.TryGetValue(hand,out var wrist);
     if(!calibrating){hand.position=handStart+handRotation*wrist.position;hand.rotation=handRotation*wrist.rotation;}
    var matrices=new Matrix4x4[target.bones.Length];
    float Loss(){for(int b=0;b<matrices.Length;b++)matrices[b]=target.bones[b].localToWorldMatrix*tb[b];float sum=0;foreach(var p in subset){var w=tw[p.target];var v=tv[p.target];var actual=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;sum+=(actual-p.goal).sqrMagnitude;}return sum/subset.Length;}
    void Rotate(Transform joint,float degrees,bool world)
    {
     foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward})
     {
      var q=world?joint.rotation:joint.localRotation;float best=Loss();var bestQ=q;
      foreach(float sign in new[]{-1f,1f}){var candidate=q*Quaternion.AngleAxis(degrees*sign,axis);if(!world&&Quaternion.Angle(rest[joint],candidate)>145)continue;if(world)joint.rotation=candidate;else joint.localRotation=candidate;float loss=Loss();if(loss<best){best=loss;bestQ=candidate;}}
      if(world)joint.rotation=bestQ;else joint.localRotation=bestQ;
     }
    }
    var fingers=joints.Where(t=>t.name.EndsWith("_"+side)&&(t.name.StartsWith("thumb_")||t.name.StartsWith("index_")||t.name.StartsWith("ring_"))).ToArray();
    foreach(float degrees in new[]{12f,6f,3f,1.5f,.5f})for(int pass=0;pass<2;pass++)
    {
     if(calibrating)Rotate(hand,degrees,true);
     if(calibrating)foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward}){var pos=hand.position;var bestPos=pos;float best=Loss();foreach(float sign in new[]{-1f,1f}){var p=pos+axis*(degrees*.00065f*sign);if(Vector3.Distance(p,handStart)>.075f)continue;hand.position=p;float loss=Loss();if(loss<best){best=loss;bestPos=p;}}hand.position=bestPos;}
     foreach(var joint in fingers)Rotate(joint,degrees,false);
    }
    if(calibrating)wristCalibration[hand]=(Quaternion.Inverse(handRotation)*(hand.position-handStart),Quaternion.Inverse(handRotation)*hand.rotation);
   }
   float error=0;foreach(var p in points)error+=(Skin(tv[p.target],tw[p.target],target.bones,tb)-p.goal).sqrMagnitude;LastRms=Mathf.Sqrt(error/points.Count);WorstRms=Mathf.Max(WorstRms,LastRms);

  }
 }
}



