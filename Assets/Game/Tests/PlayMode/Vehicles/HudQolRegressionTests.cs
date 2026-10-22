#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class HudQolRegressionTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    readonly List<UnityEngine.Object> created=new List<UnityEngine.Object>();
    static Type T(string name)=>Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetType(name)!=null).GetType(name);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Flags).Invoke(target,args);
    GameObject Object(string name,bool active=true)
    {
        var go=new GameObject(name);go.SetActive(active);created.Add(go);return go;
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        for(int i=created.Count-1;i>=0;i--)if(created[i])UnityEngine.Object.Destroy(created[i]);
        created.Clear();yield return null;
    }

    [UnityTest] public IEnumerator PiPApertureBelongsToItsViewerAndTracksScreenProjection()
    {
        var viewer=Object("Viewer").AddComponent<Camera>();viewer.nearClipPlane=.1f;
        var other=Object("Other view").AddComponent<Camera>();
        var scopeObject=Object("Inactive scope fixture",false);
        var view=scopeObject.AddComponent(T("SoldierFirstPersonView"));Set(view,"playerCamera",viewer);
        var scope=scopeObject.AddComponent(T("SoldierScopeView"));Set(scope,"view",view);Set(scope,"scopeCamera",viewer);
        var texture=new RenderTexture(32,32,0);created.Add(texture);Set(scope,"texture",texture);
        var aperture=GameObject.CreatePrimitive(PrimitiveType.Cube);created.Add(aperture);
        aperture.transform.SetPositionAndRotation(new Vector3(.2f,.1f,3),Quaternion.Euler(0,12,0));
        aperture.transform.localScale=new Vector3(.4f,.3f,.01f);Set(scope,"lens",aperture.GetComponent<Renderer>());
        object[] args={viewer,default(Rect)};
        Assert.IsTrue((bool)Call(scope,"TryGetLensScreenRect",args));
        var rect=(Rect)args[1];
        Assert.IsTrue(rect.Contains(viewer.WorldToScreenPoint(aperture.transform.position)));
        float initialWidth=rect.width;
        viewer.fieldOfView=35;args[1]=default(Rect);
        Assert.IsTrue((bool)Call(scope,"TryGetLensScreenRect",args));
        Assert.Greater(((Rect)args[1]).width,initialWidth,"ADS FOV changes must update the aperture exclusion immediately.");
        args[0]=other;
        Assert.IsFalse((bool)Call(scope,"TryGetLensScreenRect",args),"A monitor or spectator camera must not inherit another camera's PiP exclusion.");
        aperture.transform.position=Vector3.back*3;args[0]=viewer;
        Assert.IsFalse((bool)Call(scope,"TryGetLensScreenRect",args),"Off-camera optics must not hide screen pins.");
        yield return null;
    }

    [UnityTest] public IEnumerator GameplayDustOnlyAppearsAfterDamageAndClearsOnViewerChange()
    {
        var health=Object("Health").AddComponent(T("SoldierHealth"));
        var actor=Object("Inactive participant fixture",false).AddComponent(T("MatchParticipant"));Set(actor,"health",health);
        var hud=Object("Inactive HUD fixture",false).AddComponent(T("GameplayHud"));Set(hud,"player",actor);
        var canvas=Object("Damage canvas").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        var effectObject=Object("Damage edges",false);effectObject.transform.SetParent(canvas.transform,false);
        var image=effectObject.AddComponent<Image>();
        var material=new Material(Shader.Find("Game/UI/Menu Atmosphere"));created.Add(material);material.SetFloat("_Mode",5);image.material=material;
        var effect=effectObject.AddComponent(T("MenuAtmosphere"));effectObject.SetActive(true);
        Set(effect,"hud",hud);Call(effect,"LateUpdate");
        Assert.IsFalse(image.enabled,"No damage means no full-screen dust draw.");
        Assert.IsTrue((bool)Call(health,"ApplyDamage",25f,null,null));Call(effect,"LateUpdate");
        Assert.IsTrue(image.enabled);Assert.Greater(image.color.a,0);Assert.LessOrEqual(image.color.a,1);
        Set(effect,"damageUntil",Time.unscaledTime-1);Call(effect,"LateUpdate");
        Assert.IsFalse(image.enabled,"The overlay must fully stop drawing after fading.");
        Call(health,"ApplyDamage",10f,null,null);Set(hud,"player",null);Call(effect,"LateUpdate");
        Assert.IsFalse(image.enabled,"Switching the observed character must discard previous damage feedback.");
        Call(health,"ApplyDamage",10f,null,null);Call(effect,"LateUpdate");
        Assert.IsFalse(image.enabled,"Previous character damage subscriptions must be removed.");
        yield return null;
    }
}
#endif
