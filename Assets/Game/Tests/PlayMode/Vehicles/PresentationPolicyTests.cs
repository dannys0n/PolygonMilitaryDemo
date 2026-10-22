#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class PresentationPolicyTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Type T(string name)=>UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t=>t.Name==name);
    static object Get(object target,string name)=>target.GetType().GetProperty(name,Flags)?.GetValue(target)??target.GetType().GetField(name,Flags)?.GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    static object Call(object target,string name,params object[] args)=>(args.Length==0?target.GetType().GetMethod(name,Flags,null,Type.EmptyTypes,null):target.GetType().GetMethod(name,Flags)).Invoke(target,args);
    Component control;
    ScriptableObject weaponFixture;
    Component[] Actors=>((IEnumerable)Get(Get(control,"match"),"participants")).Cast<Component>().ToArray();
    void Count(int team,int count)
    {
        var method=control.GetType().GetMethod("SetCount");
        method.Invoke(control,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,team),(object)count});
    }
    IEnumerator Load(int blue)
    {
        yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Tests/Performance/Performance_Infantry.unity");yield return null;
        control=(Component)UnityEngine.Object.FindAnyObjectByType(T("EntityPerformanceControl"));
        Set(control,"unlimitedAmmo",false);Count(0,1);Count(1,blue);
        yield return null;yield return new WaitForSeconds(.6f);
        Camera.main.transform.SetPositionAndRotation(new Vector3(0,1000,-1000),Quaternion.Euler(-40,180,0));
        yield return new WaitForSeconds(.3f);
    }
    [UnityTest] public IEnumerator RemoteCosmeticDetailReturnsImmediatelyForAnObservedCamera()
    {
        yield return Load(0);
        var actor=Actors[0];var presentation=actor.GetComponent(T("SoldierWeaponPresentation"));
        var animator=(Animator)Get(presentation,"characterAnimator");var parts=(Animator)Get(presentation,"partsAnimator");
        Assert.AreEqual(AnimatorCullingMode.CullUpdateTransforms,animator.cullingMode);
        Assert.AreEqual(AnimatorCullingMode.CullUpdateTransforms,parts.cullingMode);
        Assert.AreEqual(0f,Get(presentation,"cosmeticDetail"));
        int sway=animator.GetLayerIndex("WeaponSway");Assert.AreEqual(0f,animator.GetLayerWeight(sway));
        var rig=(Behaviour)Get(presentation,"rigBuilder");Assert.IsTrue(rig.enabled);
        Assert.Greater((float)Get(Get(presentation,"firingHand"),"weight"),0,"Distance detail cannot disable hand placement or gameplay aiming.");
        var view=actor.GetComponent(T("SoldierFirstPersonView"));var camera=(Camera)Get(view,"playerCamera");
        camera.enabled=true;Call(presentation,"UpdatePresentationQuality");
        Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate,animator.cullingMode);
        Assert.AreEqual(1f,Get(presentation,"cosmeticDetail"),"A newly observed actor must regain full quality immediately.");
        camera.enabled=false;yield return new WaitForSeconds(.3f);
        Assert.AreEqual(AnimatorCullingMode.CullUpdateTransforms,animator.cullingMode);
        Assert.AreEqual(0f,Get(presentation,"cosmeticDetail"));Assert.IsTrue(rig.enabled);
        Call(presentation,"ApplyWeapon",Get(presentation,"weapon"));
        Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate,animator.cullingMode,"Weapon binding must sample the initial pose before culling resumes.");
    }
    [UnityTest] public IEnumerator OffscreenInfantryStillDamagesTargetsAndCompletesTimedReloads()
    {
        yield return Load(1);
        var mode=control.GetType().GetMethod("SetActivity");mode.Invoke(control,new[]{Enum.ToObject(mode.GetParameters()[0].ParameterType,2)});
        foreach(var actor in Actors)
        {
            ((Behaviour)actor.GetComponent(T("SandboxInfantryInput"))).enabled=false;
            actor.GetComponent<NavMeshAgent>().enabled=false;
            // Manual action fixture: stop default input from clearing the aim
            // request each frame; the Rigidbody motor and actions stay enabled.
            ((Behaviour)actor.GetComponent(T("SoldierController"))).enabled=false;
        }
        yield return null;
        var shooter=Actors.First(a=>Get(a,"team").ToString()=="Red");var target=Actors.First(a=>Get(a,"team").ToString()=="Blue");
        var presentation=shooter.GetComponent(T("SoldierWeaponPresentation"));var life=Get(shooter,"player");
        var loadout=Get(life,"loadout");var equipped=Get(loadout,"EquippedWeapon");
        weaponFixture=UnityEngine.Object.Instantiate((ScriptableObject)Get(equipped,"definition"));
        Set(weaponFixture,"spreadDegrees",0f);
        var slot=((Array)Get(loadout,"slots")).GetValue((int)Get(loadout,"selectedSlot"));
        Set(slot,"definition",weaponFixture);Set(equipped,"definition",weaponFixture);
        Call(presentation,"ApplyWeapon",equipped);
        var look=Get(life,"look");var camera=(Camera)Get(look,"aimCamera");var view=Get(life,"view");
        var region=target.GetComponentsInChildren(T("SoldierHitRegion"),true).Cast<Component>()
            .First(r=>!(bool)Get(r,"head")&&r.GetComponents<Collider>().Any(c=>c.enabled&&c.gameObject.activeInHierarchy));
        var torso=region.GetComponents<Collider>().First(c=>c.enabled&&c.gameObject.activeInHierarchy);
        Call(view,"SetAimRequested",true);
        // TPS follows the interpolated physical heading. Give the normal look,
        // solver and aim rig time to reach the target before taking one exact shot.
        // The cloned definition removes random spread without changing game assets.
        for(int frame=0;frame<8;frame++)
        {
            Vector3 direction=torso.bounds.center-camera.transform.position;
            float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            float pitch=-Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
            var mount=(Transform)Get(look,"playerCamera");
            Quaternion offset=Quaternion.Inverse(mount.rotation)*camera.transform.rotation;
            Vector3 forward=Quaternion.Euler((float)Get(look,"Pitch"),(float)Get(look,"Yaw"),0)*offset*Vector3.forward;
            float commandedYaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
            float commandedPitch=-Mathf.Atan2(forward.y,new Vector2(forward.x,forward.z).magnitude)*Mathf.Rad2Deg;
            Call(look,"ApplyLookDelta",new Vector2(Mathf.DeltaAngle(commandedYaw,yaw),commandedPitch-pitch));
            yield return new WaitForFixedUpdate();yield return null;
        }
        Physics.SyncTransforms();
        var ray=camera.ViewportPointToRay(new Vector3(.5f,.5f,0));
        float rayError=Vector3.Angle(ray.direction,torso.bounds.center-ray.origin);
        Assert.Less(rayError,.15f,$"Manual TPS aim must settle on the actual torso before testing damage; ray={ray}, torso={torso.bounds.center}.");
        var health=Get(target,"health");float before=(float)Get(health,"CurrentHealth");
        var actions=Get(life,"actions");var shooting=Get(actions,"shooting");
        object[] rayArgs={ray,(float)Get(weaponFixture,"range"),default(RaycastHit)};
        Call(shooting,"ClosestHit",rayArgs);var aimedHit=(RaycastHit)rayArgs[2];
        var muzzle=(Transform)Get(loadout,"EquippedMuzzle");object[] coverArgs={muzzle.position,null,Vector3.zero};
        bool blocked=(bool)Call(shooting,"TryMuzzleBlock",coverArgs);
        Assert.IsTrue((bool)Call(actions,"TryFire"));
        Assert.Less((float)Get(health,"CurrentHealth"),before,
            $"An invisible shooter must still damage the target. rayError={rayError}, aimedCollider={aimedHit.collider}, expectedTorso={torso}, muzzle={muzzle.position}, blocked={blocked}, cover={coverArgs[1]}.");
        var animator=(Animator)Get(presentation,"characterAnimator");Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate,animator.cullingMode);
        Call(view,"SetAimRequested",false);
        var state=Get(loadout,"EquippedState");var definition=Get(Get(loadout,"EquippedWeapon"),"definition");
        int reserve=(int)Get(state,"ReserveAmmo");
        Assert.IsTrue((bool)Call(actions,"TryReload"));yield return null;
        Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate,animator.cullingMode);
        yield return new WaitForSeconds((float)Get(definition,"reloadDuration")+.5f);
        Assert.IsFalse((bool)Get(actions,"IsReloading"));
        Assert.AreEqual(Get(definition,"magazineCapacity"),Get(state,"MagazineAmmo"));
        Assert.Less((int)Get(state,"ReserveAmmo"),reserve);
        Assert.AreEqual(AnimatorCullingMode.CullUpdateTransforms,animator.cullingMode,"After the timed action, the remote actor may return to visibility culling.");
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if(control){Count(0,0);Count(1,0);}
        if(weaponFixture)UnityEngine.Object.Destroy(weaponFixture);
        Time.timeScale=1;yield return null;LogAssert.NoUnexpectedReceived();
    }
}
#endif
