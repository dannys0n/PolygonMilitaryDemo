using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.Animations;using UnityEditor.SceneManagement;using UnityEngine;using Object=UnityEngine.Object;

// Isolated proof: bake the production Polygon MK18 motion onto a fixed UE4 rig.
// Both skins share the resulting clips; production weapon assets stay untouched.
public static class Mk18Ue4StageBuilder
{
 public const string Root=PolygonWeaponConversionBuilder.Root+"/Shared";


 static string PolygonFinger(string finger,int joint,string side)=> (finger=="thumb"?"Thumb":finger=="index"?"IndexFinger":"Finger")+"_0"+joint+(side=="r"?"1":"");
 static T Save<T>(T asset,string path)where T:Object{Directory.CreateDirectory(Path.GetDirectoryName(path));var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(asset,path);return asset;}
 static GameObject Clone(GameObject prefab){var g=Object.Instantiate(prefab);if(PrefabUtility.IsPartOfPrefabInstance(g))PrefabUtility.UnpackPrefabInstance(g,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);return g;}
 static Transform Find(GameObject g,string n)=>g.GetComponentsInChildren<Transform>(true).First(t=>t.name==n);
 public static void RestoreBindPose(GameObject source)
 {
  var skin=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.bones.Any(b=>b&&b.name=="Hand_L"));
  var matrices=skin.sharedMesh.bindposes.Select(b=>skin.transform.localToWorldMatrix*b.inverse).ToArray();
  foreach(var i in Enumerable.Range(0,skin.bones.Length).OrderBy(i=>AnimationUtility.CalculateTransformPath(skin.bones[i],source.transform).Count(c=>c=='/')))
   skin.bones[i].SetPositionAndRotation(matrices[i].GetColumn(3),matrices[i].rotation);
 }
 public static Avatar Human(GameObject go,bool ue)
 {
  var map=new List<HumanBone>();void M(string human,string name)=>map.Add(new HumanBone{humanName=human,boneName=name,limit=new HumanLimit{useDefaultValues=true}});
  foreach(var p in new[]{("Hips","pelvis","Hips"),("Spine","spine_01","Spine_01"),("Chest","spine_02","Spine_02"),("UpperChest","spine_03","Spine_03"),("Neck","neck_01","Neck"),("Head","head","Head")})M(p.Item1,ue?p.Item2:p.Item3);
  foreach(var side in new[]{("Left","l"),("Right","r")})
  {
   string suffix=side.Item2=="l"?"_L":"_R";
   foreach(var p in new[]{("Shoulder","clavicle","Clavicle"),("UpperArm","upperarm","Shoulder"),("LowerArm","lowerarm","Elbow"),("Hand","hand","Hand"),("UpperLeg","thigh","UpperLeg"),("LowerLeg","calf","LowerLeg"),("Foot","foot","Ankle"),("Toes","ball","Ball")})M(side.Item1+p.Item1,ue?p.Item2+"_"+side.Item2:p.Item3+suffix);
   foreach(var finger in new[]{("Thumb","thumb"),("Index","index"),("Middle","middle"),("Ring","ring"),("Little","pinky")})
    for(int i=0;i<3;i++)if(ue||finger.Item2=="thumb"||finger.Item2=="index"||finger.Item2=="middle")M(side.Item1+" "+finger.Item1+" "+new[]{"Proximal","Intermediate","Distal"}[i],ue?finger.Item2+"_0"+(i+1)+"_"+side.Item2:PolygonFinger(finger.Item2,i+1,side.Item2));
  }
  var skeleton=go.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).Select(g=>g.First()).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
  var a=AvatarBuilder.BuildHumanAvatar(go,new HumanDescription{human=map.ToArray(),skeleton=skeleton,upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=0,legStretch=0});if(!a.isValid||!a.isHuman)throw new Exception("Invalid humanoid calibration");return a;
 }
 static void Solve(Transform arm,Transform mid,Transform hand,Vector3 goal,Vector3 pole)
 {
  var start=arm.position;float a=Vector3.Distance(start,mid.position),b=Vector3.Distance(mid.position,hand.position);var axis=goal-start;
  // Author a small shoulder reach rather than clipping the wrist trajectory when
  // the new rig has a shorter arm. Bone lengths remain fixed; this is baked only.
  float reach=axis.magnitude,maxReach=a+b-.001f;
  if(reach>maxReach){arm.position+=axis.normalized*(reach-maxReach);start=arm.position;axis=goal-start;}
  float d=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.001f,maxReach);axis.Normalize();var bend=Vector3.ProjectOnPlane(pole-start,axis).normalized;if(bend.sqrMagnitude<.1f)bend=Vector3.ProjectOnPlane(Vector3.down,axis).normalized;
  float along=(a*a-b*b+d*d)/(2*d);var elbow=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));var q=hand.rotation;arm.rotation=Quaternion.FromToRotation(mid.position-start,elbow-start)*arm.rotation;mid.rotation=Quaternion.FromToRotation(hand.position-mid.position,start+axis*d-mid.position)*mid.rotation;hand.rotation=q;if(Vector3.Distance(hand.position,goal)>.15f)throw new Exception("IK target mismatch goal="+goal+" actual="+hand.position+" shoulder="+start+" lengths="+a+","+b);
 }
 public static AnimationClip Bake(GameObject source,GameObject arms,GameObject fps,GameObject gun,HumanPoseHandler sh,HumanPoseHandler th,HumanPose basePose,AnimationClip input,AnimationClip parts,Vector3 offset,string name,bool loop,Mk18PoseFit.Source evaluated,Mk18PoseFit.Fit fitting,string outputFolder=null,AnimationClip returnPose=null,bool normalizeToRest=false)
 {
  var bones=arms.GetComponentsInChildren<Transform>(true).Where(t=>t!=arms.transform).ToArray();var curves=new Dictionary<EditorCurveBinding,List<Keyframe>>();
  void V(Transform bone,string property,float time,float value){if(!float.IsFinite(value))throw new Exception("Invalid pose");var binding=EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bone,fps.transform),typeof(Transform),property);if(!curves.TryGetValue(binding,out var keys))curves[binding]=keys=new();keys.Add(new Keyframe(time,value));}
  void T(Transform bone,float time){var p=bone.localPosition;var q=bone.localRotation;for(int i=0;i<3;i++)V(bone,"m_LocalPosition."+"xyz"[i],time,p[i]);for(int i=0;i<4;i++)V(bone,"m_LocalRotation."+"xyzw"[i],time,q[i]);}
  evaluated.Begin(input);fitting.BeginClip();
  var pose=new HumanPose();float duration=loop?.1f:input.length;int frames=loop?1:Mathf.Max(1,Mathf.CeilToInt(duration*60));var sg=evaluated.weapon.transform;
  var targetPositions=bones.Select(t=>t.localPosition).ToArray();
  var armsPosition=arms.transform.position;var armsRotation=arms.transform.rotation;
  var alignmentPosition=Vector3.zero;var alignmentRotation=Quaternion.identity;
  var sourceBones=source.GetComponentsInChildren<Transform>(true);var sourceP=sourceBones.Select(t=>t.localPosition).ToArray();var sourceQ=sourceBones.Select(t=>t.localRotation).ToArray();
  for(int f=0;f<=frames;f++){float time=duration*f/frames;if(loop&&f>0){foreach(var bone in bones)T(bone,time);T(gun.transform,time);continue;}for(int n=0;n<sourceBones.Length;n++)sourceBones[n].SetLocalPositionAndRotation(sourceP[n],sourceQ[n]);evaluated.Sample(Mathf.Min(time,input.length));for(int n=0;n<bones.Length;n++)bones[n].localPosition=targetPositions[n];sh.GetHumanPose(ref pose);pose.bodyPosition=basePose.bodyPosition;pose.bodyRotation=basePose.bodyRotation;th.SetHumanPose(ref pose);const float scale=.938631535f;var anchor=Find(source,"Hand_R").position;var destination=!evaluated.IsThirdPerson?anchor+offset-source.transform.position:Find(arms,"hand_r").position;Vector3 Goal(Vector3 p)=>destination+(p-anchor)*scale;gun.transform.localScale=sg.lossyScale*scale;gun.transform.SetPositionAndRotation(Goal(sg.position),sg.rotation);foreach(var side in new[]{("Left","l"),("Right","r")})Solve(Find(arms,"upperarm_"+side.Item2),Find(arms,"lowerarm_"+side.Item2),Find(arms,"hand_"+side.Item2),Goal(Find(source,"Hand_"+(side.Item2=="l"?"L":"R")).position),Goal(Find(source,"Elbow_"+(side.Item2=="l"?"L":"R")).position));    fitting.Initialize();fitting.Apply(Goal);
    foreach(var side in new[]{"l","r"}){var hand=Find(arms,"hand_"+side);var desired=hand.position;var rotation=hand.rotation;fitting.RestoreHandOffset(hand);Solve(Find(arms,"upperarm_"+side),Find(arms,"lowerarm_"+side),hand,desired,Goal(Find(source,"Elbow_"+(side=="l"?"L":"R")).position));hand.rotation=rotation;}
   fitting.SeatWeapon(gun.transform,Find(arms,"upperarm_l"));
   // ADS placement already aligns the sights. Aimed firing contributes only its
   // authored recoil delta, not the legacy clip's separate absolute aiming pose.
   if(normalizeToRest)
   {
    if(f==0)
    {
     float Read(string prop,int axis)=>AnimationUtility.GetEditorCurve(returnPose,EditorCurveBinding.FloatCurve("SourceWeapon",typeof(Transform),prop+"xyzw"[axis])).Evaluate(0);
     var restPosition=new Vector3(Read("m_LocalPosition.",0),Read("m_LocalPosition.",1),Read("m_LocalPosition.",2));
     var restRotation=new Quaternion(Read("m_LocalRotation.",0),Read("m_LocalRotation.",1),Read("m_LocalRotation.",2),Read("m_LocalRotation.",3));
     alignmentRotation=restRotation*Quaternion.Inverse(gun.transform.rotation);alignmentPosition=restPosition-alignmentRotation*gun.transform.position;
    }
    arms.transform.SetPositionAndRotation(alignmentPosition+alignmentRotation*armsPosition,alignmentRotation*armsRotation);
    gun.transform.SetPositionAndRotation(alignmentPosition+alignmentRotation*gun.transform.position,alignmentRotation*gun.transform.rotation);
   }
   T(arms.transform,time);foreach(var bone in bones)T(bone,time);T(gun.transform,time);
   arms.transform.SetPositionAndRotation(armsPosition,armsRotation);
  }
  if(fitting!=null){Directory.CreateDirectory("Library/AnimationTests");File.WriteAllText("Library/AnimationTests/"+name+"_Fit.txt","Original evaluated RigBuilder IK baked at 60 Hz. Maximum landmark RMS: "+fitting.WorstRms.ToString("F5")+" m");}
  // Reloads need a fitted return pose. Firing already returns to its authored
  // rest; blending its individual joints again breaks the hand-to-gun grip.
  if(!loop&&!input.name.Contains("Fire"))
  {
   var idle=returnPose;if(!idle)throw new Exception("A reload bake requires its fitted idle pose");
   foreach(var path in curves.Keys.Where(b=>b.propertyName=="m_LocalRotation.x").Select(b=>b.path).ToArray())
   {
    var poseBindings=Enumerable.Range(0,4).Select(i=>EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[i])).ToArray();var tracks=poseBindings.Select(b=>curves[b]).ToArray();
    var poses=Enumerable.Range(0,tracks[0].Count).Select(i=>new Quaternion(tracks[0][i].value,tracks[1][i].value,tracks[2][i].value,tracks[3][i].value).normalized).ToArray();
    var restCurves=poseBindings.Select(b=>AnimationUtility.GetEditorCurve(idle,b)).ToArray();var rest=restCurves.All(c=>c!=null)?new Quaternion(restCurves[0].Evaluate(0),restCurves[1].Evaluate(0),restCurves[2].Evaluate(0),restCurves[3].Evaluate(0)).normalized:poses.Last();
    bool finger=path.Contains("thumb_0")||path.Contains("index_0")||path.Contains("ring_0");
    for(int i=0;i<poses.Length;i++)
    {
     var q=poses[i];if(finger&&i>0&&i<poses.Length-1)
     {
      var sum=Vector4.zero;float total=0;int[] weights={1,4,6,4,1};for(int tap=-2;tap<=2;tap++){var sample=poses[Mathf.Clamp(i+tap,0,poses.Length-1)];if(Quaternion.Dot(q,sample)<0)sample=new Quaternion(-sample.x,-sample.y,-sample.z,-sample.w);float weight=weights[tap+2];sum+=new Vector4(sample.x,sample.y,sample.z,sample.w)*weight;total+=weight;}sum/=total;q=new Quaternion(sum.x,sum.y,sum.z,sum.w).normalized;
     }
     float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(duration-.22f,duration,tracks[0][i].time));q=Quaternion.Slerp(q,rest,blend);
     for(int axis=0;axis<4;axis++)tracks[axis][i]=new Keyframe(tracks[axis][i].time,q[axis]);
    }
   }
   foreach(var binding in curves.Keys.Where(b=>b.propertyName.StartsWith("m_LocalPosition")).ToArray())
   {
    var idleCurve=AnimationUtility.GetEditorCurve(idle,binding);if(idleCurve==null)continue;var keys=curves[binding];for(int i=0;i<keys.Count;i++){float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(duration-.22f,duration,keys[i].time));keys[i]=new Keyframe(keys[i].time,Mathf.Lerp(keys[i].value,idleCurve.Evaluate(0),blend));}
   }
  }
  var clip=new AnimationClip{name=name,frameRate=60};var bindings=curves.Keys.ToArray();var output=bindings.Select(b=>{var keys=curves[b];var curve=new AnimationCurve(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<1e-6f)?new[]{keys.First(),keys.Last()}:keys.ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}return curve;}).ToArray();AnimationUtility.SetEditorCurves(clip,bindings,output);
  foreach(var binding in AnimationUtility.GetCurveBindings(parts).Where(b=>b.type==typeof(Transform)&&b.path.Length>0&&gun.transform.Find(b.path))){var b=binding;b.path="SourceWeapon/"+b.path;AnimationUtility.SetEditorCurve(clip,b,AnimationUtility.GetEditorCurve(parts,binding));}
  clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);return Save(clip,(outputFolder??Root)+"/"+name+".anim");
 }
}

