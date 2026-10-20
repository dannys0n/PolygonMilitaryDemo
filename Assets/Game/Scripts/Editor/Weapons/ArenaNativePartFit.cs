using System;using System.Collections.Generic;using System.Linq;using UnityEngine;
// Offline part-motion retargeting: preserve each model's own hinges and bind axes.
public sealed class ArenaNativePartFit
{
 sealed class Binding
 {
  public Transform source,target;public Vector3 p,s,sourceP,sourceS;public Quaternion q,sourceQ,axes;public bool followsParent;public Transform frame;public Matrix4x4 sourceRest,targetRest;public bool projectile;public Quaternion trajectoryAxes=Quaternion.identity;public Vector3 sourceAnchor,targetAnchor;
 }
 readonly List<Binding> bindings=new();
 sealed class Contact{public Transform source,target;public Matrix4x4 sourceRest,targetRest;public Bounds sourceBounds,targetBounds;}
 readonly List<Contact> contacts=new();Transform upper,forearm,hand;Vector3 palmOffset=new Vector3(.04f,0,0);
 static Bounds BoundsOf(IEnumerable<Vector3> points){var values=points.ToArray();var b=new Bounds(values[0],Vector3.zero);foreach(var p in values)b.Encapsulate(p);return b;}
 public void ConfigureHand(GameObject fps,Transform gun,Dictionary<Transform,List<Vector3>> native,Dictionary<Transform,List<Vector3>> polygon,Dictionary<Transform,Transform> mapping,Dictionary<Transform,Transform> targets)
 {
  var bones=fps.GetComponentsInChildren<Transform>(true);upper=bones.First(t=>t.name=="upperarm_l");forearm=bones.First(t=>t.name=="lowerarm_l");hand=bones.First(t=>t.name=="hand_l");
  foreach(var group in polygon.GroupBy(p=>mapping[p.Key]))
  {
   if(!new[]{"Rocket","Mag","Magazine","Bag","Drum","Top","ChargingHandle","Lever","Rail","Feed_Tray","Cartridge0","Cartridge1","Cartridge2","Cartridge3","Cartridge4","Cartridge5"}.Contains(group.Key.name)||!native.TryGetValue(group.Key,out var source)||source.Count<3)continue;
   var points=group.SelectMany(p=>p.Value).ToArray();if(points.Length<3)continue;
   var first=group.First(p=>p.Value.Count>0);contacts.Add(new Contact{source=group.Key,target=targets[first.Key],sourceRest=group.Key.localToWorldMatrix,targetRest=targets[first.Key].localToWorldMatrix,sourceBounds=BoundsOf(source),targetBounds=BoundsOf(points)});
  }
  foreach(var b in bindings.Where(b=>b.projectile))
  {
   var sourcePoints=native[b.source];var targetPoints=polygon.Where(p=>mapping[p.Key]==b.source).SelectMany(p=>p.Value).ToArray();
   var sourceCenter=BoundsOf(sourcePoints).center;var targetCenter=BoundsOf(targetPoints).center;
   Vector3 Axis(IEnumerable<Vector3> input){var values=input.ToArray();var center=values.Aggregate(Vector3.zero,(sum,p)=>sum+p)/values.Length;Vector3 direction=gun.forward;for(int i=0;i<12;i++){Vector3 next=Vector3.zero;foreach(var p in values){var delta=p-center;next+=delta*Vector3.Dot(delta,direction);}direction=next.normalized;}if(Vector3.Dot(direction,gun.forward)<0)direction=-direction;return direction;}
   b.trajectoryAxes=Quaternion.Inverse(gun.rotation)*Quaternion.FromToRotation(Axis(sourcePoints),Axis(targetPoints))*gun.rotation;
   b.sourceAnchor=b.source.InverseTransformPoint(sourceCenter);b.targetAnchor=b.target.InverseTransformPoint(targetCenter);
  }
 }
 void FitSupportHand()
 {
  if(!hand||contacts.Count==0)return;var palm=hand.TransformPoint(palmOffset);Vector3 shift=Vector3.zero;float total=0;
  foreach(var c in contacts)
  {
   if(c.source.lossyScale.sqrMagnitude<.001f)continue;
   var bind=c.sourceRest.MultiplyPoint3x4(c.source.worldToLocalMatrix.MultiplyPoint3x4(palm));var closest=c.sourceBounds.ClosestPoint(bind);float distance=Vector3.Distance(bind,closest);if(distance>.105f)continue;
   float weight=Mathf.Exp(-distance*distance/(2*.035f*.035f));var size=c.sourceBounds.size;var targetSize=c.targetBounds.size;
   var ratio=new Vector3(size.x>.005f?Mathf.Clamp(targetSize.x/size.x,.7f,1.3f):1,size.y>.005f?Mathf.Clamp(targetSize.y/size.y,.7f,1.3f):1,size.z>.005f?Mathf.Clamp(targetSize.z/size.z,.7f,1.3f):1);
   var targetBind=c.targetBounds.center+Vector3.Scale(bind-c.sourceBounds.center,ratio);
   var targetWorld=c.target.TransformPoint(c.targetRest.inverse.MultiplyPoint3x4(targetBind));shift+=(targetWorld-palm)*weight;total+=weight;
  }
  if(total<.001f)return;shift=Vector3.ClampMagnitude(shift/Mathf.Max(1,total),.075f);var goal=hand.position+shift;var pole=forearm.position;var wrist=hand.rotation;
  var start=upper.position;float a=Vector3.Distance(start,forearm.position),b=Vector3.Distance(forearm.position,hand.position);var direction=goal-start;float length=Mathf.Clamp(direction.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f);var axis=direction.normalized;var bend=Vector3.ProjectOnPlane(pole-start,axis).normalized;if(bend.sqrMagnitude<.01f)bend=Vector3.Cross(axis,Vector3.up).normalized;
  float along=(a*a-b*b+length*length)/(2*length);var elbow=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));upper.rotation=Quaternion.FromToRotation(forearm.position-start,elbow-start)*upper.rotation;forearm.rotation=Quaternion.FromToRotation(hand.position-forearm.position,start+axis*length-forearm.position)*forearm.rotation;hand.rotation=wrist;
 }

 public void Add(Transform source,Transform target,Transform nativeRoot,Transform targetRoot,bool followsParent)
 {
  Quaternion nativeParent=source.parent?Quaternion.Inverse(nativeRoot.rotation)*source.parent.rotation:Quaternion.identity;
  Quaternion targetParent=target.parent?Quaternion.Inverse(targetRoot.rotation)*target.parent.rotation:Quaternion.identity;
  bindings.Add(new Binding{source=source,target=target,p=target.localPosition,q=target.localRotation,s=target.localScale,sourceP=source.localPosition,sourceQ=source.localRotation,sourceS=source.localScale,axes=Quaternion.Inverse(targetParent)*nativeParent,followsParent=followsParent,frame=nativeRoot,sourceRest=nativeRoot.worldToLocalMatrix*source.localToWorldMatrix,targetRest=nativeRoot.worldToLocalMatrix*target.localToWorldMatrix,projectile=source.name=="Rocket"});
 }
 public void Sample(bool adjustHand=true)
 {
  foreach(var b in bindings)
  {
   if(b.followsParent){b.target.SetLocalPositionAndRotation(b.p,b.q);b.target.localScale=b.s;continue;}
   if(b.projectile)
   {
    if(b.source.lossyScale.sqrMagnitude<.001f){b.target.localScale=Vector3.zero;continue;}
    // Retarget the carried round around its geometry center, and rotate its
    // travel frame onto this model's shaft axis so insertion follows its tube.
    var sourceNow=b.frame.worldToLocalMatrix*b.source.localToWorldMatrix;
    var delta=b.trajectoryAxes*(sourceNow.rotation*Quaternion.Inverse(b.sourceRest.rotation))*Quaternion.Inverse(b.trajectoryAxes);
    var rotation=delta*b.targetRest.rotation;
    var center=b.targetRest.MultiplyPoint3x4(b.targetAnchor)+b.trajectoryAxes*(sourceNow.MultiplyPoint3x4(b.sourceAnchor)-b.sourceRest.MultiplyPoint3x4(b.sourceAnchor));
    b.target.SetPositionAndRotation(b.frame.TransformPoint(center-rotation*b.targetAnchor),b.frame.rotation*rotation);
    b.target.localScale=b.s;continue;
   }
   var turn=b.axes*(b.source.localRotation*Quaternion.Inverse(b.sourceQ))*Quaternion.Inverse(b.axes);
   b.target.SetLocalPositionAndRotation(b.p+b.axes*(b.source.localPosition-b.sourceP),turn*b.q);
   Vector3 ratio=new Vector3(b.sourceS.x==0?1:b.source.localScale.x/b.sourceS.x,b.sourceS.y==0?1:b.source.localScale.y/b.sourceS.y,b.sourceS.z==0?1:b.source.localScale.z/b.sourceS.z);b.target.localScale=Vector3.Scale(b.s,ratio);
  }
  if(adjustHand)FitSupportHand();
 }
}

