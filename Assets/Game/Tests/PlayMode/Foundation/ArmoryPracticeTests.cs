#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class ArmoryPracticeTests
{
    [UnityTest]
    public IEnumerator BothArmoriesAcceptHeadAndBodyHitsWithoutKillingTheDummy()
    {
        foreach(var name in new[]{"Armory","Armory_UE4OriginalProportions"})
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Tests/Weapons/"+name+".unity",new LoadSceneParameters(LoadSceneMode.Single));
            var armory=Object.FindAnyObjectByType<ArmoryController>();
            while(armory.targets==null)yield return null;
            armory.Activate(armory.targets.First(t=>t.WeaponId=="legacy-mk18"));
            float ready=Time.time+8;while(armory.loadout.IsSwitching&&Time.time<ready)yield return null;
            Assert.IsFalse(armory.loadout.IsSwitching);
            var dummy=Object.FindObjectsByType<SoldierHealth>(FindObjectsSortMode.None).Single(h=>h.infiniteHealth);
            var regions=dummy.GetComponentsInChildren<SoldierHitRegion>();Assert.AreEqual(2,regions.Length);
            var definition=armory.loadout.EquippedWeapon.definition;definition.spreadDegrees=0;
            SoldierHitscan.ShotResult result=default;
            System.Action<SoldierHitscan.ShotResult> handler=s=>result=s;
            armory.shooting.ShotResolved+=handler;
            try
            {
                int index=0;
                foreach(var region in regions)
                {
                    Physics.SyncTransforms();
                    var origin=armory.shooting.aimCamera.transform.position;
                    var point=region.GetComponent<Collider>().bounds.center;
                    Assert.IsTrue(armory.loadout.actions.TryFire(Time.time+1+index++,new Ray(origin,point-origin),Vector3.up));
                    Assert.IsTrue(result.damaged,"The dummy must accept normal weapon damage.");
                    Assert.AreEqual(region,result.collider.GetComponent<SoldierHitRegion>());
                    yield return null;yield return null;
                    Assert.Greater(armory.hitMarker.alpha,0);
                    Assert.AreEqual(dummy.maxHealth,dummy.CurrentHealth);Assert.IsFalse(dummy.IsDead);
                }
                Assert.IsTrue(dummy.ApplyDamage(1000000,armory.loadout.gameObject));
                Assert.AreEqual(dummy.maxHealth,dummy.CurrentHealth);Assert.IsFalse(dummy.IsDead);
                yield return new WaitForSeconds(.25f);Assert.AreEqual(0,armory.hitMarker.alpha);
            }
            finally{armory.shooting.ShotResolved-=handler;}
        }
        LogAssert.NoUnexpectedReceived();
    }
}
#endif
