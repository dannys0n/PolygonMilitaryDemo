using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

public class HipFireTests
{
    [Test]
    public void RandomShotsStayInConeAndCoverBothSides()
    {
        var original = Random.state;
        try
        {
            Random.InitState(171);
            var forward = new Vector3(.3f,.2f,.9f).normalized;
            var basis = Quaternion.LookRotation(forward,Vector3.up);
            Vector2 mean = Vector2.zero;
            float max = 0f;
            for (int i=0;i<4000;i++)
            {
                var direction=SoldierHitscan.SpreadDirection(forward,Vector3.up,3f);
                float angle=Vector3.Angle(direction,forward);
                Assert.That(angle,Is.LessThanOrEqualTo(3.01f));max=Mathf.Max(max,angle);
                var local=Quaternion.Inverse(basis)*direction;mean+=new Vector2(local.x,local.y);
            }
            Assert.That(max,Is.GreaterThan(2.9f));Assert.That((mean/4000).magnitude,Is.LessThan(.002f));
            Assert.That(Vector3.Angle(SoldierHitscan.SpreadDirection(forward,Vector3.up,0f),forward),Is.LessThan(.01f));
        }
        finally { Random.state=original; }
    }

    [Test]
    public void BloomUsesShotClockCapsRecoversAndResetsForAimOrWeaponChange()
    {
        var go=new GameObject("Spread test");go.SetActive(false);
        var definition=ScriptableObject.CreateInstance<SoldierWeaponDefinition>();
        var other=ScriptableObject.CreateInstance<SoldierWeaponDefinition>();
        try
        {
            definition.hipFireSpreadDegrees=2f;definition.hipBloomPerShot=.5f;definition.maximumHipBloom=1.5f;
            definition.hipBloomRecoveryDelay=.1f;definition.hipBloomRecoveryTime=.2f;
            var loadout=go.AddComponent<SoldierWeaponLoadout>();loadout.slots=new[]{new SoldierWeaponLoadout.Slot{definition=definition}};
            var actions=go.AddComponent<SoldierActions>();
            typeof(SoldierActions).GetField("loadout",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(actions,loadout);
            var record=typeof(SoldierActions).GetMethod("RecordHipShot",BindingFlags.NonPublic|BindingFlags.Instance);
            Assert.That(actions.HipSpreadAt(20),Is.EqualTo(2f));
            record.Invoke(actions,new object[]{definition,20f,false});Assert.That(actions.HipSpreadAt(20.05f),Is.EqualTo(2.5f).Within(.001f));
            for(int i=0;i<10;i++)record.Invoke(actions,new object[]{definition,20f,false});
            Assert.That(actions.HipSpreadAt(20.05f),Is.EqualTo(3.5f).Within(.001f));
            Assert.That(actions.HipSpreadAt(22),Is.EqualTo(2f).Within(.001f));
            record.Invoke(actions,new object[]{definition,22f,true});Assert.That(actions.HipSpreadAt(22),Is.EqualTo(2f));
            record.Invoke(actions,new object[]{definition,22f,false});other.hipFireSpreadDegrees=.4f;loadout.slots[0].definition=other;
            Assert.That(actions.HipSpreadAt(22),Is.EqualTo(.4f));
        }
        finally { Object.DestroyImmediate(go);Object.DestroyImmediate(definition);Object.DestroyImmediate(other); }
    }

    [TestCase("KSG_FPSPack")]
    [TestCase("Drake-12_FPSPack")]
    public void PolishedShotgunsUseRadiusSpread(string name)
    {
        var definition=AssetDatabase.LoadAssetAtPath<SoldierWeaponDefinition>("Assets/Game/Definitions/Weapons/"+name+".asset");
        Assert.That(definition,Is.Not.Null);Assert.That(definition.UsesSpread,Is.True);Assert.That(definition.UsesLauncherCrosshair,Is.False);
    }
    [TestCase("RPG-7_FPSPack")]
    [TestCase("M32")]
    public void LaunchersUseTheirOwnReticle(string name)
    {
        var definition=AssetDatabase.LoadAssetAtPath<SoldierWeaponDefinition>("Assets/Game/Definitions/Weapons/"+name+".asset");
        Assert.That(definition,Is.Not.Null);Assert.That(definition.UsesLauncherCrosshair,Is.True);
    }
}
