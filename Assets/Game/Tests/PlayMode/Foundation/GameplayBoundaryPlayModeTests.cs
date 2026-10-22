#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class GameplayBoundaryPlayModeTests
{
    [UnityTest]
    public IEnumerator CosmeticShotDoesNotResolveHitsOrConsumeAmmunition()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Tests/Weapons/Armory.unity",new LoadSceneParameters(LoadSceneMode.Single));
        var wall=Object.FindAnyObjectByType<ArmoryController>();
        while(wall.targets==null)yield return null;
        wall.Activate(wall.targets.First(t=>t.WeaponId=="legacy-mk18"));
        float readyAt=Time.time+8;while(wall.loadout.IsSwitching&&Time.time<readyAt)yield return null;
        Assert.That(wall.loadout.IsSwitching,Is.False,"Weapon equip must finish before testing accepted fire.");
        var player=wall.loadout.GetComponent<SoldierController>();
        var health=player.GetComponent<SoldierHealth>();Assert.That(health.maxHealth,Is.EqualTo(300));
        var actions=player.actions;var shooting=player.GetComponent<SoldierHitscan>();
        int cosmetic=0,accepted=0,resolved=0;
        System.Action shown=()=>cosmetic++,fired=()=>accepted++;
        System.Action<SoldierHitscan.ShotResult> hit=s=>resolved++;
        actions.FireStarted+=shown;actions.ShotFired+=fired;shooting.ShotResolved+=hit;
        try
        {
            int ammo=player.loadout.EquippedState.MagazineAmmo;
            actions.PresentFire();Assert.That(cosmetic,Is.EqualTo(1));Assert.That(accepted,Is.Zero);Assert.That(resolved,Is.Zero);
            Assert.That(player.loadout.EquippedState.MagazineAmmo,Is.EqualTo(ammo));
            var aim=new Ray(player.transform.position+Vector3.up,Vector3.forward);
            float now=Time.time+1;
            Assert.That(actions.TryFire(now,aim,Vector3.up),Is.True);
            Assert.That(actions.TryFire(now+.001f,aim,Vector3.up),Is.False);
            Assert.That(accepted,Is.EqualTo(1));Assert.That(resolved,Is.EqualTo(1));
            Assert.That(player.loadout.EquippedState.MagazineAmmo,Is.EqualTo(ammo-1));
        }
        finally {actions.FireStarted-=shown;actions.ShotFired-=fired;shooting.ShotResolved-=hit;}
    }
    [UnityTest]
    public IEnumerator VehicleCommandsDriveStopAndExitWithoutDevicePolling()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Tests/Vehicles/Vehicle_Garage.unity",new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var rider=Object.FindObjectsByType<VehicleOccupant>().First(o=>o.IsLocal);
        var vehicle=Vehicle.Active.First(v=>v.name.StartsWith("Hummer")&&!v.IsDestroyed);
        rider.life.Respawn(vehicle.transform.TransformPoint(vehicle.EntryBounds.center),Quaternion.identity);
        int seat=System.Array.FindIndex(vehicle.seats,s=>s.role==Vehicle.SeatRole.Driver);
        Assert.That(vehicle.Board(rider,seat),Is.True);
        rider.ApplyControl(new VehicleControlFrame{active=true,lookAllowed=true,move=Vector2.up,boost=true},.02f);
        Assert.That(vehicle.DriverInput.move.y,Is.EqualTo(1));Assert.That(vehicle.DriverInput.boost,Is.True);
        rider.ApplyControl(default,.02f);Assert.That(vehicle.DriverInput.move,Is.EqualTo(Vector2.zero));
        rider.ApplyControl(new VehicleControlFrame{active=true,lookAllowed=true,toggleFreelook=true},.02f);
        Assert.That(rider.Freelook,Is.True);
        rider.ApplyControl(new VehicleControlFrame{active=true,lookAllowed=true,interactPressed=true,interactHeld=true},.36f);
        Assert.That(rider.IsMounted,Is.False);
        float settleAt=Time.time+4;
        while(!vehicle.CanExit && Time.time<settleAt)yield return new WaitForFixedUpdate();
        Assert.That(vehicle.CanExit,Is.True,"AI boarding requires a grounded, parked vehicle.");
        // Human identity is independent of local ownership or the AI input adapter.
        var holder=new GameObject("Remote seat test");holder.SetActive(false);
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Matches/Vehicles/Player_Polygon_Vehicles.prefab");
        var crew=Object.Instantiate(prefab,holder.transform);
        crew.GetComponent<SoldierController>().SetControl(null,SoldierControlRole.AI,false);
        crew.transform.position=vehicle.transform.TransformPoint(vehicle.EntryBounds.center);
        holder.SetActive(true);
        var bot=crew.GetComponent<VehicleOccupant>();
        var controller=rider.GetComponent<SoldierController>();var source=controller.inputSource;
        try
        {
            Assert.That(vehicle.Board(bot,seat),Is.True,$"AI entry: canBoard={vehicle.CanBoard(bot,seat)}, inside={vehicle.IsInsideEntryBox(bot.transform.position)}, grounded={vehicle.motor.CanDisembark}, speed={vehicle.Body.linearVelocity.magnitude}, health={bot.life.health.CurrentHealth}");
            controller.SetControl(source,SoldierControlRole.Human,false);
            rider.transform.position=vehicle.transform.TransformPoint(vehicle.EntryBounds.center);
            int passenger=System.Array.FindIndex(vehicle.seats,s=>s.role==Vehicle.SeatRole.Passenger);
            Assert.That(vehicle.Board(rider,passenger),Is.True);
            Assert.That(rider.SwitchSeat(seat),Is.True);
            Assert.That(vehicle.Driver,Is.EqualTo(rider));Assert.That(bot.SeatIndex,Is.EqualTo(passenger));
            rider.Leave(true);
            rider.transform.position=vehicle.transform.TransformPoint(vehicle.EntryBounds.center);
            Assert.That(vehicle.Board(rider,passenger),Is.True,"A human can replace an AI without relying on its input adapter.");
            Assert.That(bot.IsMounted,Is.False);
        }
        finally
        {
            rider.Leave(true);controller.SetControl(source,SoldierControlRole.Human,true);
            Object.Destroy(holder);
        }
        yield return null;
    }
}
#endif
