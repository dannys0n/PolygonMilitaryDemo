using System;using System.Linq;using UnityEditor;using UnityEngine;
// Offline contact-aware reload fitting. Original manipulation stays authored; planted hands use the new idle grip.
public static class GripReloadContinuity
{
 sealed class Side
 {
  public Transform upper,forearm,hand;public Transform[] fingers;
  public Vector3 oldP,newP;public Quaternion oldQ,newQ;public Quaternion[] newFingers;
  public Quaternion upperQ,forearmQ;public Vector3 pole;
 }
 static Transform Find(GameObject root,string n)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==n);
 static void Solve(Side side,Vector3 goal,Quaternion rotation)
 {
  var start=side.upper.position;float a=Vector3.Distance(start,side.forearm.position),b=Vector3.Distance(side.forearm.position,side.hand.position);var delta=goal-start;float excess=delta.magnitude-(a+b-.0001f);if(excess>0){var shift=delta.normalized*Mathf.Min(excess,.075f);side.upper.position+=shift;start=side.upper.position;delta=goal-start;}float length=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f);var axis=delta.normalized;
  var bend=Vector3.ProjectOnPlane(side.pole-start,axis).normalized;if(bend.sqrMagnitude<.01f)bend=Vector3.Cross(axis,Vector3.up).normalized;
  float along=(a*a-b*b+length*length)/(2*length);var elbow=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
  side.upper.rotation=Quaternion.FromToRotation(side.forearm.position-start,elbow-start)*side.upper.rotation;
  side.forearm.rotation=Quaternion.FromToRotation(side.hand.position-side.forearm.position,start+axis*length-side.forearm.position)*side.forearm.rotation;side.hand.rotation=rotation;
 }
 public static AnimationClip Bake(GameObject root,Transform gun,Transform[] bones,AnimationClip oldIdle,AnimationClip oldReload,AnimationClip newIdle,string output,float duration)
 {
  var sides=new[]{"r","l"}.Select(s=>new Side{upper=Find(root,"upperarm_"+s),forearm=Find(root,"lowerarm_"+s),hand=Find(root,"hand_"+s),fingers=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.EndsWith("_"+s)&&new[]{"thumb_","index_","middle_","ring_","pinky_"}.Any(t.name.StartsWith)).ToArray()}).ToArray();
  oldIdle.SampleAnimation(root,0);var oldGunP=gun.position;var oldGunQ=gun.rotation;
  foreach(var s in sides){s.oldP=gun.InverseTransformPoint(s.hand.position);s.oldQ=Quaternion.Inverse(gun.rotation)*s.hand.rotation;}
  newIdle.SampleAnimation(root,0);var newGunP=gun.position;var newGunQ=gun.rotation;var turn=newGunQ*Quaternion.Inverse(oldGunQ);
  foreach(var s in sides){s.newP=gun.InverseTransformPoint(s.hand.position);s.newQ=Quaternion.Inverse(gun.rotation)*s.hand.rotation;s.newFingers=s.fingers.Select(t=>t.localRotation).ToArray();s.upperQ=s.upper.localRotation;s.forearmQ=s.forearm.localRotation;s.pole=s.forearm.position;}
  var rest=bones.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();var tracks=bones.Select(t=>new TpsPartsAdapter.PoseTrack{path=AnimationUtility.CalculateTransformPath(t,root.transform),source=t}).ToArray();
  int frames=Mathf.CeilToInt(duration*60);var times=new float[frames+1];
  for(int f=0;f<=frames;f++)
  {
   for(int i=0;i<bones.Length;i++){bones[i].SetLocalPositionAndRotation(rest[i].localPosition,rest[i].localRotation);bones[i].localScale=rest[i].localScale;}
   float phase=(float)f/frames,time=duration*phase;times[f]=time;oldReload.SampleAnimation(root,oldReload.length*phase);
   var originalGunP=gun.position;var originalGunQ=gun.rotation;
   var positions=sides.Select(s=>gun.InverseTransformPoint(s.hand.position)).ToArray();var rotations=sides.Select(s=>Quaternion.Inverse(gun.rotation)*s.hand.rotation).ToArray();
   gun.SetPositionAndRotation(newGunP+turn*(originalGunP-oldGunP),turn*originalGunQ);
   float edge=Mathf.Max(1-Mathf.SmoothStep(0,1,Mathf.Clamp01(time/.14f)),1-Mathf.SmoothStep(0,1,Mathf.Clamp01((duration-time)/.14f)));
   var goalPositions=new Vector3[sides.Length];var goalRotations=new Quaternion[sides.Length];
   for(int i=0;i<sides.Length;i++)
   {
    var s=sides[i];float distance=Vector3.Distance(positions[i],s.oldP),angle=Quaternion.Angle(rotations[i],s.oldQ);
    float contact=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.018f,.075f,distance)))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(15,65,angle)));contact=Mathf.Max(contact,edge);
    // Wrist trajectory remains relative to the moving receiver. During planted portions it seats exactly on the new grip.
    var p=Vector3.Lerp(positions[i],s.newP,contact);var q=Quaternion.Slerp(rotations[i],s.newQ,contact);
    s.upper.localRotation=Quaternion.Slerp(s.upper.localRotation,s.upperQ,contact);s.forearm.localRotation=Quaternion.Slerp(s.forearm.localRotation,s.forearmQ,contact);
    goalPositions[i]=p;goalRotations[i]=q;
    for(int k=0;k<s.fingers.Length;k++)s.fingers[k].localRotation=Quaternion.Slerp(s.fingers[k].localRotation,s.newFingers[k],contact);
   }
   // Exact local endpoints avoid any pose-pop across state transitions, including hidden IK helper bones.
   for(int i=0;i<bones.Length;i++)if(!bones[i].IsChildOf(gun)||bones[i]==gun){bones[i].localPosition=Vector3.Lerp(bones[i].localPosition,rest[i].localPosition,edge);bones[i].localRotation=Quaternion.Slerp(bones[i].localRotation,rest[i].localRotation,edge);bones[i].localScale=Vector3.Lerp(bones[i].localScale,rest[i].localScale,edge);}
   for(int i=0;i<sides.Length;i++)Solve(sides[i],gun.TransformPoint(goalPositions[i]),gun.rotation*goalRotations[i]);
   foreach(var track in tracks)track.Sample();
  }
  var clip=new AnimationClip{frameRate=60};foreach(var track in tracks)TpsPartsAdapter.Write(clip,track,times);return WeaponReloadBuilder.Save(clip,output);
 }
}
