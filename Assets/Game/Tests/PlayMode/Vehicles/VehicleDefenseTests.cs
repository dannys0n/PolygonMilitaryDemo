#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using Object=UnityEngine.Object;

// Isolate tests without changing the game's predefined Assembly-CSharp boundary.
public class VehicleDefenseTests
{
    readonly List<GameObject> objects=new List<GameObject>();
    readonly List<Component> vehicles=new List<Component>();
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static Type T(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).First(t=>t!=null);
    static object Get(object o,string name){var p=o.GetType().GetProperty(name,flags);return p!=null?p.GetValue(o):o.GetType().GetField(name,flags).GetValue(o);}
    static void Set(object o,string name,object value)
    {
        var field=o.GetType().GetField(name,flags);
        if(field==null && o.GetType().Name=="JetMotor")
        {
            o=Get(Get(o,"vehicle"),"jetAssist");field=o.GetType().GetField(name,flags);
        }
        field.SetValue(o,value);
    }
    static void EnumField(object o,string name,string value)=>Set(o,name,Enum.Parse(o.GetType().GetField(name,flags).FieldType,value));
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,flags).Invoke(o,args);
    static void SimulateFlight(Component motor,object input,bool powered)
    {
        input=Call(Get(motor,"vehicle"),"PrepareDriverInput",input,powered);
        Call(motor,"Simulate",input,powered);
    }
    GameObject Root(string name){var g=new GameObject(name);objects.Add(g);return g;}
    Component Vehicle(string name,int faction,Vector3 position)
    {
        var root=Root(name);root.SetActive(false);root.transform.position=position;
        var body=root.AddComponent<Rigidbody>();body.useGravity=false;
        var v=(Behaviour)root.AddComponent(T("Vehicle"));v.enabled=false;
        // Aircraft classification now follows the motor, not the decorative instrument preset.
        var motor=(Behaviour)root.AddComponent(T("JetMotor"));motor.enabled=false;Set(v,"motor",motor);Set(motor,"vehicle",v);
        var seat=Activator.CreateInstance(T("Vehicle").GetNestedType("Seat"));
        var actorRoot=Root(name+" operator");actorRoot.SetActive(false);
        var actor=actorRoot.AddComponent(T("VehicleOccupant"));Set(actor,"faction",faction);
        var soldier=(Behaviour)actorRoot.AddComponent(T("SoldierController"));soldier.enabled=false;Set(actor,"soldier",soldier);
        var life=(Behaviour)actorRoot.AddComponent(T("SoldierLife"));life.enabled=false;
        Set(life,"health",actorRoot.AddComponent(T("SoldierHealth")));Set(actor,"life",life);
        seat.GetType().GetField("occupant").SetValue(seat,actor);
        foreach(string n in new[]{"anchor","exit","eye"})seat.GetType().GetField(n).SetValue(seat,root.transform);
        var seats=Array.CreateInstance(seat.GetType(),1);seats.SetValue(seat,0);Set(v,"seats",seats);
        Set(v,"cameraTarget",root.transform);EnumField(v,"instruments","FixedWing");
        root.SetActive(true);((IList)T("Vehicle").GetField("Active").GetValue(null)).Add(v);vehicles.Add(v);return v;
    }
    Component Missile(string seeker,string guidance,out Component target)
    {
        var source=Vehicle("Source",0,new Vector3(0,1000,0));target=Vehicle("Target",1,new Vector3(0,1000,100));
        var root=Root("Seeker");root.transform.position=new Vector3(0,1000,5);
        var body=root.AddComponent<Rigidbody>();body.useGravity=false;body.linearVelocity=Vector3.forward*30;
        var m=root.AddComponent(T("VehicleMissileGuidance"));EnumField(m,"seeker",seeker);EnumField(m,"guidance",guidance);Set(m,"seekerDelay",0f);
        Call(m,"Initialize",source,new Ray(new Vector3(0,1000,0),Vector3.forward),null,null);
        Assert.AreEqual(target,Get(m,"Target"),"Seeker must acquire the hostile vehicle");return m;
    }
    Component Decoy(string signature)
    {
        var root=Root("Decoy");root.SetActive(false);root.transform.position=new Vector3(0,1000,95);
        var c=root.AddComponent(T("VehicleDecoy"));EnumField(c,"signature",signature);root.SetActive(true);return c;
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        var registry=(IList)T("Vehicle").GetField("Active").GetValue(null);foreach(var v in vehicles)registry.Remove(v);
        foreach(Component d in ((IList)T("VehicleDecoy").GetField("Active").GetValue(null)).Cast<Component>().ToArray())if(d)Object.Destroy(d.gameObject);
        foreach(Component c in ((IList)T("GrenadeSmoke").GetField("clouds",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Cast<Component>().ToArray())if(c)Object.Destroy(c.gameObject);
        foreach(var g in objects)if(g)Object.Destroy(g);objects.Clear();vehicles.Clear();yield return null;
    }
    [UnityTest] public IEnumerator InfraredSeekerFollowsFlare()
    {var m=Missile("Infrared","AirToAir",out _);var d=Decoy("Infrared");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(d,Get(m,"decoy"));Assert.IsNull(Get(m,"Target"));}
    [UnityTest] public IEnumerator InfraredSeekerIgnoresChaff()
    {var m=Missile("Infrared","AirToAir",out var target);Decoy("Radar");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator RadarSeekerFollowsChaff()
    {var m=Missile("Radar","AirDefence",out _);var d=Decoy("Radar");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(d,Get(m,"decoy"));}
    [UnityTest] public IEnumerator RadarSeekerIgnoresFlare()
    {var m=Missile("Radar","AirDefence",out var target);Decoy("Infrared");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator OpticalGuidanceIgnoresAircraftDecoys()
    {var m=Missile("Optical","LaserSpot",out var target);Decoy("Infrared");Decoy("Radar");yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator SmokeBreaksOpticalGuidance()
    {var m=Missile("Optical","LaserSpot",out _);var cloud=Root("Obscurant");cloud.transform.position=new Vector3(0,1000,60);cloud.AddComponent(T("GrenadeSmoke"));yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.IsNull(Get(m,"Target"));}
    [UnityTest] public IEnumerator DistantFlareDoesNotProvideGlobalImmunity()
    {var m=Missile("Infrared","AirToAir",out var target);var d=Decoy("Infrared");d.transform.position=new Vector3(0,1000,250);yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));}
    [UnityTest] public IEnumerator FlaresRechargeWithoutExhaustingDeployments()
    {
        var v=Vehicle("Dispenser",0,new Vector3(0,1000,0));var c=v.gameObject.AddComponent(T("VehicleCountermeasures"));
        Set(c,"automaticForAI",false);Set(c,"flares",true);Set(c,"dispensers",new Transform[0]);Set(c,"cooldown",.1f);Set(c,"rechargeSeconds",.2f);
        for(int i=0;i<5;i++)
        {
            Assert.IsTrue((bool)Call(c,"Deploy"));Assert.AreEqual(2,Get(c,"Charges"));
            Assert.IsFalse((bool)Call(c,"Deploy"));yield return new WaitForSeconds(.12f);
            Assert.IsFalse((bool)Call(c,"Deploy"),"Recharge must finish, not only the shorter cooldown");
            yield return new WaitForSeconds(.12f);
        }
    }
    [UnityTest] public IEnumerator SmokeRetainsItsBoundedRechargeableChargePool()
    {
        var v=Vehicle("Smoke dispenser",0,new Vector3(0,1000,0));var c=v.gameObject.AddComponent(T("VehicleCountermeasures"));
        Set(c,"automaticForAI",false);Set(c,"smoke",true);Set(c,"dispensers",new Transform[0]);Set(c,"cooldown",.1f);Set(c,"rechargeSeconds",1f);
        Assert.IsTrue((bool)Call(c,"Deploy"));Assert.IsFalse((bool)Call(c,"Deploy"));yield return new WaitForSeconds(.15f);
        Assert.IsTrue((bool)Call(c,"Deploy"));Assert.AreEqual(0,Get(c,"Charges"));Assert.IsFalse((bool)Call(c,"Deploy"));
        yield return new WaitForSeconds(1.1f);Assert.GreaterOrEqual((int)Get(c,"Charges"),1);Assert.LessOrEqual((int)Get(c,"Charges"),2);
    }
    [UnityTest] public IEnumerator ExpiredDecoysLeaveRegistry()
    {var d=Decoy("Infrared");Set(d,"lifetime",.05f);d.gameObject.SetActive(false);d.gameObject.SetActive(true);yield return new WaitForSeconds(.15f);Assert.IsFalse((bool)d);Assert.AreEqual(0,((IList)T("VehicleDecoy").GetField("Active").GetValue(null)).Count);}
    [UnityTest] public IEnumerator AllVehiclePrefabsStartWithoutRuntimeErrors()
    {
        int count=0;foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Game/Prefabs/Vehicles"}))
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));if(!prefab.GetComponent(T("Vehicle")))continue;
            var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);count++;
            yield return new WaitForFixedUpdate();yield return null;Object.Destroy(root);yield return null;
        }
        Assert.GreaterOrEqual(count,34);
    }
    [UnityTest] public IEnumerator JetProducesThrustWithoutHoverLiftAtRest()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;var motor=root.GetComponent(T("JetMotor"));var input=Activator.CreateInstance(T("VehicleInput"));
        yield return null;for(int i=0;i<10;i++){SimulateFlight(motor,input,true);yield return new WaitForFixedUpdate();}
        Assert.Greater(root.GetComponent<Rigidbody>().linearVelocity.z,.1f);Assert.Greater((float)Get(motor,"EnginePower"),0);Assert.Less(root.GetComponent<Rigidbody>().linearVelocity.y,-.2f);
    }
    [UnityTest] public IEnumerator JetsRespondFasterWithoutBypassingRigidBodyFlight()
    {
        foreach(string name in new[]{"Jet01","Jet02"})
        foreach(bool assisted in new[]{false,true})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/"+name+".prefab");
            var roots=new GameObject[2];var motors=new Component[2];
            for(int i=0;i<2;i++)
            {
                roots[i]=Object.Instantiate(prefab,new Vector3(i*500,1000,0),Quaternion.identity);objects.Add(roots[i]);
                ((Behaviour)roots[i].GetComponent(T("Vehicle"))).enabled=false;
                motors[i]=roots[i].GetComponent(T("JetMotor"));Set(motors[i],"assistEnabled",assisted);
                roots[i].GetComponent<Rigidbody>().linearVelocity=Vector3.forward*50;
            }
            // Compare against the preceding airframe tune using the same forces and starting airspeed.
            Set(motors[0],"pitchRate",65f);Set(motors[0],"rollRate",120f);Set(motors[0],"yawRate",28f);
            Set(motors[0],"controlResponse",5f);Set(motors[0],"maximumAngularAcceleration",12f);
            Set(motors[0],"referenceControlSpeed",65f);Set(motors[0],"maximumBank",60f);Set(motors[0],"headingToBank",1.4f);
            var input=Activator.CreateInstance(T("VehicleInput"));Set(input,"directionalFlight",assisted);
            Set(input,"heading",60f);Set(input,"viewPitch",-15f);Set(input,"pitchRate",-.5f);Set(input,"move",new Vector2(.5f,0));
            yield return null;
            for(int step=0;step<25;step++)
            {for(int i=0;i<2;i++)SimulateFlight(motors[i],input,true);yield return new WaitForFixedUpdate();}
            float previous=Quaternion.Angle(Quaternion.identity,roots[0].transform.rotation);
            float tuned=Quaternion.Angle(Quaternion.identity,roots[1].transform.rotation);
            Assert.Greater(tuned,previous*1.1f,name+" must respond sooner in "+(assisted?"assisted":"manual")+" flight");
            Assert.Less(roots[1].GetComponent<Rigidbody>().angularVelocity.magnitude,4.01f,"Keep the physical angular-speed limit");
            TestContext.WriteLine(name+" "+(assisted?"assisted":"manual")+": rotation after 0.5 s "+previous.ToString("F1")+" -> "+tuned.ToString("F1")+" degrees");
            foreach(var root in roots)Object.Destroy(root);yield return null;
        }
    }
    [UnityTest] public IEnumerator AssistedJetsTurnTheirFlightPathWithoutADS()
    {
        foreach(string name in new[]{"Jet01","Jet02"})
        {
            var roots=new GameObject[2];var motors=new Component[2];var inputs=new object[2];
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/"+name+".prefab");
            for(int i=0;i<2;i++)
            {
                roots[i]=Object.Instantiate(prefab,new Vector3(i*500,1000,0),Quaternion.identity);objects.Add(roots[i]);
                ((Behaviour)roots[i].GetComponent(T("Vehicle"))).enabled=false;
                motors[i]=roots[i].GetComponent(T("JetMotor"));Set(motors[i],"assistEnabled",true);
                roots[i].GetComponent<Rigidbody>().linearVelocity=Vector3.forward*100;
                inputs[i]=Activator.CreateInstance(T("VehicleInput"));Set(inputs[i],"directionalFlight",true);
                Set(inputs[i],"heading",60f);Set(inputs[i],"aim",i==1);
            }
            yield return null;
            for(int step=0;step<150;step++)
            {for(int i=0;i<2;i++)SimulateFlight(motors[i],inputs[i],true);yield return new WaitForFixedUpdate();}
            for(int i=0;i<2;i++)
            {
                var body=roots[i].GetComponent<Rigidbody>();
                float pathHeading=Mathf.Atan2(body.linearVelocity.x,body.linearVelocity.z)*Mathf.Rad2Deg;
                float noseHeading=roots[i].transform.eulerAngles.y;
                Assert.Greater(pathHeading,20,"The flight path must turn, not just the rendered nose");
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(noseHeading,pathHeading)),15,"Nose and actual trajectory must remain coordinated");
                Assert.Less(Vector3.Angle(roots[i].transform.forward,Quaternion.Euler(0,60,0)*Vector3.forward),8,"Assist must directly align the nose to the aim direction");
                TestContext.WriteLine(name+" ADS="+(i==1)+": path="+pathHeading.ToString("F1")+", nose="+noseHeading.ToString("F1"));
            }
            Assert.Less(Quaternion.Angle(roots[0].transform.rotation,roots[1].transform.rotation),.5f,"ADS must not enable or change jet steering");
            foreach(var root in roots)Object.Destroy(root);yield return null;
        }
    }
    [UnityTest] public IEnumerator JetRearTargetDoesNotFlipTurnDirection()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");
        var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;
        var motor=root.GetComponent(T("JetMotor"));Set(motor,"assistEnabled",true);
        root.GetComponent<Rigidbody>().linearVelocity=Vector3.forward*100;
        var input=Activator.CreateInstance(T("VehicleInput"));Set(input,"directionalFlight",true);
        yield return null;
        for(int step=0;step<60;step++)
        {
            // Mouse jitter around directly behind must not repeatedly reverse the physical bank.
            Set(input,"heading",root.transform.eulerAngles.y+(step%2==0?179f:181f));
            SimulateFlight(motor,input,true);yield return new WaitForFixedUpdate();
            if(step>15)
            {
                Vector3 right=Quaternion.Euler(0,root.transform.eulerAngles.y,0)*Vector3.right;
                Assert.Greater(Vector3.Dot(root.transform.up,right),0,"Keep banking to the chosen side of the 180-degree turn");
            }
        }
        var velocity=root.GetComponent<Rigidbody>().linearVelocity;
        Assert.Greater(Mathf.Atan2(velocity.x,velocity.z)*Mathf.Rad2Deg,5,"The sustained bank must turn the path");
    }
    [UnityTest] public IEnumerator JetAcceleratesFasterAndSettlesBelowReducedSpeedLimit()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");
        var roots=new GameObject[2];var motors=new Component[2];
        for(int i=0;i<2;i++)
        {
            roots[i]=Object.Instantiate(prefab,new Vector3(i*500,1000,0),Quaternion.identity);objects.Add(roots[i]);
            ((Behaviour)roots[i].GetComponent(T("Vehicle"))).enabled=false;
            motors[i]=roots[i].GetComponent(T("JetMotor"));Set(motors[i],"assistEnabled",true);
            roots[i].GetComponent<Rigidbody>().linearVelocity=Vector3.forward*25;
        }
        Set(motors[0],"maximumThrust",180000f);Set(motors[0],"engineResponse",1.5f);Set(motors[0],"throttleChangeRate",.55f);Set(motors[0],"maxSpeed",180f);
        var input=Activator.CreateInstance(T("VehicleInput"));Set(input,"directionalFlight",true);Set(input,"move",Vector2.up);Set(input,"boost",true);
        yield return null;
        for(int step=0;step<50;step++)
        {for(int i=0;i<2;i++)SimulateFlight(motors[i],input,true);yield return new WaitForFixedUpdate();}
        Assert.Greater(roots[1].GetComponent<Rigidbody>().linearVelocity.magnitude,roots[0].GetComponent<Rigidbody>().linearVelocity.magnitude+10,"Faster acceleration from flight speed");
        for(int step=0;step<400;step++){SimulateFlight(motors[1],input,true);yield return new WaitForFixedUpdate();}
        Assert.Less(roots[1].GetComponent<Rigidbody>().linearVelocity.magnitude,(float)Get(motors[1],"maxSpeed")*1.05f,"Afterburner must respect the lower speed envelope");
    }
    [UnityTest] public IEnumerator JetsBankThroughReversalWithVisibleControlSurfaces()
    {
        foreach(string name in new[]{"Jet01","Jet02"})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/"+name+".prefab");
            var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
            ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;
            var motor=root.GetComponent(T("JetMotor"));Set(motor,"assistEnabled",true);
            var surfaces=(Array)Get(motor,"controlSurfaces");Assert.GreaterOrEqual(surfaces.Length,4);
            root.GetComponent<Rigidbody>().linearVelocity=Vector3.forward*55;
            var input=Activator.CreateInstance(T("VehicleInput"));Set(input,"directionalFlight",true);Set(input,"heading",180f);
            float visibleMovement=0,maximumBank=0;
            yield return null;
            for(int step=0;step<350;step++)
            {
                SimulateFlight(motor,input,true);yield return new WaitForFixedUpdate();
                foreach(var surface in surfaces)
                {
                    var part=(Transform)Get(surface,"part");
                    visibleMovement=Mathf.Max(visibleMovement,Quaternion.Angle((Quaternion)Get(surface,"restRotation"),part.localRotation));
                }
                maximumBank=Mathf.Max(maximumBank,Mathf.Abs(Mathf.DeltaAngle(0,root.transform.eulerAngles.z)));
            }
            var body=root.GetComponent<Rigidbody>();float pathHeading=Mathf.Atan2(body.linearVelocity.x,body.linearVelocity.z)*Mathf.Rad2Deg;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(pathHeading,180)),45,"Complete most of a physical reversal within seven seconds");
            Assert.Greater(maximumBank,45,"A reversal must visibly bank the aircraft");
            Assert.Greater(visibleMovement,5,"Control surfaces must visibly articulate");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(root.transform.eulerAngles.y,pathHeading)),15,"Rendered direction follows flight path");
            Object.Destroy(root);yield return null;
        }
    }
    [UnityTest] public IEnumerator JetFinTravelDrivesSteeringAndNeedsAirflow()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");
        var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;
        var motor=root.GetComponent(T("JetMotor"));Set(motor,"assistEnabled",false);
        var body=root.GetComponent<Rigidbody>();body.useGravity=false;
        var input=Activator.CreateInstance(T("VehicleInput"));Set(input,"move",Vector2.right);
        yield return null;
        SimulateFlight(motor,input,true);
        Vector3 first=(Vector3)Get(motor,"ControlDeflection");
        Assert.Less(first.magnitude,.25f,"Fin actuator must travel rather than jump to full steering");
        foreach(var surface in (Array)Get(motor,"controlSurfaces"))
        {
            if(Mathf.Abs((float)Get(surface,"roll"))<.01f)continue;
            float expected=Mathf.Clamp(-(float)Get(surface,"roll")*first.z*(float)Get(motor,"aileronTravel"),-30,30);
            Assert.AreEqual(expected,(float)Get(surface,"angle"),.001f,"Visual fin angle comes from the physical actuator state");
        }
        yield return new WaitForFixedUpdate();
        Assert.Less(body.angularVelocity.magnitude,.001f,"Deflected fins cannot turn a jet without airflow");
        body.linearVelocity=Vector3.forward*55;
        for(int step=0;step<8;step++){SimulateFlight(motor,input,true);yield return new WaitForFixedUpdate();}
        Assert.Less(body.angularVelocity.z,-.1f,"Aileron deflection must physically roll the Rigidbody with airflow");
        Set(input,"move",Vector2.left);Vector3 before=(Vector3)Get(motor,"ControlDeflection");SimulateFlight(motor,input,true);
        Assert.Less(((Vector3)Get(motor,"ControlDeflection")-before).magnitude,.25f,"Reversing controls must respect fin travel speed");
    }
    [UnityTest] public IEnumerator JetAssistDirectlyTracksElevatedAndLoweredAim()
    {
        foreach(float pitch in new[]{-25f,25f})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");
            var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
            ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;
            var motor=root.GetComponent(T("JetMotor"));Set(motor,"assistEnabled",true);
            root.GetComponent<Rigidbody>().linearVelocity=Vector3.forward*55;
            var input=Activator.CreateInstance(T("VehicleInput"));Set(input,"directionalFlight",true);
            Set(input,"heading",45f);Set(input,"viewPitch",pitch);
            yield return null;
            for(int step=0;step<200;step++){SimulateFlight(motor,input,true);yield return new WaitForFixedUpdate();}
            Vector3 desired=Quaternion.Euler(pitch,45,0)*Vector3.forward;
            Assert.Less(Vector3.Angle(root.transform.forward,desired),8,"Direct aim tracking must work above and below the horizon");
            Assert.Less(root.GetComponent<Rigidbody>().angularVelocity.magnitude,4.01f);
            Object.Destroy(root);yield return null;
        }
    }
    [UnityTest] public IEnumerator AssistedControlsReplayThroughManualMotorIdentically()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");
        var roots=new GameObject[2];var motors=new Component[2];
        for(int i=0;i<2;i++)
        {
            roots[i]=Object.Instantiate(prefab,new Vector3(i*500,1000,0),Quaternion.identity);objects.Add(roots[i]);
            ((Behaviour)roots[i].GetComponent(T("Vehicle"))).enabled=false;
            motors[i]=roots[i].GetComponent(T("JetMotor"));Set(motors[i],"assistEnabled",i==0);
            roots[i].GetComponent<Rigidbody>().linearVelocity=Vector3.forward*55;
        }
        var assisted=Activator.CreateInstance(T("VehicleInput"));Set(assisted,"directionalFlight",true);Set(assisted,"heading",60f);Set(assisted,"viewPitch",-15f);
        var manual=Activator.CreateInstance(T("VehicleInput"));yield return null;
        for(int step=0;step<150;step++)
        {
            SimulateFlight(motors[0],assisted,true);var controls=(Vector3)Get(motors[0],"FlightControlInput");
            Set(manual,"pitchRate",controls.x);Set(manual,"yaw",controls.y);Set(manual,"move",new Vector2(-controls.z,0));
            Call(motors[1],"Simulate",manual,true);yield return new WaitForFixedUpdate();
            Assert.Less(Quaternion.Angle(roots[0].transform.rotation,roots[1].transform.rotation),.02f,"Assist must only supply ordinary manual controls");
            Assert.Less((roots[0].GetComponent<Rigidbody>().linearVelocity-roots[1].GetComponent<Rigidbody>().linearVelocity).magnitude,.01f,"Same controls must produce same flight forces");
        }
    }
    [UnityTest] public IEnumerator JetAssistSettlesWithoutRepeatedAimBounce()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Prototypes/Jet01.prefab");
        var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;
        var motor=root.GetComponent(T("JetMotor"));Set(motor,"assistEnabled",true);
        root.GetComponent<Rigidbody>().linearVelocity=Vector3.forward*55;
        var input=Activator.CreateInstance(T("VehicleInput"));Set(input,"directionalFlight",true);Set(input,"heading",60f);Set(input,"viewPitch",-10f);
        Vector3 desired=Quaternion.Euler(-10,60,0)*Vector3.forward;float minimum=180,maximum=0;
        yield return null;
        for(int step=0;step<350;step++)
        {
            SimulateFlight(motor,input,true);yield return new WaitForFixedUpdate();
            if(step>=250){float error=Vector3.Angle(root.transform.forward,desired);minimum=Mathf.Min(minimum,error);maximum=Mathf.Max(maximum,error);}
        }
        Assert.Less(maximum,3,"Hold the requested aim after settling");
        Assert.Less(maximum-minimum,1,"Do not keep bouncing around the target");
    }
    [UnityTest] public IEnumerator HelicopterAssistReplaysThroughManualMotorIdentically()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Transports/TransportHelicopter.prefab");
        var roots=new GameObject[2];var motors=new Component[2];
        for(int i=0;i<2;i++)
        {
            roots[i]=Object.Instantiate(prefab,new Vector3(i*500,1000,0),Quaternion.identity);objects.Add(roots[i]);
            ((Behaviour)roots[i].GetComponent(T("Vehicle"))).enabled=false;
            motors[i]=roots[i].GetComponent(T("HelicopterMotor"));
            Set(Get(Get(motors[i],"vehicle"),"helicopterAssist"),"assistEnabled",i==0);
            roots[i].GetComponent<Rigidbody>().linearVelocity=new Vector3(0,0,10);
        }
        var source=Activator.CreateInstance(T("VehicleInput"));Set(source,"directionalFlight",true);
        Set(source,"heading",45f);Set(source,"movementHeading",45f);Set(source,"move",new Vector2(.3f,.7f));
        yield return null;
        for(int step=0;step<200;step++)
        {
            var controls=Call(Get(motors[0],"vehicle"),"PrepareDriverInput",source,true);
            Call(motors[0],"Simulate",controls,true);Call(motors[1],"Simulate",controls,true);
            yield return new WaitForFixedUpdate();
            Assert.Less(Quaternion.Angle(roots[0].transform.rotation,roots[1].transform.rotation),.02f);
            Assert.Less((roots[0].GetComponent<Rigidbody>().linearVelocity-roots[1].GetComponent<Rigidbody>().linearVelocity).magnitude,.01f);
        }
    }
    [UnityTest] public IEnumerator HelicopterGuidanceToggleAndCollectiveRemainBounded()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Transports/TransportHelicopter.prefab");
        var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        var vehicle=root.GetComponent(T("Vehicle"));((Behaviour)vehicle).enabled=false;
        var motor=root.GetComponent(T("HelicopterMotor"));var guide=Get(vehicle,"helicopterAssist");
        var source=Activator.CreateInstance(T("VehicleInput"));Set(source,"directionalFlight",true);Set(source,"heading",30f);
        yield return null;
        for(int step=0;step<150;step++)
        {
            Set(guide,"assistEnabled",step%50<25);
            var controls=Call(vehicle,"PrepareDriverInput",source,true);
            Assert.LessOrEqual(Mathf.Abs((float)Get(controls,"pitchRate")),1);
            Assert.LessOrEqual(Mathf.Abs((float)Get(controls,"yaw")),1);
            Call(motor,"Simulate",controls,true);yield return new WaitForFixedUpdate();
            Assert.IsFalse(float.IsNaN(root.GetComponent<Rigidbody>().linearVelocity.y));
        }
    }
    [UnityTest] public IEnumerator RearDoorOpensAndClosesItsColliderWithTheMesh()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Transports/Van.prefab");var root=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(root);
        ((Behaviour)root.GetComponent(T("Vehicle"))).enabled=false;root.GetComponent<Rigidbody>().useGravity=false;
        var doors=root.GetComponent(T("VehicleDoors"));var door=((Array)Get(doors,"doors")).GetValue(0);Set(door,"duration",.1f);var leaf=((Array)Get(door,"leaves")).GetValue(0);var part=(Transform)Get(leaf,"part");Quaternion closed=part.localRotation;
        Assert.IsTrue((bool)Call(doors,"Toggle",0));yield return new WaitForSeconds(.2f);Assert.Greater(Quaternion.Angle(closed,part.localRotation),80);
        Assert.IsNotNull(part.GetComponentInChildren<BoxCollider>());Call(doors,"Toggle",0);yield return new WaitForSeconds(.2f);Assert.Less(Quaternion.Angle(closed,part.localRotation),1);
    }
    [UnityTest] public IEnumerator FlareBehindSolidCoverCannotDistractSeeker()
    {
        var m=Missile("Infrared","AirToAir",out var target);Decoy("Infrared");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(wall);wall.transform.position=new Vector3(0,1000,40);wall.transform.localScale=new Vector3(20,20,2);Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();Assert.AreEqual(target,Get(m,"Target"));
    }
    [UnityTest] public IEnumerator PassengerCannotUseCountermeasureControls()
    {
        var v=Vehicle("Crew",0,new Vector3(0,1000,0));var seat=((Array)Get(v,"seats")).GetValue(0);EnumField(seat,"role","Passenger");var actor=(Component)Get(seat,"occupant");Set(actor,"<MountedVehicle>k__BackingField",v);
        var c=v.gameObject.AddComponent(T("VehicleCountermeasures"));Set(c,"automaticForAI",false);Set(c,"flares",true);
        Assert.IsFalse((bool)Call(c,"TryDeploy",actor));Assert.AreEqual(2,Get(c,"Charges"));yield return null;
    }
    [UnityTest] public IEnumerator DestroyedVehicleCannotDispense()
    {
        var v=Vehicle("Destroyed",0,new Vector3(0,1000,0));var c=v.gameObject.AddComponent(T("VehicleCountermeasures"));Set(c,"automaticForAI",false);Set(c,"flares",true);
        Set(v,"<Health>k__BackingField",0f);Assert.IsFalse((bool)Call(c,"Deploy"));Assert.AreEqual(2,Get(c,"Charges"));yield return null;
    }
    [UnityTest] public IEnumerator AIReactsToIncomingMissile()
    {
        Missile("Infrared","AirToAir",out var target);var seat=((Array)Get(target,"seats")).GetValue(0);var actor=(Component)Get(seat,"occupant");
        var soldier=actor.gameObject.AddComponent(T("SoldierController"));Set(actor,"soldier",soldier);Set(actor,"<MountedVehicle>k__BackingField",target);
        var c=target.gameObject.AddComponent(T("VehicleCountermeasures"));Set(c,"flares",true);Set(c,"dispensers",new[]{target.transform});
        Set(c,"flarePrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Effects/Vehicles/Countermeasures/Flare.prefab").GetComponent(T("VehicleDecoy")));
        yield return new WaitForSeconds(.7f);Assert.Less((int)Get(c,"Charges"),2);
    }
    Component Platform(out Component vehicle)
    {
        vehicle=Vehicle("Moving deck",0,new Vector3(0,1000,0));var deck=GameObject.CreatePrimitive(PrimitiveType.Cube);deck.transform.SetParent(vehicle.transform,false);deck.transform.localScale=new Vector3(8,.2f,8);
        var player=Root("Standing controller");player.transform.position=new Vector3(0,1000.12f,0);var cc=player.AddComponent<CharacterController>();cc.height=2;cc.radius=.3f;cc.center=Vector3.up;
        var platform=player.AddComponent(T("SoldierVehiclePlatform"));Physics.SyncTransforms();Call(platform,"EndStep",false);Assert.AreEqual(vehicle,Get(platform,"Support"));return platform;
    }
    [UnityTest] public IEnumerator MovingPlatformCarriesUnparentedCharacter()
    {
        var p=Platform(out var v);v.transform.position+=Vector3.right*2;Physics.SyncTransforms();Call(p,"BeginStep",Vector3.zero,.02f,false);
        Assert.Greater(p.transform.position.x,1.8f);Assert.IsNull(p.transform.parent);yield return null;
    }
    [UnityTest] public IEnumerator JumpAndExcessiveTiltReleasePlatformSupport()
    {
        var p=Platform(out var v);Call(p,"BeginStep",Vector3.zero,.02f,true);Assert.IsNull(Get(p,"Support"));Call(p,"EndStep",false);Assert.AreEqual(v,Get(p,"Support"));
        v.transform.rotation=Quaternion.Euler(0,0,60);Physics.SyncTransforms();Call(p,"BeginStep",Vector3.zero,.02f,false);Assert.IsNull(Get(p,"Support"));yield return null;
    }
    [UnityTest] public IEnumerator BoardingSwitchingAndLeavingRestoreInfantryControl()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Hummer.prefab");var car=Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);objects.Add(car);
        var v=(Behaviour)car.GetComponent(T("Vehicle"));v.enabled=false;car.GetComponent<Rigidbody>().useGravity=false;
        var match=(Behaviour)Root("Boarding test match").AddComponent(T("ArenaMatch"));match.enabled=false;
        Set(match,"settings",AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Matches/Vehicle_Test.asset"));
        var holder=Root("Inactive actor setup");holder.SetActive(false);
        var actorPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Matches/Vehicles/Player_Polygon_Vehicles.prefab");var player=Object.Instantiate(actorPrefab,new Vector3(4,1000,0),Quaternion.identity,holder.transform);objects.Add(player);
        Set(player.GetComponent(T("ArenaParticipant")),"match",match);holder.SetActive(true);
        var actor=player.GetComponent(T("VehicleOccupant"));var soldier=player.GetComponent(T("SoldierController"));((Behaviour)Get(soldier,"inputSource")).enabled=false;
        yield return null;var entry=(Bounds)Get(v,"EntryBounds");player.transform.position=v.transform.TransformPoint(entry.center+Vector3.right*(entry.extents.x-.2f));Physics.SyncTransforms();
        Assert.IsTrue((bool)Call(v,"BoardFirstAvailable",actor));Assert.AreEqual(v,Get(actor,"MountedVehicle"));
        Call(actor,"SwitchSeat",1);Assert.AreEqual(1,Get(actor,"SeatIndex"));Assert.IsTrue((bool)Call(actor,"Leave",false));
        Assert.IsFalse((bool)Get(actor,"IsMounted"));Assert.IsTrue(((Behaviour)soldier).enabled);Assert.IsTrue(player.GetComponent<CharacterController>().enabled);yield return null;
    }
}
#endif
