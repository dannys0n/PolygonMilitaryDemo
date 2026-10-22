using NUnit.Framework;
using UnityEngine;

public class GameplayFoundationTests
{
    [TestCase(SoldierControlRole.Human,true,300f)]
    [TestCase(SoldierControlRole.Human,false,300f)]
    [TestCase(SoldierControlRole.AI,false,150f)]
    public void HealthUsesIdentityRatherThanLocalOwnership(SoldierControlRole role,bool local,float expected)
    {
        var go=new GameObject("Role test");go.SetActive(false);
        try
        {
            var controller=go.AddComponent<SoldierController>();controller.SetControl(null,role,local);
            var health=go.AddComponent<SoldierHealth>();var life=go.AddComponent<SoldierLife>();life.health=health;
            life.ResetHealth();Assert.That(health.maxHealth,Is.EqualTo(expected));
            health.ApplyDamage(20,null);life.ResetHealth();Assert.That(health.CurrentHealth,Is.EqualTo(expected));
            Assert.That(controller.IsLocallyControlled,Is.EqualTo(local&&role==SoldierControlRole.Human));
        }
        finally { Object.DestroyImmediate(go); }
    }
    [Test]
    public void ChangingInputDeviceDoesNotImplicitlyChangeCharacterIdentity()
    {
        var go=new GameObject("Identity test");go.SetActive(false);
        try
        {
            var controller=go.AddComponent<SoldierController>();controller.SetControl(null,SoldierControlRole.Human,false);
            controller.inputSource=null;
            Assert.That(controller.IsHumanControlled,Is.True);Assert.That(controller.IsLocallyControlled,Is.False);
        }
        finally { Object.DestroyImmediate(go); }
    }
    [Test]
    public void FireCadenceAndAmmunitionUseTheSuppliedClock()
    {
        var definition=ScriptableObject.CreateInstance<SoldierWeaponDefinition>();
        try
        {
            definition.magazineCapacity=3;definition.startingReserveAmmo=10;definition.roundsPerMinute=300;
            var state=new SoldierWeaponLoadout.WeaponState(definition);
            Assert.That(state.TryConsumeRound(10,definition),Is.True);
            Assert.That(state.TryConsumeRound(10.1f,definition),Is.False);
            Assert.That(state.TryConsumeRound(10.21f,definition),Is.True);
            Assert.That(state.MagazineAmmo,Is.EqualTo(1));state.CompleteReload(definition);
            Assert.That(state.MagazineAmmo,Is.EqualTo(3));Assert.That(state.ReserveAmmo,Is.EqualTo(8));
        }
        finally { Object.DestroyImmediate(definition); }
    }
}
