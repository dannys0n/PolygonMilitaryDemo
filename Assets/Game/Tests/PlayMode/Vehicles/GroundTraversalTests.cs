#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Minimal physical fixtures exercise the production capsule, not a parallel movement solver.
public class GroundTraversalTests
{
    readonly List<GameObject> objects=new();
    Component motor;
    Rigidbody body;
    static Type MotorType=>UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t=>t.Name=="SoldierPhysicsBody");
    bool Grounded=>(bool)MotorType.GetProperty("isGrounded").GetValue(motor);
    void Traverse(Vector3 direction)=>MotorType.GetMethod("TraverseGround").Invoke(motor,new object[]{direction,6f});
    void Command(string method)=>MotorType.GetMethod(method).Invoke(motor,null);
    GameObject Box(Vector3 position,Vector3 scale,Quaternion rotation=default)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(go);
        go.transform.SetPositionAndRotation(position,rotation==default?Quaternion.identity:rotation);go.transform.localScale=scale;return go;
    }
    IEnumerator Actor(Vector3 position)
    {
        var go=new GameObject("Ground traversal fixture");objects.Add(go);go.SetActive(false);go.transform.position=position;
        body=go.AddComponent<Rigidbody>();body.mass=80;
        var capsule=go.AddComponent<CapsuleCollider>();capsule.radius=.3f;capsule.height=1.8f;capsule.center=Vector3.up*.9f;
        motor=go.AddComponent(MotorType);MotorType.GetField("body").SetValue(motor,body);MotorType.GetField("capsule").SetValue(motor,capsule);
        go.SetActive(true);yield return new WaitForSeconds(.3f);
    }
    [UnityTearDown] public IEnumerator Cleanup(){foreach(var go in objects)if(go)UnityEngine.Object.Destroy(go);objects.Clear();yield return null;}
    IEnumerator Walk(Vector3 direction,float seconds,Action sample=null)
    {
        for(float t=0;t<seconds;t+=Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();Traverse(direction);
            var normal=(Vector3)MotorType.GetProperty("GroundNormal").GetValue(motor);
            var desired=Vector3.ProjectOnPlane(direction,Grounded?normal:Vector3.up).normalized*4;
            MotorType.GetMethod("AccelerateTowards").Invoke(motor,new object[]{desired,Grounded?normal:Vector3.up,45f,Time.fixedDeltaTime});sample?.Invoke();
        }
    }
    [UnityTest] public IEnumerator FlatGroundNeedsNoGraceStateAndSmallStepCanBeTraversed()
    {
        Box(new Vector3(0,-.5f,0),new Vector3(20,1,20));Box(new Vector3(0,.1f,2),new Vector3(3,.2f,2));
        yield return Actor(Vector3.zero);Assert.IsTrue(Grounded);
        string trace="";
        yield return Walk(Vector3.forward,.8f,()=>{if(body.position.z>.65f&&body.position.z<1.1f)trace+=" feet="+body.position.ToString("F3")+" velocity="+body.linearVelocity.ToString("F3")+" grounded="+Grounded+" riser="+MotorType.GetField("stepContact",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(motor);});
        Assert.Greater(body.position.z,1.5f,"Capsule must climb a 20cm step instead of sticking to its riser."+trace);
        Assert.Greater(body.position.y,.15f);Assert.IsTrue(Grounded,"Stepping does not become a jump");
    }
    [UnityTest] public IEnumerator DescendingOneStepRetainsGroundButRealJumpDoesNot()
    {
        Box(new Vector3(0,-.5f,0),new Vector3(20,1,20));Box(new Vector3(0,.1f,0),new Vector3(3,.2f,2));
        yield return Actor(new Vector3(0,.2f,0));int airborne=0;string trace="";
        yield return Walk(Vector3.forward,.8f,()=>{if(!Grounded){airborne++;trace+=" feet="+body.position.ToString("F3")+" velocity="+body.linearVelocity.ToString("F3");}});
        Assert.LessOrEqual(airborne,1,"A supported step down should not trigger a floating/jump interval."+trace);
        Command("BeginJump");body.AddForce(Vector3.up*5,ForceMode.VelocityChange);
        yield return new WaitForFixedUpdate();Traverse(Vector3.forward);Assert.IsFalse(Grounded);
        yield return new WaitForSeconds(.15f);Traverse(Vector3.forward);Assert.IsFalse(Grounded,"Jump must not be snapped back to nearby ground");
    }
    [UnityTest] public IEnumerator RampCrestDoesNotLaunchButLargeLedgeAndFastFallsStayAirborne()
    {
        Box(new Vector3(0,-.5f,0),new Vector3(20,1,20));
        // 15 degree ramp meeting the floor at its low end, with a level landing at its crest.
        Box(new Vector3(0,.35f,2),new Vector3(3,.2f,3),Quaternion.Euler(-15,0,0));
        Box(new Vector3(0,.3f,4.6f),new Vector3(3,.6f,2.3f));
        yield return Actor(new Vector3(0,0,.2f));
        yield return Walk(Vector3.forward,1.4f);
        Assert.Less(body.position.y,.95f,"Walking a ramp crest should not launch the capsule");
        body.position=new Vector3(0,10,0);Command("ResetMotion");body.linearVelocity=new Vector3(30,-30,0);
        yield return new WaitForFixedUpdate();Traverse(Vector3.forward);Assert.IsFalse(Grounded,"No remote support or plane momentum snapping");
    }
    [UnityTest] public IEnumerator StepUnderLowRoofDoesNotPassTheStandingCapsuleThroughIt()
    {
        Box(new Vector3(0,-.5f,0),new Vector3(20,1,20));Box(new Vector3(0,.1f,2),new Vector3(3,.2f,2));
        Box(new Vector3(0,1.95f,1.8f),new Vector3(3,.2f,2));
        yield return Actor(Vector3.zero);yield return Walk(Vector3.forward,.8f);
        Assert.Less(body.position.z,1.2f,"Step clearance must test the entire raised capsule");
        Assert.Less(body.position.y,.15f,"Do not teleport upward into a low roof");
    }
    [UnityTest] public IEnumerator SteepSurfaceDoesNotBecomeWalkableGround()
    {
        Box(new Vector3(0,0,0),new Vector3(4,.2f,6),Quaternion.Euler(65,0,0));
        yield return Actor(new Vector3(0,1,0));Traverse(Vector3.zero);
        Assert.IsFalse(Grounded,"Ground probing must retain the existing 50 degree slope limit");
    }
    [UnityTest] public IEnumerator TallStepDoesNotAllowCapsuleTeleport()
    {
        Box(new Vector3(0,-.5f,0),new Vector3(20,1,20));Box(new Vector3(0,.3f,2),new Vector3(3,.6f,2));
        yield return Actor(Vector3.zero);yield return Walk(Vector3.forward,.8f);
        Assert.Less(body.position.z,1.1f);Assert.Less(body.position.y,.2f,"A step above stepOffset must remain an obstacle");
    }
}
#endif
