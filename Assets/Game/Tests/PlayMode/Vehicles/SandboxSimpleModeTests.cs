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
    [UnityTest] public IEnumerator EachTeamHasOneLockOnJetAndAIAcquiresBeforeLaunching()
    {
        yield return Load();
        var flights=((IEnumerable)Get(rules,"flights")).Cast<object>().Where(f=>Get(f,"battle").ToString()=="Jets").ToArray();
        foreach(string team in new[]{"Red","Blue"})
        {
            var side=flights.Where(f=>Get(f,"team").ToString()==team).ToArray();
            Assert.AreEqual(2,side.Length);
            Assert.AreEqual(1,side.Count(f=>(bool)Get(((Component)Get(f,"current")).GetComponentInChildren(T("VehicleLauncher")),"requiresLock")));
        }
        foreach(var flight in ((IEnumerable)Get(rules,"flights")).Cast<object>())
        {
            var v=(Component)Get(flight,"current");
            ((Behaviour)v.GetComponent(T("SandboxAirCombatAI"))).enabled=false;
            v.GetComponent<Rigidbody>().isKinematic=true;
            var countermeasures=v.GetComponent(T("VehicleCountermeasures"));if(countermeasures)((Behaviour)countermeasures).enabled=false;
        }
        var source=flights.First(f=>Get(f,"team").ToString()=="Red" && (bool)Get(((Component)Get(f,"current")).GetComponentInChildren(T("VehicleLauncher")),"requiresLock"));
        var target=flights.First(f=>Get(f,"team").ToString()=="Blue");
        var aircraft=(Component)Get(source,"current");var opposing=(Component)Get(target,"current");
        aircraft.transform.SetPositionAndRotation(new Vector3(0,1000,0),Quaternion.identity);
        opposing.transform.SetPositionAndRotation(new Vector3(0,1000,250),Quaternion.identity);
        foreach(var f in flights.Where(f=>f!=source&&f!=target))((Component)Get(f,"current")).transform.position=new Vector3(2000,1000,2000);
        Physics.SyncTransforms();
        var ai=aircraft.GetComponent(T("SandboxAirCombatAI"));var opponent=opposing.GetComponent(T("SandboxAirCombatAI"));
        Set(ai,"enemy",opponent);
        var pilot=((IEnumerable)Get(source,"crew")).Cast<Component>().First().GetComponent(T("VehicleOccupant"));
        var launcher=aircraft.GetComponentInChildren(T("VehicleLauncher"));
        Set(launcher,"lockDuration",.15f);
        int rounds=(int)Get(launcher,"Missiles");
        Call(ai,"FireStation",pilot);
        Assert.AreEqual(rounds,Get(launcher,"Missiles"),"AI cannot launch before obtaining the normal player seeker lock.");
        float until=Time.time+1;
        while((int)Get(launcher,"Missiles")==rounds&&Time.time<until)
        {Call(ai,"FireStation",pilot);yield return null;}
        Assert.Less((int)Get(launcher,"Missiles"),rounds,"AI should acquire and fire a guided missile at an aligned enemy.");
        Assert.IsTrue((bool)Get(launcher,"IsLocked"));
        var systems=aircraft.GetComponent(T("VehicleWeaponSystems"));
        var guidance=T("VehicleMissileGuidance").GetField("Active").GetValue(null) as IList;
        Assert.IsTrue(guidance.Cast<Component>().Any(m=>(UnityEngine.Object)Get(m,"Target")==opposing),"Missile must track the validated hostile aircraft.");
        Set(launcher,"missiles",0);Set(launcher,"nextRecharge",Time.time+10);
        Call(ai,"FireStation",pilot);
        var current=systems.GetType().GetMethods(Flags).Single(m=>m.Name=="Current"&&m.GetParameters()[0].ParameterType.Name=="VehicleOccupant").Invoke(systems,new object[]{pilot});
        Assert.AreEqual("Gun",Get(current,"system").ToString(),"Empty missile racks must fall back to the cannon.");
    }
    [UnityTest] public IEnumerator FriendlyMarkersFollowViewedTeamAndHideSelfEnemiesAndOffscreenPlayers()
    {
        yield return Load();
        var spectator=All("ArenaAISpectator").Single();
        Call(spectator,"SetManualSpectating",true);Call(spectator,"SetInterestMode",false);
        var actors=infantry.Select(i=>(Component)Get(i,"participant")).ToArray();
        var viewer=actors.First(a=>Get(a,"team").ToString()=="Red");
        var friendly=actors.First(a=>a!=viewer&&Get(a,"team").ToString()=="Red");
        var enemy=actors.First(a=>Get(a,"team").ToString()=="Blue");
        Time.timeScale=0;
        Call(spectator,"Follow",viewer);Call(spectator,"EnterPerspective");yield return null;
        var markers=All("ArenaFriendlyMarkers").Single();
        var camera=(Camera)Get(spectator,"PresentationCamera");
        var animator=(Animator)Get(Get(Get(friendly,"player"),"actions"),"animator");
        Vector3 head=animator.GetBoneTransform(HumanBodyBones.Head).position;
        camera.transform.SetPositionAndRotation(head+Vector3.back*10,Quaternion.identity);
        Call(markers,"LateUpdate");
        Assert.IsTrue((bool)Call(markers,"IsVisible",friendly));
        Assert.IsFalse((bool)Call(markers,"IsVisible",viewer));
        Assert.IsFalse((bool)Call(markers,"IsVisible",enemy));
        var root=(RectTransform)Get(markers,"root");
        Assert.AreEqual(0,root.GetComponentsInChildren(T("ArenaUiPerspective"),true).Length,"World markers cannot inherit decorative HUD tilt or look lag.");
        camera.transform.rotation=Quaternion.Euler(0,180,0);Call(markers,"LateUpdate");
        Assert.IsFalse((bool)Call(markers,"IsVisible",friendly),"Behind-camera markers must be hidden.");
        camera.transform.rotation=Quaternion.identity;
        friendly.gameObject.SetActive(false);Call(markers,"LateUpdate");
        Assert.IsFalse((bool)Call(markers,"IsVisible",friendly));friendly.gameObject.SetActive(true);
        Call(spectator,"Follow",enemy);Call(spectator,"EnterPerspective");yield return null;
        camera=(Camera)Get(spectator,"PresentationCamera");
        camera.transform.SetPositionAndRotation(head+Vector3.back*10,Quaternion.identity);Call(markers,"LateUpdate");
        Assert.IsFalse((bool)Call(markers,"IsVisible",friendly),"Changing spectator teams must immediately change friendly identification.");
        Time.timeScale=1;
    }
    [UnityTest] public IEnumerator SpectatorAircraftHudChangesProfileInBothViewsWithoutStaleInstruments()
    {
        yield return Load();
        var spectator=All("ArenaAISpectator").Single();
        Call(spectator,"SetManualSpectating",true);Call(spectator,"SetInterestMode",false);
        yield return null;
        var hud=All("ArenaVehicleHud").Single();
        var flights=((IEnumerable)Get(rules,"flights")).Cast<object>().ToArray();
        foreach(string reference in new[]{"F15","Cobra","Su27","LittleBird","F15"})
        {
            var flight=flights.First(f=>Get(Get(f,"prefab"),"instrumentReference").ToString()==reference);
            var pilot=((IEnumerable)Get(flight,"crew")).Cast<Component>().First();
            Call(spectator,"Follow",pilot);
            Call(spectator,"ExitPerspective");
            for(int view=0;view<2;view++)
            {
                if(view==1)Call(spectator,"EnterPerspective");
                yield return null;yield return null;
                bool jet=reference=="F15"||reference=="Su27",dials=reference=="LittleBird";
                Assert.AreEqual(!dials,((GameObject)Get(hud,"flightPanel")).activeInHierarchy,reference);
                Assert.AreEqual(jet,(bool)Get(Get(hud,"fixedWingReadout"),"Visible"),reference);
                Assert.AreEqual(dials,((GameObject)Get(Get(hud,"referenceInstruments"),"panel")).activeInHierarchy,reference);
                Assert.AreEqual(reference=="Su27",((Component)Get(hud,"metricPitchScale")).gameObject.activeInHierarchy);
                if(!dials)StringAssert.StartsWith(jet?"IAS":"GS",(string)Get(Get(hud,"speedReadout"),"text"));
                if(jet)Assert.IsFalse(((Component)Get(hud,"gunnerReadout")).gameObject.activeInHierarchy,"Fixed jet cannons do not show turret instruments.");
            }
        }
    }
    [UnityTest] public IEnumerator SpectatorInterestSwitchesForQuietTimeDwellAndVarietyWithoutInterruptingDeathWatch()
    {
        yield return Load();
        var spectator = All("ArenaAISpectator").Single();
        Assert.IsTrue((bool)Get(spectator, "InterestMode"), "Interest mode starts enabled.");
        Call(spectator, "SetManualSpectating", true);
        Set(spectator, "ControlsBlocked", false);
        Call(spectator, "SetInterestMode", true);
        var director = spectator.GetComponent(T("ArenaSpectatorInterest"));
        // Drive the director deterministically while gameplay continues normally.
        ((Behaviour)director).enabled = false;
        Call(director, "SetEnabled", true);
        var actors = All("ArenaParticipant").Where(a => (bool)Call(spectator, "CanFollowFiltered", a)).ToArray();
        var foot = actors.Where(a => !((bool)Get(a.GetComponent(T("VehicleOccupant")), "IsMounted"))).Take(3).ToArray();
        Assert.AreEqual(3, foot.Length);
        Call(spectator, "Follow", foot[0]);
        float now = Time.time;
        Set(director, "lastActionAt", now - 3.1f);
        // Clear historical shots so this specifically exercises the quiet timeout.
        foreach (var actor in actors) Set(actor, "<LastShotTime>k__BackingField", float.NegativeInfinity);
        Call(director, "Tick", now);
        Assert.AreNotEqual(foot[0], Get(spectator, "FollowedCharacter"));
        Call(spectator, "Follow", foot[0]);
        Call(foot[0], "RecordShotAt", foot[0].transform.position);
        Set(director, "followedAt", now - 15.1f);
        Call(director, "Tick", now);
        Assert.AreNotEqual(foot[0], Get(spectator, "FollowedCharacter"), "Even active targets have a maximum dwell.");
        Call(spectator, "Follow", foot[0]);
        Call(spectator, "Follow", foot[1]);
        Call(spectator, "Follow", foot[2]);
        var varied = (Component)Call(director, "Choose", now);
        Assert.IsTrue((bool)Get(varied.GetComponent(T("VehicleOccupant")), "IsMounted"), "Fourth selection must change unit type when available.");
        Set(director, "lastActionAt", now - 4);
        Set(director, "followedAt", now - 20);
        Set(spectator, "deathWatch", true);
        Call(director, "Tick", now);
        Assert.AreEqual(foot[2], Get(spectator, "FollowedCharacter"), "Existing death camera must finish first.");
        Set(spectator, "deathWatch", false);
        var filterType = Get(spectator, "Filter").GetType();
        Call(spectator, "SetFilter", Enum.Parse(filterType, "Infantry"));
        var filtered = (Component)Call(director, "Choose", now);
        Assert.IsFalse((bool)Get(filtered.GetComponent(T("VehicleOccupant")), "IsMounted"));
        Call(spectator, "SetInterestMode", false);
        var held = Get(spectator, "FollowedCharacter");
        Call(director, "Tick", now + 30);
        Assert.AreEqual(held, Get(spectator, "FollowedCharacter"));
        yield return null;
    }
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
    [UnityTest] public IEnumerator SandboxTeamsUsePreparedInfantryLooksOnSharedRigs()
    {
        yield return Load();
        string[] Appearance(Component actor)
        {
            var view=actor.GetComponent(T("SoldierFirstPersonView"));
            var root=(Transform)Get(view,"thirdPersonRoot");
            var rider=actor.GetComponent(T("VehicleOccupant"));
            var saved=rider?Get(rider,"renderers") as IDictionary:null;
            return root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.StartsWith("SM_Chr_")
                &&(saved!=null&&saved.Contains(r)?(bool)saved[r]:r.enabled))
                .Select(r=>r.name+":"+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"null"))).OrderBy(n=>n).ToArray();
        }
        var red=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Matches/Player_Polygon_AI.prefab");
        var blue=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Matches/Player_Polygon_AI_Skin01.prefab");
        var redLook=Appearance(red.transform);var blueLook=Appearance(blue.transform);
        CollectionAssert.AreNotEqual(redLook,blueLook);
        var actors=All("ArenaParticipant");Assert.AreEqual(48,actors.Length);
        foreach(var actor in actors)
        {
            CollectionAssert.AreEqual(Get(actor,"team").ToString()=="Blue"?blueLook:redLook,Appearance(actor),actor.name);
            var view=actor.GetComponent(T("SoldierFirstPersonView"));
            var root=(Transform)Get(view,"thirdPersonRoot");
            var rider=actor.GetComponent(T("VehicleOccupant"));
            var saved=Get(rider,"renderers") as IDictionary;
            foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(saved.Contains(skin)&&!(bool)saved[skin])Assert.IsFalse(skin.enabled,"Mounting must not enable the other skin stored in a prefab variant.");
                if(!skin.enabled)continue;
                Assert.IsNotNull(skin.rootBone);
                Assert.IsTrue(skin.rootBone.IsChildOf(actor.transform));
                foreach(var bone in skin.bones)Assert.IsTrue(bone&&bone.IsChildOf(actor.transform),"Skins must use the live actor rig, not prefab asset bones.");
            }
        }
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
            if((bool)Get(actor,"IsAwaitingSpawn"))continue; // Local player now chooses deployment before receiving equipment.
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
    [UnityTest] public IEnumerator PlayerDeploymentReplacesClassSelectionAndSupportsFlagsVehiclesAndPause()
    {
        yield return Load();
        yield return null;
        var menu=All("ConquestDeploymentMenu").Single();
        var actor=(Component)Get(menu,"participant");
        Assert.IsTrue((bool)Get(menu,"IsVisible"));
        Assert.IsTrue((bool)Get(actor,"IsAwaitingSpawn"));
        Assert.IsFalse((bool)Get(actor,"NeedsClassSelection"));
        Assert.IsFalse((bool)Get(Get(menu,"classMenu"),"IsVisible"));
        var points=((IEnumerable)Get(rules,"points")).Cast<Component>().ToArray();
        foreach(var point in points)
        {
            if(!(bool)Get(actor,"IsAwaitingSpawn"))Call(menu,"OpenRedeployment");
            Call(menu,"SelectFlag",point);Call(menu,"RequestDeployment");
            yield return null;yield return null;
            Assert.IsFalse((bool)Get(actor,"IsAwaitingSpawn"));
            Assert.IsFalse((bool)Get(menu,"IsVisible"));
            Assert.AreEqual(point,Get(actor.GetComponent(T("SandboxInfantryInput")),"objective"));
            Assert.NotNull(Get(actor,"SelectedClass"));
        }
        var pause=Get(menu,"pause");Call(pause,"Pause");
        Call(Get(menu,"classMenu"),"Open");
        Assert.IsFalse((bool)Get(pause,"IsPaused"));
        Assert.IsTrue((bool)Get(menu,"IsVisible"));
        Assert.IsTrue((bool)Call(menu,"CloseTacticalMap"));
        var vehicles=((IEnumerable)Get(rules,"deploymentVehicles")).Cast<Component>().ToArray();
        Assert.Greater(vehicles.Length,0);
        Component previous=null;
        foreach(string name in new[]{"PickupTechnical","Jet01","AttackHelicopter01"})
        {
            var prefab=vehicles.Single(v=>v.name==name);
            Call(menu,"OpenRedeployment");Call(menu,"SelectBase");Call(rules,"SelectVehicle",prefab);Call(menu,"RequestDeployment");
            yield return null;yield return null;
            var current=(Component)Get(rules,"PlayerVehicle");
            Assert.IsTrue(current,name+" should spawn at HQ");
            Assert.IsFalse((bool)Get(menu,"IsVisible"));
            Assert.IsTrue((bool)Get(actor.GetComponent(T("VehicleOccupant")),"IsMounted"));
            if(previous)Assert.AreNotSame(previous,current);
            previous=current;
            Call(menu,"OpenRedeployment");Call(menu,"CloseTacticalMap");
            Assert.AreSame(current,Get(rules,"PlayerVehicle"),"Cancel must retain the current vehicle");
            Assert.IsFalse((bool)Get(Get(menu,"input"),"InputBlocked"));
        }
        Call(menu,"OpenRedeployment");Call(menu,"SelectFlag",points[0]);Call(menu,"RequestDeployment");
        yield return null;yield return null;
        Assert.IsFalse((bool)Get(actor.GetComponent(T("VehicleOccupant")),"IsMounted"));
        Assert.IsNull(Get(rules,"PlayerVehicle"));
        Assert.IsFalse((bool)Get(Get(menu,"input"),"InputBlocked"));
    }
    [UnityTest] public IEnumerator SpectatorFiltersCycleAndRecoverWhenMatchingVehiclesBecomeAvailable()
    {
        yield return Load();yield return null;
        var spectator=All("ArenaAISpectator").Single();
        var filterType=T("ArenaAISpectator").GetNestedType("FollowFilter");
        Call(spectator,"SetManualSpectating",true);yield return null;
        var hud=All("ArenaSpectatorHud").Single();
        hud.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="AIRBORNE VEHICLES").onClick.Invoke();
        Assert.AreEqual("AirborneVehicles",Get(spectator,"Filter").ToString(),"HUD buttons must apply the follow filter");
        foreach(string filter in new[]{"Infantry","AirborneVehicles"})
        {
            Call(spectator,"SetFilter",Enum.Parse(filterType,filter));
            for(int i=0;i<6;i++)
            {
                Call(spectator,"CycleCharacter",1);
                var followed=(Component)Get(spectator,"FollowedCharacter");
                Assert.IsTrue(followed,filter+" should have a living target");
                Assert.IsTrue((bool)Call(spectator,"MatchesFilter",followed));
                var rider=followed.GetComponent(T("VehicleOccupant"));
                Assert.AreEqual(filter=="AirborneVehicles",(bool)Get(rider,"IsMounted"));
                if(filter=="AirborneVehicles")Assert.IsTrue((bool)Get(Get(rider,"MountedVehicle"),"IsAircraft"));
            }
        }
        Call(spectator,"SetFilter",Enum.Parse(filterType,"GroundVehicles"));
        Assert.IsNull(Get(spectator,"FollowedCharacter"));
        Assert.IsTrue((bool)Get(spectator,"WaitingForFilteredTarget"));
        var prefab=((IEnumerable)Get(rules,"deploymentVehicles")).Cast<Component>().Single(v=>v.name=="PickupTechnical");
        var vehicle=UnityEngine.Object.Instantiate(prefab,new Vector3(-450,12,0),Quaternion.identity);
        try
        {
            var input=infantry.First(i=>((Behaviour)i).enabled);
            var rider=input.GetComponent(T("VehicleOccupant"));
            Set(((Array)Get(vehicle,"seats")).GetValue(0),"occupant",rider);
            Call(rider,"Mount",vehicle,0);
            yield return new WaitForSecondsRealtime(.65f);
            Assert.AreSame(Get(input,"participant"),Get(spectator,"FollowedCharacter"));
            Assert.IsFalse((bool)Get(spectator,"WaitingForFilteredTarget"));
            Call(rider,"Leave",true);yield return null;
            Assert.IsNull(Get(spectator,"FollowedCharacter"),"Dismounting removes a ground-vehicle target");
            Call(spectator,"SetFilter",Enum.Parse(filterType,"All"));
            Assert.IsNotNull(Get(spectator,"FollowedCharacter"));
            Call(spectator,"StopFollowing");
            Assert.IsFalse((bool)Get(spectator,"WaitingForFilteredTarget"),"Free camera must cancel automatic follow retries");
        }
        finally{if(vehicle)UnityEngine.Object.Destroy(vehicle.gameObject);}
    }
    [UnityTest] public IEnumerator DefenseOnlyVehicleEquipmentStaysVisibleBetweenReadoutRefreshes()
    {
        yield return Load();yield return null;
        var spectator=All("ArenaAISpectator").Single();var hud=All("ArenaVehicleHud").Single();
        Call(spectator,"SetInterestMode",false);
        var flight=((IEnumerable)Get(rules,"flights")).Cast<object>().First(f=>Get(f,"battle").ToString()=="Jets");
        var vehicle=(Component)Get(flight,"current");
        ((Behaviour)vehicle.GetComponent(T("SandboxAirCombatAI"))).enabled=false;
        vehicle.GetComponent<Rigidbody>().isKinematic=true;
        var pilot=((IEnumerable)Get(flight,"crew")).Cast<Component>().First();
        var systems=vehicle.GetComponent(T("VehicleWeaponSystems"));
        var defenses=vehicle.GetComponent(T("VehicleCountermeasures"));
        Assert.IsNotNull(defenses);
        Set(systems,"weapons",Array.CreateInstance(Get(systems,"weapons").GetType().GetElementType(),0));
        Set(defenses,"automaticForAI",false);
        Call(spectator,"SetManualSpectating",true);Call(spectator,"Follow",pilot);
        yield return null;
        foreach(bool smoke in new[]{false,true})
        {
            Set(defenses,"smoke",smoke);Set(defenses,"flares",!smoke);Set(defenses,"chaff",false);
            Set(hud,"nextReadout",float.NegativeInfinity);Call(hud,"Update");
            Assert.AreEqual(smoke?"SMOKE":"FLARES",Get(Get(hud,"equippedName"),"text"));
            Assert.IsFalse(((UnityEngine.UI.Image)Get(hud,"equippedIcon")).gameObject.activeSelf);
            // Hold the readout cache unchanged: visibility must survive the intervening frames.
            Set(hud,"nextReadout",float.PositiveInfinity);
            for(int i=0;i<8;i++)
            {
                yield return null;
                Assert.IsTrue(((GameObject)Get(hud,"vehicleEquipment")).activeInHierarchy,
                    "A defense-only station must not disappear between text refreshes.");
            }
        }
        Set(hud,"nextReadout",float.NegativeInfinity);
    }
    [UnityTest] public IEnumerator SpectatorVehicleHudUsesObservedOccupantAndBothPresentationCameras()
    {
        yield return Load();yield return null;
        var spectator=All("ArenaAISpectator").Single();var hud=All("ArenaVehicleHud").Single();
        Call(spectator,"SetInterestMode",false); // This test explicitly controls the followed target.
        Call(spectator,"SetManualSpectating",true);yield return null;
        Assert.IsNull(Get(hud,"player"));
        var flight=((IEnumerable)Get(rules,"flights")).Cast<object>().First(f=>Get(f,"battle").ToString()=="Jets");
        var pilot=((IEnumerable)Get(flight,"crew")).Cast<Component>().First();
        Call(spectator,"Follow",pilot);yield return null;yield return null;
        Assert.AreSame(pilot.GetComponent(T("VehicleOccupant")),Get(hud,"player"));
        Assert.IsTrue(((GameObject)Get(hud,"mountedPanel")).activeInHierarchy);
        Assert.IsTrue(((GameObject)Get(hud,"vehicleEquipment")).activeInHierarchy);
        Assert.IsFalse(((GameObject)Get(hud,"infantryEquipment")).activeSelf);
        Assert.AreSame(Get(spectator,"spectatorCamera"),Get(hud,"PresentationCamera"));
        var preview = (Camera)Get(spectator,"InsetCamera");
        Assert.IsNotNull(preview); Assert.IsFalse(preview.enabled, "The monitor must not render automatically every frame.");
        Assert.IsTrue((bool)Get(Get(Get(pilot,"player"),"view"),"NeedsCameraAnimation"), "A scheduled FPS preview still owns its animation graph.");
        int captures=0;bool leanPreview=true;
        var previewData=preview.GetComponent(T("UniversalAdditionalCameraData"));
        bool originalShadows=(bool)Get(previewData,"renderShadows"),originalPost=(bool)Get(previewData,"renderPostProcessing");
        void CountPreview(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        { if(camera==preview){captures++;leanPreview &= !(bool)Get(previewData,"renderShadows")&&!(bool)Get(previewData,"renderPostProcessing");} }
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += CountPreview;
        try { yield return new WaitForSecondsRealtime(.25f); }
        finally { UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= CountPreview; }
        Assert.Greater(captures,0); Assert.LessOrEqual(captures,9, "Small-monitor captures must respect the 30 Hz budget.");
        Assert.IsTrue(leanPreview,"Monitor captures must not duplicate shadow maps and post processing.");
        Assert.AreEqual(originalShadows,Get(previewData,"renderShadows"));Assert.AreEqual(originalPost,Get(previewData,"renderPostProcessing"));
        Call(spectator,"EnterPerspective");yield return null;
        Assert.AreSame(Get(Get(Get(pilot,"player"),"view"),"playerCamera"),Get(hud,"PresentationCamera"));
        var groundPrefab=((IEnumerable)Get(rules,"deploymentVehicles")).Cast<Component>().Single(v=>v.name=="PickupTechnical");
        var ground=UnityEngine.Object.Instantiate(groundPrefab,new Vector3(-450,8,0),Quaternion.identity);
        try
        {
            var actor=(Component)Get(infantry.First(i=>((Behaviour)i).enabled),"participant");
            var rider=actor.GetComponent(T("VehicleOccupant"));
            var seats=(Array)Get(ground,"seats");int gunner=Enumerable.Range(0,seats.Length).First(i=>Get(seats.GetValue(i),"role").ToString()=="Gunner");
            Set(seats.GetValue(gunner),"occupant",rider);Call(rider,"Mount",ground,gunner);
            var actorView = (Component)Get(Get(actor,"player"),"view");
            var bodyMeshes = ((Transform)Get(actorView,"thirdPersonRoot")).GetComponentsInChildren<MeshRenderer>(true);
            var gpuModes = bodyMeshes.Select(m => m.HasPropertyBlock()).ToArray();
            Call(spectator,"Follow",actor);yield return null;yield return new WaitForSecondsRealtime(.08f);
            Assert.IsTrue(((Transform)Get(actorView,"thirdPersonRoot")).GetComponentsInChildren<MeshRenderer>(true).All(m => m.HasPropertyBlock()), "Per-camera body hiding must bypass GPU Resident Drawer.");
            Assert.AreSame(rider,Get(hud,"player"));Assert.IsTrue(((GameObject)Get(hud,"vehicleEquipment")).activeInHierarchy);
            Call(rider,"Leave",true);yield return null;yield return null;
            Assert.IsFalse(((GameObject)Get(hud,"mountedPanel")).activeSelf);
            Assert.IsTrue(((GameObject)Get(hud,"infantryEquipment")).activeSelf);
            Call(spectator,"StopFollowing");yield return null;
            for (int i=0;i<bodyMeshes.Length;i++) if(bodyMeshes[i] && bodyMeshes[i].transform.IsChildOf(actor.transform)) Assert.AreEqual(gpuModes[i],bodyMeshes[i].HasPropertyBlock());
            Assert.IsFalse(((GameObject)Get(hud,"vehicleEquipment")).activeSelf);
        }
        finally{if(ground)UnityEngine.Object.Destroy(ground.gameObject);}
    }
    [UnityTest] public IEnumerator CharacterSpeedLimitAndRectangularBoundaryKillWallsWork()
    {
        yield return Load();
        Assert.AreEqual(3,(float)Get(rules,"vehicleRespawnDelay"));
        Assert.AreEqual(3,(float)Get(Get(All("ArenaMatch").Single(),"settings"),"respawnDelay"));
        var input=infantry.First(i=>((Behaviour)i).enabled);((Behaviour)input).enabled=false;
        var actor=(Component)Get(input,"participant");var body=actor.GetComponent<Rigidbody>();
        body.position=new Vector3(-450,20,0);body.linearVelocity=Vector3.right*10000;
        yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
        Assert.LessOrEqual(body.linearVelocity.magnitude,151,"Native speed limit must contain extreme impulses");
        Assert.AreEqual(0,body.linearDamping,"Do not add drag to ordinary locomotion");
        var walls=All("FallKillVolume").Where(v=>!(bool)Get(v,"pollRoster")).ToArray();Assert.AreEqual(4,walls.Length);
        foreach(var wall in walls)Assert.IsTrue(wall.GetComponent<BoxCollider>().isTrigger);
        var west=walls.Single(v=>v.name=="West kill wall");
        body.position=new Vector3(west.transform.position.x,10,west.transform.position.z);body.linearVelocity=Vector3.zero;
        yield return new WaitForSeconds(.15f);
        Assert.IsTrue((bool)Get(Get(actor,"health"),"IsDead"),"Crossing a boundary must use normal death/respawn");
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
    [UnityTest] public IEnumerator DestroyedAircraftWaitsThenReplacesItsEntireCoupledCrew()
    {
        yield return Load();
        var flight = ((IEnumerable)Get(rules, "flights")).Cast<object>().First(f => Get(f, "battle").ToString() == "Helicopters");
        var previous = (Component)Get(flight, "current"); int generation = (int)Get(flight, "respawns");
        var enemyFlight=((IEnumerable)Get(rules,"flights")).Cast<object>()
            .First(f=>Get(f,"battle").Equals(Get(flight,"battle"))&&!Get(f,"team").Equals(Get(flight,"team")));
        var pilot=((IEnumerable)Get(flight,"crew")).Cast<Component>().First();
        var attacker=((IEnumerable)Get(enemyFlight,"crew")).Cast<Component>().First();
        Assert.IsFalse((bool)Call(rules,"CanDamage",pilot,attacker),"A cockpit hit must not bypass the coupled aircraft hull.");
        Set(rules,"vehicleRespawnDelay",.6f);
        var spectator=All("ArenaAISpectator").Single();Call(spectator,"SetManualSpectating",true);yield return null;
        Assert.IsNull(Get(spectator,"FollowedCharacter"),"Spectating starts in free roam");
        Call(spectator,"Follow",pilot);
        Call(previous, "ApplyDamage", (float)Get(previous, "maxHealth") + 1, null, null);
        Call(rules, "Tick", .02f);
        Assert.AreSame(previous,Get(flight,"current"),"Do not replace the aircraft before the timer expires");
        Assert.AreEqual(generation,Get(flight,"respawns"));
        Assert.IsNull(Get(spectator,"FollowedCharacter"),"Crew death temporarily switches to free roam");
        Assert.Greater((float)Get(spectator,"VehicleRespawnRemaining"),0);
        yield return new WaitForSeconds(.75f);
        var replacement = (Component)Get(flight, "current");
        Assert.AreNotSame(previous, replacement);
        Assert.AreEqual(generation + 1, Get(flight, "respawns"));
        Assert.AreSame(pilot,Get(spectator,"FollowedCharacter"),"Resume the observed crew after its timed replacement");
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
    [UnityTest] public IEnumerator LocalVehicleDeathUsesFreeCameraUntilDeploymentTimerFinishes()
    {
        yield return Load();yield return null;
        Set(rules,"vehicleRespawnDelay",.6f);
        var menu=All("ConquestDeploymentMenu").Single();var actor=(Component)Get(menu,"participant");
        var spectator=All("ArenaAISpectator").Single();
        var prefab=((IEnumerable)Get(rules,"deploymentVehicles")).Cast<Component>().Single(v=>v.name=="PickupTechnical");
        Call(rules,"SelectVehicle",prefab);Call(menu,"SelectBase");Call(menu,"RequestDeployment");
        yield return null;yield return null;
        var vehicle=Get(rules,"PlayerVehicle");Assert.IsNotNull(vehicle);
        Call(vehicle,"ApplyDamage",(float)Get(vehicle,"maxHealth"),null,null);
        yield return null;
        Assert.IsTrue((bool)Get(spectator,"IsSpectating"));
        Assert.IsNull(Get(spectator,"FollowedCharacter"));
        Assert.Greater((float)Get(spectator,"VehicleRespawnRemaining"),0);
        Assert.IsFalse((bool)Get(menu,"IsVisible"),"The waiting camera should be usable until the timer expires");
        Assert.IsFalse((bool)Get(spectator,"ControlsBlocked"));
        yield return new WaitForSeconds(.8f);
        Assert.IsTrue((bool)Get(menu,"IsVisible"),"Return to deployment selection once the timer finishes");
        Call(menu,"SelectFlag",((Array)Get(rules,"points")).GetValue(0));Call(menu,"RequestDeployment");
        yield return null;yield return null;
        Assert.IsFalse((bool)Get(actor,"IsAwaitingSpawn"));Assert.IsFalse((bool)Get(Get(actor,"health"),"IsDead"));
        Assert.IsFalse((bool)Get(spectator,"IsSpectating"));
    }
    [UnityTest] public IEnumerator InfantrySpawnsStaySupportedAndBelowMapEscapeRespawnsNormally()
    {
        yield return Load();
        foreach(var zone in ((IEnumerable)Get(rules,"infantryZones")).Cast<Component>())
        {
            var actor=(Component)Get(infantry.First(i=>((Behaviour)i).enabled&&Get(i,"objective")==Get(zone,"point")),"participant");
            for(int i=0;i<20;i++)
            {
                object[] arguments={actor,Vector3.zero};
                Assert.IsTrue((bool)zone.GetType().GetMethod("TryPosition").Invoke(zone,arguments));
                Vector3 p=(Vector3)arguments[1];
                Assert.IsTrue(Physics.Raycast(p+Vector3.up*.5f,Vector3.down,out _,1.1f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),"Spawn must have physical support");
                actor.GetType().GetMethod("Respawn",new[]{typeof(Vector3),typeof(Quaternion)}).Invoke(actor,new object[]{p,Quaternion.identity});
                Assert.Less(actor.GetComponent<Rigidbody>().linearVelocity.magnitude,.01f,"Respawn must reset velocity");
                yield return null;
            }
        }
        var victim=(Component)Get(infantry.First(i=>((Behaviour)i).enabled),"participant");
        var health=Get(victim,"health");int deaths=(int)Get(victim,"Deaths");
        var body=victim.GetComponent<Rigidbody>();body.position=new Vector3(0,-1000,0);body.linearVelocity=Vector3.zero;
        yield return new WaitForSeconds(.4f);
        Assert.IsTrue((bool)Get(health,"IsDead"),"Safety must catch a body below the entire trigger, without OnTriggerEnter");
        Assert.AreEqual(deaths+1,Get(victim,"Deaths"));
        Call(victim,"CancelRespawn");
        Assert.IsTrue((bool)Call(Get(victim,"match"),"Spawn",victim));
        Assert.IsFalse((bool)Get(health,"IsDead"));Assert.Greater(body.position.y,-1);
        ((Behaviour)victim.GetComponent(T("SandboxInfantryInput"))).enabled=false;
        body.position=new Vector3(-450,12,0);body.linearVelocity=Vector3.down*200;
        yield return new WaitForSeconds(.3f);
        Assert.Greater(body.position.y,-1,"Fast downward motion must hit the solid backing");
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
