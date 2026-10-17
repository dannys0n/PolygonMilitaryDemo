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

public class WeaponAttachmentTests
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    readonly List<GameObject> roots=new List<GameObject>();
    Component loadout,view,actions;
    static Type T(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).First(t=>t!=null);
    static object Get(object o,string name){var p=o.GetType().GetProperty(name,Flags);return p!=null?p.GetValue(o):o.GetType().GetField(name,Flags).GetValue(o);}
    static void Set(object o,string name,object v)=>o.GetType().GetField(name,Flags).SetValue(o,v);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    GameObject Root(string name){var g=new GameObject(name);roots.Add(g);return g;}
    [UnitySetUp] public IEnumerator Setup()
    {
        var match=(Behaviour)Root("Attachment test match").AddComponent(T("ArenaMatch"));match.enabled=false;Set(match,"settings",AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Matches/Vehicle_Test.asset"));
        var holder=Root("Actor fixture");holder.SetActive(false);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Matches/Vehicles/Player_Polygon_Vehicles.prefab");var actor=Object.Instantiate(prefab,Vector3.zero,Quaternion.identity,holder.transform);
        Set(actor.GetComponent(T("ArenaParticipant")),"match",match);holder.SetActive(true);loadout=actor.GetComponent(T("SoldierWeaponLoadout"));view=actor.GetComponent(T("SoldierFirstPersonView"));actions=actor.GetComponent(T("SoldierActions"));
        ((Behaviour)actor.GetComponent(T("SoldierLocalInput"))).enabled=false;yield return null;yield return null;
        ((Behaviour)actor.GetComponent(T("SoldierController"))).enabled=false;Call(actor.GetComponent(T("SoldierLife")),"SetGameplayAllowed",true);
    }
    [UnityTearDown] public IEnumerator Cleanup(){for(int i=roots.Count-1;i>=0;i--)if(roots[i])Object.Destroy(roots[i]);roots.Clear();yield return null;}
    static void AssertFinite(Transform t){Assert.IsFalse(float.IsNaN(t.position.x)||float.IsInfinity(t.position.x));Assert.Greater(t.lossyScale.sqrMagnitude,0);}
    [UnityTest] public IEnumerator OpticMountStandardsFilterRuntimeAndClassMenuConsistently()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        var entries=((Array)Get(catalog,"entries")).Cast<object>().Where(e=>((Array)Get(e,"sights")).Length>0).ToArray();
        var all=AssetDatabase.FindAssets("t:WeaponSightDefinition",new[]{"Assets/Game"}).Select(g=>AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        object Entry(string name)=>entries.Single(e=>(string)Get(Get(e,"weapon"),"displayName")==name);
        Component Module(string name)=>((Component)Get(Entry(name),"thirdPersonPrefab")).GetComponent(T("SoldierWeaponSights"));
        var pso=all.Single(s=>(string)Get(s,"id")=="svd-pso-4x");
        var side=all.Single(s=>(string)Get(s,"id")=="scope_1p29");
        var rail=all.Single(s=>(string)Get(s,"id")=="polygon-reflex");
        var susat=all.Single(s=>(string)Get(s,"id")=="military_scope_10");
        Assert.IsFalse((bool)Call(Module("M4"),"Supports",pso));
        Assert.IsFalse((bool)Call(Module("M4"),"Supports",side));
        Assert.IsFalse((bool)Call(Module("M4"),"Supports",susat));
        Assert.IsTrue((bool)Call(Module("M4"),"Supports",rail));
        foreach(string name in new[]{"AKM","AK-12","AK-74","SVD"})
        {Assert.IsTrue((bool)Call(Module(name),"Supports",pso),name);Assert.IsTrue((bool)Call(Module(name),"Supports",side),name);}
        Assert.IsFalse((bool)Call(Module("MK23"),"Supports",rail),"An authored transform alone is not a pistol rail.");
        Assert.IsTrue((bool)Call(Module("M1911"),"Supports",rail),"The M1911's authored optic bridge remains supported.");
        var native=all.Single(s=>(string)Get(s,"id")=="aug-built-in-sight");
        Assert.IsTrue((bool)Call(Module("Steyr AUG A1"),"Supports",native));
        Assert.IsFalse((bool)Call(Module("M4"),"Supports",native));
        var menu=AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/UI/ClassMenuCatalog.asset");
        foreach(var entry in entries)
        {
            var definition=Get(entry,"weapon");var module=((Component)Get(entry,"thirdPersonPrefab")).GetComponent(T("SoldierWeaponSights"));
            foreach(var sight in (Array)Get(entry,"sights"))Assert.IsTrue((bool)Call(module,"Supports",sight),(string)Get(definition,"displayName"));
            var item=((Array)Get(menu,"weapons")).Cast<object>().Single(w=>(string)Get(w,"id")== (string)Get(definition,"catalogId"));
            CollectionAssert.AreEquivalent(((Array)Get(entry,"sights")).Cast<object>().Select(s=>(string)Get(s,"id")).ToArray(),(string[])Get(item,"sights"));
        }
        var slots=((Array)Get(loadout,"slots")).Cast<object>().ToArray();
        int m4=Array.FindIndex(slots,s=>(string)Get(Get(s,"definition"),"displayName")=="M4");
        Call(actions,"CancelActions");Call(loadout,"SetStartingSlot",m4);yield return null;
        var equipped=(Component)Get(loadout,"EquippedSights");
        var rejected=Assert.Throws<TargetInvocationException>(()=>Call(equipped,"SetSight",pso));
        Assert.IsInstanceOf<ArgumentException>(rejected.InnerException);
        var selected=Get(equipped,"SelectedSight");
        for(int i=0;i<40;i++){Call(equipped,"Cycle");Assert.IsTrue((bool)Call(equipped,"Supports",Get(equipped,"SelectedSight")));}
        Call(equipped,"SetSight",selected);yield return null;
    }
    static void AssertAttachmentPose(object definition,Transform mount,Transform instance)
    {
        var expected=Matrix4x4.TRS((Vector3)Get(definition,"positionOffset"),Quaternion.Euler((Vector3)Get(definition,"rotationOffset")),(Vector3)Get(definition,"scale"));
        var socket=mount.GetComponent(T("WeaponAttachmentSocket"));if(socket)foreach(var p in (Array)Get(socket,"placements"))if((Object)Get(p,"attachment")== (Object)definition)expected=Matrix4x4.TRS((Vector3)Get(p,"position"),(Quaternion)Get(p,"rotation"),(Vector3)Get(p,"scale"));
        Assert.Less(Vector3.Distance(expected.GetColumn(3),instance.localPosition),.0001f);Assert.Less(Quaternion.Angle(expected.rotation,instance.localRotation),.01f);AssertFinite(instance);
    }
    [UnityTest] public IEnumerator AllWeaponAttachmentsEquipAndKeepTheirCalibratedPoses()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");var entries=(Array)Get(catalog,"entries");var slots=(Array)Get(loadout,"slots");int checkedWeapons=0;
        for(int i=0;i<slots.Length;i++)
        {
            var slot=slots.GetValue(i);var definition=Get(slot,"definition");var entry=entries.Cast<object>().First(e=>(Object)Get(e,"weapon")== (Object)definition);var choices=((Array)Get(entry,"attachments")).Cast<object>().Where(d=>d!=null).GroupBy(d=>Get(d,"mount")).Select(g=>g.First()).ToArray();if(choices.Length==0)continue;
            Call(actions,"CancelActions");Assert.IsTrue((bool)Call(loadout,"SetStartingSlot",i));yield return null;
            var selected=Array.CreateInstance(T("WeaponAttachmentDefinition"),choices.Length);for(int n=0;n<choices.Length;n++)selected.SetValue(choices[n],n);Call(loadout,"SetAttachments",selected);yield return null;
            var attachments=(Component)Get(loadout,"EquippedAttachments");var visuals=(IList)Get(attachments,"visuals");
            foreach(var mountInfo in (Array)Get(attachments,"mounts"))
            {
                var part=choices.FirstOrDefault(d=>Get(d,"mount").Equals(Get(mountInfo,"kind")));if(part==null)continue;
                foreach(string endpoint in new[]{"firstPerson","thirdPerson"})
                {
                    var mount=(Transform)Get(mountInfo,endpoint);if(!mount)continue;
                    var instance=visuals.Cast<GameObject>().FirstOrDefault(g=>g&&g.transform.parent==mount);Assert.IsNotNull(instance,"Missing "+endpoint+" attachment on "+Get(definition,"displayName"));AssertAttachmentPose(part,mount,instance.transform);
                }
            }
            foreach(var weaponView in new[]{Get(loadout,"FirstPersonView"),Get(loadout,"ThirdPersonView")}.Where(v=>v!=null))
            {
                var animator=(Animator)Get(weaponView,"animator");animator.Update(.05f);
            }
            yield return null;checkedWeapons++;
        }
        Assert.GreaterOrEqual(checkedWeapons,30);
    }
    [UnityTest] public IEnumerator AlternateSightUsesSameEquippedOpticAndReleasesPiP()
    {
        var sights=(Component)Get(loadout,"EquippedSights");
        var sight=((Array)Get(sights,"compatibleSights")).Cast<Object>().First(s=>s&&Get(s,"prefab")!=null&&(bool)Get(s,"pictureInPicture"));
        Call(loadout,"SetSight",sight);yield return null;
        var attachment=(Component)Get(sights,"FirstPersonAttachment");
        var rear=new GameObject("Test alternate rear").transform;rear.SetParent(attachment.transform,false);rear.localPosition=new Vector3(0,.08f,0);
        var front=new GameObject("Test alternate front").transform;front.SetParent(attachment.transform,false);front.localPosition=new Vector3(0,.08f,.1f);
        Set(attachment,"alternateRearSight",rear);Set(attachment,"alternateFrontSight",front);Set(attachment,"alternateEyeDistance",.17f);Set(attachment,"alternateFieldOfView",55f);Set(attachment,"alternateSensitivityMultiplier",.9f);
        Call(view,"SetAimRequested",true);yield return new WaitForSeconds(.4f);
        var scope=view.GetComponent(T("SoldierScopeView"));Assert.IsNotNull(Get(scope,"ScopeTexture"));
        int revision=(int)Get(sights,"Revision");Assert.IsTrue((bool)Call(loadout,"TryToggleSightMode"));yield return null;
        Assert.AreEqual(sight,Get(sights,"SelectedSight"));Assert.AreEqual(attachment,Get(sights,"FirstPersonAttachment"));
        Assert.AreEqual(rear,Get(sights,"ActiveAimPoint"));Assert.AreEqual(front,Get(sights,"ActiveFrontAimPoint"));
        Assert.AreEqual(.17f,(float)Get(sights,"EyeDistance"),.0001f);Assert.AreEqual(55f,(float)Get(sights,"FieldOfView"));Assert.AreEqual(.9f,(float)Get(sights,"SensitivityMultiplier"));
        Assert.Greater((int)Get(sights,"Revision"),revision);Assert.IsFalse((bool)Get(sights,"UsesPictureInPicture"));
        Assert.IsNull(Get(scope,"ScopeTexture"));Assert.IsFalse(((Camera)Get(scope,"ScopeCamera")).enabled);
        Assert.IsTrue((bool)Call(loadout,"TryToggleSightMode"));yield return null;Assert.IsTrue((bool)Get(sights,"UsesPictureInPicture"));Assert.IsNotNull(Get(scope,"ScopeTexture"));
    }
    [UnityTest] public IEnumerator ConfiguredBackupSightsRemainSelectedAcrossViewAndWeaponSwaps()
    {
        int slot=(int)Get(loadout,"selectedSlot");
        foreach(string name in new[]{"Military_Scope_03","Scope_Trijicon","Military_Scope_12","Scope_ACOG"})
        {
            var sights=(Component)Get(loadout,"EquippedSights");
            var sight=((Array)Get(sights,"compatibleSights")).Cast<Object>().First(s=>s&&Get(s,"prefab")!=null&&((Component)Get(s,"prefab")).name==name);
            Call(loadout,"SetSight",sight);yield return null;
            var attachment=(Component)Get(sights,"FirstPersonAttachment");
            Assert.IsTrue((bool)Get(attachment,"HasAlternateSight"),name);
            var primary=(Transform)Get(sights,"ActiveAimPoint");var secondary=(Transform)Get(attachment,"alternateRearSight");
            Assert.Greater(secondary.localPosition.y-primary.localPosition.y,.01f,name);
            Assert.IsTrue((bool)Call(loadout,"TryToggleSightMode"));Assert.AreEqual(secondary,Get(sights,"ActiveAimPoint"));
            Call(view,"SetFirstPerson",false);yield return null;Assert.IsTrue((bool)Get(sights,"AlternateSightActive"),name+" TPS");
            Call(view,"SetFirstPerson",true);yield return null;Assert.IsTrue((bool)Get(sights,"AlternateSightActive"),name+" FPS");
            Assert.AreEqual(sight,Get(sights,"SelectedSight"));AssertFinite((Transform)Get(sights,"ActiveAimPoint"));
            Assert.IsFalse((bool)Get(sights,"UsesPictureInPicture"));
            int other=((int[])Get(loadout,"AvailableSlots")).First(i=>i!=slot);
            Assert.IsTrue((bool)Call(loadout,"SetStartingSlot",other));yield return null;
            Assert.IsTrue((bool)Call(loadout,"SetStartingSlot",slot));yield return null;
            sights=(Component)Get(loadout,"EquippedSights");Assert.AreEqual(sight,Get(sights,"SelectedSight"));Assert.IsTrue((bool)Get(sights,"AlternateSightActive"),name+" weapon swap");
            Assert.IsTrue((bool)Call(loadout,"TryToggleSightMode"));Assert.IsTrue((bool)Get(sights,"UsesPictureInPicture"));
        }
    }
    [UnityTest] public IEnumerator OpticCalibrationDoesNotAccumulateAndPiPOwnershipIsPreserved()
    {
        var slots=(Array)Get(loadout,"slots");int index=Array.FindIndex(slots.Cast<object>().ToArray(),s=>(string)Get(Get(s,"definition"),"displayName")=="AK-47");Call(loadout,"SetStartingSlot",index);yield return null;
        var sights=(Component)Get(loadout,"EquippedSights");var choices=((Array)Get(sights,"compatibleSights")).Cast<Object>().Where(s=>s&&Get(s,"prefab")!=null).ToArray();
        var selected=choices.First(s=>((Component)Get(s,"prefab")).name=="Polygon_Scope_A");Call(loadout,"SetSight",selected);yield return null;
        var first=(Component)Get(sights,"FirstPersonAttachment");var expectedPosition=first.transform.localPosition;var expectedRotation=first.transform.localRotation;
        for(int i=0;i<5;i++){Call(sights,"SetRiser",.025f);Call(sights,"SetRiser",0f);Assert.Less(Vector3.Distance(expectedPosition,first.transform.localPosition),.00001f);Assert.Less(Quaternion.Angle(expectedRotation,first.transform.localRotation),.001f);}
        Call(view,"SetAimRequested",true);yield return new WaitForSeconds(.4f);
        var scope=view.GetComponent(T("SoldierScopeView"));var camera=(Camera)Get(scope,"ScopeCamera");Assert.IsNotNull(camera);Assert.IsTrue(camera.enabled);
        AssertFinite(first.transform);Call(view,"SetFirstPerson",false);yield return null;Assert.IsFalse(camera.enabled);Assert.IsNull(Get(scope,"ScopeTexture"));
    }
    [UnityTest] public IEnumerator AttachmentsRemainBoundDuringFireReloadAndWeaponSwap()
    {
        var slots=(Array)Get(loadout,"slots");int index=Array.FindIndex(slots.Cast<object>().ToArray(),s=>(string)Get(Get(s,"definition"),"displayName")=="AK-47");Call(loadout,"SetStartingSlot",index);yield return null;
        var sights=(Component)Get(loadout,"EquippedSights");var sight=((Array)Get(sights,"compatibleSights")).Cast<Object>().First(s=>s&&Get(s,"prefab")!=null&&((Component)Get(s,"prefab")).name=="Polygon_Scope_A");Call(loadout,"SetSight",sight);
        var first=(Component)Get(sights,"FirstPersonAttachment");var mount=first.transform.parent;var position=first.transform.localPosition;var rotation=first.transform.localRotation;
        Assert.IsTrue((bool)Call(actions,"TryFire"));yield return new WaitForSeconds(.15f);Assert.IsTrue((bool)Call(actions,"TryReload"));
        for(int i=0;i<5;i++){yield return new WaitForSeconds(.15f);Assert.AreEqual(mount,first.transform.parent);Assert.Less(Vector3.Distance(position,first.transform.localPosition),.00001f);Assert.Less(Quaternion.Angle(rotation,first.transform.localRotation),.001f);}
        Call(actions,"CancelActions");Call(loadout,"SetStartingSlot",0);yield return null;Assert.IsFalse((bool)first);Call(loadout,"SetStartingSlot",index);yield return null;Assert.IsNotNull(Get(Get(loadout,"EquippedSights"),"FirstPersonAttachment"));
    }
    [UnityTest] public IEnumerator CaptureCorrectedWeaponAssembliesInFPSAndTPS()
    {
        var light=Root("Runtime inspection light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(40,-30,0);
        var camera=Root("Runtime attachment inspection camera").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.055f,.06f);
        var slots=(Array)Get(loadout,"slots");var catalog=AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");var entries=(Array)Get(catalog,"entries");
        foreach(string name in new[]{"AK-47","M82","AUG","SVD","M16A4","1911","TAR-21","MK18"})
        {
            Call(actions,"CancelActions");int index=Array.FindIndex(slots.Cast<object>().ToArray(),s=>(string)Get(Get(s,"definition"),"displayName")==name);Call(loadout,"SetStartingSlot",index);yield return null;
            var definition=Get(slots.GetValue(index),"definition");var entry=entries.Cast<object>().First(e=>(Object)Get(e,"weapon")== (Object)definition);var choices=((Array)Get(entry,"attachments")).Cast<object>().Where(d=>d!=null&&(Get(d,"mount").ToString()=="Grip"||Get(d,"mount").ToString()=="Rail")).GroupBy(d=>Get(d,"mount")).Select(g=>g.OrderBy(d=>((Object)d).name=="SM_Wep_Mod_ForeGrip_01"?0:1).First()).ToArray();var selected=Array.CreateInstance(T("WeaponAttachmentDefinition"),choices.Length);for(int n=0;n<choices.Length;n++)selected.SetValue(choices[n],n);Call(loadout,"SetAttachments",selected);
            var sights=(Component)Get(loadout,"EquippedSights");var sight=((Array)Get(sights,"compatibleSights")).Cast<Object>().FirstOrDefault(s=>s&&Get(s,"prefab")!=null&&((Component)Get(s,"prefab")).name=="Polygon_Scope_A");if(sight)Call(loadout,"SetSight",sight);
            foreach(bool firstPerson in new[]{true,false})
            {
                Call(view,"SetFirstPerson",firstPerson);yield return new WaitForSeconds(.1f);var weapon=(Component)Get(loadout,firstPerson?"FirstPersonView":"ThirdPersonView");Assert.IsNotNull(weapon);var renderers=weapon.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();Assert.Greater(renderers.Length,0);
                var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);var effect=(Component)Get(weapon,"effects");var forward=((Transform)Get(effect,"muzzleFlash")).forward;var right=Vector3.Cross(Vector3.up,forward).normalized;if(right.sqrMagnitude<.1f)right=weapon.transform.right;
                camera.cullingMask=1<<(int)Get(view,firstPerson?"firstPersonLayer":"thirdPersonLayer");camera.transform.position=b.center+right*1.5f;camera.transform.LookAt(b.center);camera.aspect=2;camera.orthographicSize=Mathf.Max(.1f,b.extents.magnitude*.6f);
                var hidden=new Dictionary<Renderer,bool>();if(!firstPerson)foreach(var r in view.GetComponentsInChildren<Renderer>(true))if(!r.transform.IsChildOf(weapon.transform)){hidden[r]=r.enabled;r.enabled=false;}
                var rt=new RenderTexture(1000,500,24);rt.Create();camera.targetTexture=rt;camera.Render();foreach(var r in hidden)if(r.Key)r.Key.enabled=r.Value;var previous=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(1000,500,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1000,500),0,0);texture.Apply();RenderTexture.active=previous;
                string path="C:/Users/2002d/Documents/Codex/2026-09-29/how/work/runtime-"+name.Replace("/","-")+"-"+(firstPerson?"FPS":"TPS")+"-attachments.png";System.IO.File.WriteAllBytes(path,texture.EncodeToPNG());camera.targetTexture=null;rt.Release();Object.Destroy(rt);Object.Destroy(texture);
            }
        }
    }
    [UnityTest] public IEnumerator M82BuiltInHousingHidesForReplacementAndRestoresForOriginalSight()
    {
        var slots=(Array)Get(loadout,"slots");int index=Array.FindIndex(slots.Cast<object>().ToArray(),s=>(string)Get(Get(s,"definition"),"displayName")=="M82");Call(loadout,"SetStartingSlot",index);yield return null;
        var sights=(Component)Get(loadout,"EquippedSights");var choices=((Array)Get(sights,"compatibleSights")).Cast<Object>().Where(s=>s&&Get(s,"prefab")!=null).ToArray();var original=choices.First(s=>((Component)Get(s,"prefab")).name=="M82_BuiltInScope");var replacement=choices.First(s=>((Component)Get(s,"prefab")).name=="Polygon_Scope_A");
        foreach(var selected in new[]{replacement,original,replacement})
        {
            Call(loadout,"SetSight",selected);yield return null;
            foreach(string name in new[]{"firstPersonMount","thirdPersonMount"})
            {var mount=(Transform)Get(sights,name);var socket=mount.GetComponent(T("WeaponAttachmentSocket"));var renderers=(Renderer[])Get(socket,"builtInSightRenderers");Assert.AreEqual(1,renderers.Length);Assert.AreEqual(selected!=original,renderers[0].forceRenderingOff);}
        }
    }
}
#endif
