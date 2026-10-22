using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Control scenes only. Population changes are event driven; no Update or per-frame roster scans.
public sealed class EntityPerformanceControl : MatchRules
{
    public enum Activity { Idle, CombatWithoutDamage, Combat }
    public MatchController match;
    public GameObject redPrefab, bluePrefab;
    public Vehicle vehiclePrefab;
    public ConquestCapturePoint objective;
    public int initialRed, initialBlue;
    [Min(1)] public int maximumPerTeam = 256;
    public Activity activity;
    public bool showControls = true;
    public bool unlimitedAmmo = true;
    public float teamDistance = 40, spacing = 3;
    readonly List<Unit> red = new List<Unit>(), blue = new List<Unit>();
    readonly Dictionary<MatchParticipant, Unit> owners = new Dictionary<MatchParticipant, Unit>();
    string redText = "0", blueText = "0";
    static readonly string[] ActivityLabels = { "Idle", "AI + FX", "Combat" };
    string populationLabel = "Infantry bodies / crew: 0";
    int nextId;
    bool shuttingDown;
    sealed class Unit
    {
        public int id;
        public MatchParticipant.Team team;
        public Vector3 position;
        public Quaternion rotation;
        public Vehicle vehicle;
        public GameObject root;
        public readonly List<MatchParticipant> actors = new List<MatchParticipant>();
        public bool removed;
    }
    public int RedCount => red.Count;
    public int BlueCount => blue.Count;
    public int ActorCount => owners.Count;
    public override string DisplayName => "ENTITY PERFORMANCE CONTROL";
    public override int ScoreLimit => int.MaxValue;
    public override bool HasEnded => false;
    public override bool UsesMatchTimer => false;
    public override bool UsesCustomSpawning => true;
    public override int TeamScore(MatchParticipant.Team team) => 0;
    public override void Begin(MatchController owner) => match = owner;
    public override void Tick(float deltaTime) { }
    public override void OnSpawned(MatchParticipant actor, bool wasDead) { }
    public override bool TryChooseSpawn(MatchParticipant actor, Dictionary<Transform, float> history, out Transform spawn) { spawn = null; return false; }
    public override bool CanDamage(MatchParticipant victim, MatchParticipant attacker) => activity == Activity.Combat;
    IEnumerator Start()
    {
        while (!match.IsRunning) yield return null;
        if (initialRed > 0 && red.Count == 0) SetCount(MatchParticipant.Team.Red, initialRed);
        if (initialBlue > 0 && blue.Count == 0) SetCount(MatchParticipant.Team.Blue, initialBlue);
    }
    public void SetCount(MatchParticipant.Team team, int count)
    {
        var units = team == MatchParticipant.Team.Red ? red : blue;
        count = Mathf.Clamp(count, 0, maximumPerTeam);
        while (units.Count > count) { var unit = units[units.Count - 1]; units.RemoveAt(units.Count - 1); Remove(unit); }
        while (units.Count < count) units.Add(Create(team, units.Count));
        redText = red.Count.ToString(); blueText = blue.Count.ToString();
        populationLabel = "Infantry bodies / crew: " + owners.Count;
    }
    Unit Create(MatchParticipant.Team team, int index)
    {
        float side = team == MatchParticipant.Team.Red ? -1 : 1;
        var unit = new Unit { id = nextId++, team = team,
            position = new Vector3(((index % 8 + 1) / 2) * (index % 2 == 0 ? -spacing : spacing), .15f, side * (teamDistance * .5f + index / 8 * spacing)),
            rotation = Quaternion.Euler(0, side < 0 ? 0 : 180, 0) };
        unit.root = new GameObject(team + " unit " + unit.id);
        unit.root.SetActive(false);
        if (vehiclePrefab)
        {
            unit.vehicle = Instantiate(vehiclePrefab, unit.position + Vector3.up * .5f, unit.rotation, unit.root.transform);

            unit.vehicle.Destroyed += _ => StartCoroutine(ReplaceDestroyed(unit));
        }
        int crew = unit.vehicle ? unit.vehicle.seats.Length : 1;
        for (int seat = 0; seat < crew; seat++)
        {
            var root = Instantiate(team == MatchParticipant.Team.Red ? redPrefab : bluePrefab, unit.root.transform);
            var actor = root.GetComponent<MatchParticipant>();
            actor.match = match; actor.team = team;
            root.name = team + " " + unit.id + " crew " + seat;
            var ai = root.GetComponent<AIInput>(); if (ai) ai.enabled = false;
            var simple = root.GetComponent<SandboxInfantryInput>(); if (simple) simple.enabled = false;
            var vehicleAI = root.GetComponent<VehicleAI>(); if (vehicleAI) vehicleAI.enabled = false;
            var nav = root.GetComponent<NavMeshAgent>(); if (nav) nav.enabled = false;
            var view = root.GetComponent<SoldierFirstPersonView>();
            view.firstPerson = false; view.playerCamera.enabled = false;
            root.transform.SetPositionAndRotation(unit.position + Vector3.right * (seat * 2), unit.rotation);
            owners.Add(actor, unit); unit.actors.Add(actor);
        }
        unit.root.SetActive(true);
        if (unit.vehicle)
        {
            var teamFilter = unit.vehicle.DamageFilter;
            unit.vehicle.DamageFilter = source => AllowVehicleDamage(source) && (teamFilter == null || teamFilter(source));
        }
        // MatchController processes registered joins on its next tick.
        return unit;
    }
    bool AllowVehicleDamage(GameObject source) => activity == Activity.Combat;
    public override bool TrySpawn(MatchParticipant actor)
    {
        if (!owners.TryGetValue(actor, out var unit) || unit.removed || (unit.vehicle && unit.vehicle.IsDestroyed)) return false;
        int seat = unit.actors.IndexOf(actor);
        actor.player.loadout.infiniteAmmunition = unlimitedAmmo;

        actor.Respawn(unit.position + (unit.vehicle ? Vector3.right * (10 + seat * 2) : Vector3.zero), unit.rotation);
        if (unit.vehicle)
        {
            var occupant = actor.GetComponent<VehicleOccupant>();
            occupant.faction = (int)actor.team;
            unit.vehicle.seats[seat].occupant = occupant;
            occupant.Mount(unit.vehicle, seat);
        }
        Configure(actor, unit.vehicle != null);
        return true;
    }
    void Configure(MatchParticipant actor, bool seated)
    {
        bool active = activity != Activity.Idle;
        var basic = actor.GetComponent<AIInput>();
        var simple = actor.GetComponent<SandboxInfantryInput>();
        var driver = actor.GetComponent<VehicleAI>();
        if (basic) basic.enabled = active && seated;
        if (simple) { simple.enabled = active && !seated; if (active && !seated) simple.Configure(objective); }
        actor.GetComponent<SoldierController>().SetControl(seated ? basic : simple, SoldierControlRole.AI, false);
        // Production vehicle AI dismounts on disable. Preserve the seated baseline when idling it.
        var occupant = actor.GetComponent<VehicleOccupant>();
        var vehicle = occupant.MountedVehicle;
        int seat = occupant.SeatIndex;
        if (driver) driver.enabled = active && seated;
        if (vehicle && !occupant.IsMounted)
        {
            vehicle.seats[seat].occupant = occupant;
            occupant.Mount(vehicle, seat);
        }
        var nav = actor.GetComponent<NavMeshAgent>();
        if (nav && !active) nav.enabled = false;
    }
    public void SetActivity(Activity value)
    {
        activity = value;
        foreach (var pair in owners) if (pair.Key && !pair.Key.health.IsDead) Configure(pair.Key, pair.Value.vehicle != null);
        // Idle retains the same physical bodies and rendered equipment, but supplies no movement/fire intent.
        foreach (var unit in red) if (unit.vehicle) unit.vehicle.DriverInput = default;
        foreach (var unit in blue) if (unit.vehicle) unit.vehicle.DriverInput = default;
    }
    IEnumerator ReplaceDestroyed(Unit unit)
    {
        yield return new WaitForSeconds(3);
        if (shuttingDown || unit.removed) yield break;
        var units = unit.team == MatchParticipant.Team.Red ? red : blue;
        int index = units.IndexOf(unit);
        if (index < 0) yield break;
        Remove(unit);
        units[index] = Create(unit.team, index);
    }
    void Remove(Unit unit)
    {
        unit.removed = true;
        foreach (var actor in unit.actors)
        {
            owners.Remove(actor);
            if (!actor) continue;
            var occupant = actor.GetComponent<VehicleOccupant>();
            if (occupant && occupant.IsMounted) occupant.Leave(true);
            // Mounted occupants can be reparented by the vehicle lifecycle.
            actor.gameObject.SetActive(false);
            Destroy(actor.gameObject);
        }
        if (unit.vehicle) { unit.vehicle.gameObject.SetActive(false); Destroy(unit.vehicle.gameObject); }
        unit.root.SetActive(false); Destroy(unit.root);
    }
    void OnDestroy() { shuttingDown = true; }
    void OnGUI()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F1) { showControls = !showControls; Event.current.Use(); }
        if (!showControls) return;
        GUILayout.BeginArea(new Rect(12, 48, 350, 400), GUI.skin.box);
        GUILayout.Label(vehiclePrefab ? "VEHICLE BASELINE (counts include full crews)" : "INFANTRY BASELINE");
        Row("RED", MatchParticipant.Team.Red, ref redText);
        Row("BLUE", MatchParticipant.Team.Blue, ref blueText);
        GUILayout.Label(populationLabel);
        int selected = GUILayout.SelectionGrid((int)activity, ActivityLabels, 3);
        if (selected != (int)activity) SetActivity((Activity)selected);
        GUILayout.Label("Idle: AI/navigation off; bodies and animation remain.\nAI + FX: combat without damage.\nCombat: damage, ragdolls, 3-second respawns.");
        bool unlimited = GUILayout.Toggle(unlimitedAmmo, "Unlimited infantry ammo (no reloads)");
        if (unlimited != unlimitedAmmo)
        {
            unlimitedAmmo = unlimited;
            foreach (var pair in owners)
            {
                var loadout = pair.Key.player.loadout;
                loadout.infiniteAmmunition = unlimited;
                foreach (var slot in loadout.slots) if (slot.state != null) slot.state.InfiniteOverride = unlimited;
            }
        }
        if (GUILayout.Button("Clear both teams")) { SetCount(MatchParticipant.Team.Red, 0); SetCount(MatchParticipant.Team.Blue, 0); }
        if (GUILayout.Button("Reload scene (cold population / pool reset)")) SceneManager.LoadScene(SceneManager.GetActiveScene().path);
        GUILayout.Label("RMB + WASD / Q E: fly | Shift: faster\nF1: hide controls. Keep the same camera for comparisons.\nUse Unity Profiler or a standalone Development Build.");
        GUILayout.EndArea();
    }
    void Row(string label, MatchParticipant.Team team, ref string text)
    {
        GUILayout.BeginHorizontal(); GUILayout.Label(label, GUILayout.Width(40));
        text = GUILayout.TextField(text, 3, GUILayout.Width(45));
        if (GUILayout.Button("Apply") && int.TryParse(text, out int count)) SetCount(team, count);
        int current = team == MatchParticipant.Team.Red ? red.Count : blue.Count;
        if (GUILayout.Button("-10")) SetCount(team, current - 10);
        if (GUILayout.Button("+10")) SetCount(team, current + 10);
        GUILayout.EndHorizontal();
    }
}










