#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;

public class SandboxSimpleModeTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>().First(t => t.Name == name);
    static object Get(object o, string name) => o.GetType().GetProperty(name, Flags)?.GetValue(o) ?? o.GetType().GetField(name, Flags)?.GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
    static void Set(object o, string name, object value)
    {
        var field = o.GetType().GetField(name, Flags);
        if (field != null) field.SetValue(o, value); else o.GetType().GetProperty(name, Flags).SetValue(o, value);
    }
    static Component[] All(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren(T(name), true)).ToArray();
    Component rules;
    Component[] infantry;
    IEnumerator Load()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Gameplay/MilitaryBase/MilitaryBase_Sandbox.unity", new LoadSceneParameters(LoadSceneMode.Single));
        Time.timeScale = 1;
        yield return null;
        rules = All("SandboxConquestRules").Single();
        infantry = All("SandboxInfantryInput");
        float until = Time.realtimeSinceStartup + 8;
        while (((Array)Get(rules, "flights")).Cast<object>().Any(f => Get(f, "current") == null) && Time.realtimeSinceStartup < until)
        { Time.timeScale = 1; yield return null; }
        Assert.IsTrue((bool)Get(All("ArenaMatch").Single(), "IsRunning"));
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        Time.timeScale = 1;
        yield return null;
    }
    [UnityTest] public IEnumerator SavedSandboxHasThreeIndependentInfantryBattlesAndSeparateAirCrews()
    {
        yield return Load();
        var zones = ((IEnumerable)Get(rules, "infantryZones")).Cast<Component>().ToArray();
        Assert.AreEqual(3, zones.Length);
        Assert.AreEqual(36, infantry.Length);
        Assert.AreEqual(48, All("ArenaParticipant").Length);
        foreach (var zone in zones)
        {
            var point = Get(zone, "point");
            var actors = infantry.Where(i => Get(i, "objective") == point).Select(i => (Component)Get(i, "participant")).ToArray();
            Assert.AreEqual(12, actors.Length);
            Assert.AreEqual(6, actors.Count(a => Get(a, "team").ToString() == "Red"));
            Assert.AreEqual(6, actors.Count(a => Get(a, "team").ToString() == "Blue"));
            Assert.NotNull(Get(zone, "respawnBox"));
        }
        var flights = ((IEnumerable)Get(rules, "flights")).Cast<object>().ToArray();
        Assert.AreEqual(8, flights.Length);
        Assert.AreEqual(4, flights.Count(f => Get(f, "battle").ToString() == "Jets"));
        Assert.AreEqual(4, flights.Count(f => Get(f, "battle").ToString() == "Helicopters"));
        foreach (var flight in flights)
        {
            Assert.NotNull(Get(flight, "current"));
            Assert.AreEqual(Get(flight, "battle").ToString() == "Jets" ? 1 : 2, ((Array)Get(flight, "crew")).Length);
        }
        foreach (string legacy in new[] { "ArenaAIInput", "ArenaVehicleAI", "CombinedArmsOrchestrator", "VehicleSpawnPoint" })
            Assert.AreEqual(0, All(legacy).Length, "Saved sandbox must not retain legacy behavior: " + legacy);
        var match = All("ArenaMatch").Single();
        var settings = Get(match, "settings");
        Assert.IsTrue((bool)Get(settings, "polishedWeaponsOnly"));
        Assert.NotNull(match.GetComponent(T("ArenaRandomLoadouts")));
        var catalog = Get(settings, "weaponCatalog");
        foreach (var actor in infantry.Select(i => (Component)Get(i, "participant")))
        {
            var choice = Get(actor, "SelectedClass"); Assert.NotNull(choice);
            foreach (string weapon in new[] { "primary", "secondary" })
                Assert.IsTrue((bool)Get(Call(catalog, "Find", Get(choice, weapon)), "polished"));
            var loadout = Get(Get(actor, "player"), "loadout");
            Assert.DoesNotThrow(() => Call(choice, "ResolveSlots", loadout), "Random scopes and attachments must be compatible with their weapon.");
        }
        var first = (Component)Get(infantry[0], "participant");
        var otherZone = infantry.First(i => Get(i, "objective") != Get(infantry[0], "objective"));
        Assert.IsFalse((bool)Call(infantry[0], "Eligible", Get(otherZone, "participant")), "Infantry cannot acquire another capture-point battle.");
        var airCrew = ((IEnumerable)Get(flights[0], "crew")).Cast<Component>().First();
        Assert.IsFalse((bool)Call(infantry[0], "Eligible", airCrew), "Infantry must ignore aircraft crews.");
        Assert.IsFalse((bool)Call(rules, "CanDamage", first, airCrew), "Air combat must not damage the isolated ground battles.");
    }
    [UnityTest] public IEnumerator InfantryReloadsPrimaryUntilBothMagazineAndReserveAreExhausted()
    {
        yield return Load();
        var input = infantry.First(i => ((Behaviour)i).enabled);
        var soldier = Get(input, "soldier"); var loadout = Get(soldier, "loadout");
        Call(Get(soldier, "actions"), "CancelActions"); Set(loadout, "IsSwitching", false);
        var slots = (Array)Get(loadout, "slots"); var available = (int[])Get(loadout, "AvailableSlots");
        Assert.GreaterOrEqual(available.Length, 2);
        var primary = Get(slots.GetValue(available[0]), "state");
        int previousMag = (int)Get(primary, "MagazineAmmo"), previousReserve = (int)Get(primary, "ReserveAmmo");
        int selected = (int)Get(loadout, "selectedSlot");
        Set(loadout, "selectedSlot", available[0]); Set(primary, "MagazineAmmo", 0); Set(primary, "ReserveAmmo", 1);
        Assert.AreEqual(0, Call(input, "ChooseWeapon"), "Empty magazine with reserve must retain primary for reload.");
        Set(primary, "ReserveAmmo", 0);
        Assert.AreEqual(2, Call(input, "ChooseWeapon"), "Only complete primary exhaustion permits sidearm selection.");
        Set(loadout, "selectedSlot", available[1]);
        Assert.AreEqual(0, Call(input, "ChooseWeapon"), "Exhausted primary must not cause swap oscillation.");
        Set(primary, "MagazineAmmo", 1);
        Assert.AreEqual(1, Call(input, "ChooseWeapon"), "A usable primary takes priority over the pistol.");
        Set(primary, "MagazineAmmo", previousMag); Set(primary, "ReserveAmmo", previousReserve); Set(loadout, "selectedSlot", selected);
    }
    [UnityTest] public IEnumerator DestroyedAircraftImmediatelyReplacesItsEntireCoupledCrew()
    {
        yield return Load();
        var flight = ((IEnumerable)Get(rules, "flights")).Cast<object>().First(f => Get(f, "battle").ToString() == "Helicopters");
        var previous = (Component)Get(flight, "current"); int generation = (int)Get(flight, "respawns");
        var enemyFlight=((IEnumerable)Get(rules,"flights")).Cast<object>()
            .First(f=>Get(f,"battle").Equals(Get(flight,"battle"))&&!Get(f,"team").Equals(Get(flight,"team")));
        var pilot=((IEnumerable)Get(flight,"crew")).Cast<Component>().First();
        var attacker=((IEnumerable)Get(enemyFlight,"crew")).Cast<Component>().First();
        Assert.IsFalse((bool)Call(rules,"CanDamage",pilot,attacker),"A cockpit hit must not bypass the coupled aircraft hull.");
        Call(previous, "ApplyDamage", (float)Get(previous, "maxHealth") + 1, null, null);
        Call(rules, "Tick", .02f);
        var replacement = (Component)Get(flight, "current");
        Assert.AreNotSame(previous, replacement);
        Assert.AreEqual(generation + 1, Get(flight, "respawns"));
        foreach (var actor in ((IEnumerable)Get(flight, "crew")).Cast<Component>())
        {
            Assert.IsFalse((bool)Get(Get(actor, "health"), "IsDead"));
            var rider = actor.GetComponent(T("VehicleOccupant"));
            Assert.IsTrue((bool)Get(rider, "IsMounted")); Assert.AreSame(replacement, Get(rider, "MountedVehicle"));
        }
        var filter = (Delegate)Get(replacement, "DamageFilter");
        Assert.IsFalse((bool)filter.DynamicInvoke(infantry[0].gameObject), "Ground weapons must not enter the isolated aircraft battle.");
        yield return null;
        foreach (var ai in All("SandboxAirCombatAI"))
        {
            var target = Get(ai, "Target") as Component;
            if (target) Assert.AreEqual(Get(ai, "battle"), Get(target.GetComponent(T("SandboxAirCombatAI")), "battle"));
        }
    }
    [UnityTest] public IEnumerator CannonRoundDamagesWithoutShovingDynamicTarget()
    {
        var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.transform.position=new Vector3(0,1000,10);
        var body=target.AddComponent<Rigidbody>();body.useGravity=false;
        var health=target.AddComponent(T("SoldierHealth"));
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Vehicles/Projectiles/HelicopterCannonRound.prefab");
        var round=UnityEngine.Object.Instantiate(prefab,new Vector3(0,1000,0),Quaternion.identity);
        var rocket=round.GetComponent(T("VehicleRocket"));
        rocket.GetType().GetMethod("Launch",new[]{typeof(GameObject),typeof(Transform),typeof(Vector3),typeof(Sprite)})
            .Invoke(rocket,new object[]{null,null,Vector3.zero,null});
        yield return new WaitForSeconds(.15f);
        Assert.Less((float)Get(health,"CurrentHealth"),100f,"The swept projectile must still damage its target.");
        Assert.Less(body.linearVelocity.magnitude,.01f,"Projectile damage must not add a collision impulse.");
        UnityEngine.Object.Destroy(target);if(round)UnityEngine.Object.Destroy(round);
    }
}
#endif
