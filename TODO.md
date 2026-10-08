# Project to-do list

Work through these stages in order. Check off items when complete; keep unfinished polish visible.

## 1. Finish the character and animation prototype

- [x] Add pistols to the shared FPS/TPS weapon setup.
- [x] Add and tune pistol idle, aim, fire, reload, equip, and unequip animations.
- [x] Make a second pass on hand grips, fingers, wrists, and elbow hints for every weapon (accepted prototype baseline).
- [ ] Polish FPS shoulder/arm offsets and consistency between weapons during swapping.
- [ ] Verify a second Polygon character variant with the shared player setup: FPS/TPS visibility, grips, reloads, sprint, and foot IK; keep appearance separate from gameplay.
- [x] Refine the TPS equip/unequip animations.

## 2. Clean up and organize the project

- [x] Audit asset references; remove unused SWAT/tutorial assets and obsolete code. Retain weapon tests and original clips for verification/rollback.
- [x] Clarify view/presentation update responsibilities within existing scripts; keep action admission and equipment switching separate.
- [x] Group current player, weapon, animation, and test files; keep imported assets isolated.
- [x] Keep authored game assets in Assets/Game and source imports in their existing folders; shared imported dependencies remain intentional.
- [x] Use one main player, weapon definitions, and equipment slots; label FPS/TPS grip settings and keep test variants separate.

Current organization: main player at `Assets/Game/Players/Prefabs/Player_Polygon.prefab`; weapon-test scenes and player variants at `Assets/Game/Tests`; weapon configuration and loadout scripts at `Assets/Game/Scripts/Weapons`. Original animation clips remain available for rollback.

## 3. Implement the local arena shooter prototype

Build a small Call of Duty-style team deathmatch prototype: two teams, fixed starting loadouts, hitscan weapons, magazine/reserve ammunition, health, death, a short respawn delay, and a score limit or match timer. Use the imported prototype map and local AI opponents as the feature testbed. Keep arena rules and feedback simple; validate the complete local loop before networking. Full multiplayer validation belongs to the NGO stage.

### Boundaries and scope

Shared systems belong under `Assets/Game/Scripts`, with reusable assets in the existing player, character, and weapon folders. Put arena-specific code, prefabs, and scenes under `Assets/Game/ArenaShooter/Scripts`, `Prefabs`, and `Scenes` as needed. Shared code must not depend on arena code.

Support multiple Polygon character appearances through player prefab variants that share gameplay scripts, weapon definitions, and animation controllers. Prefer reusing the existing skeleton/rig and replacing compatible meshes and materials; a different skeleton needs its own valid Humanoid Avatar and reassigned bone/constraint references. Character choice must not change weapon or arena rules.

Keep movement, look, equipment, weapon state, shooting, health/damage, and animation presentation reusable. Arena code owns team assignment, starting loadouts, spawn selection, respawn policy, scoring, and match lifecycle. Keep the seven-weapon setup as an animation/equipment test fixture.

Use the old Input Manager. Input requests actions; gameplay validates and owns action state; presentation responds to accepted actions. Keep the implementation local and straightforward. Preserve entry points that a future network driver can call, without adding networking abstractions yet. Later the server will validate actions and own ammo, damage, and scores.

Do not build classes, dynamic pickups, conquest, battle royale, persistence, progression, attachments, or killstreaks now. Clear boundaries should support later tests; they do not require a universal game framework or a separate script for every small responsibility.

### 3.1 Prepare the shared player for gameplay

- [x] Give the shared player a source-neutral SoldierController and command frame. Keep keyboard/mouse polling in SoldierLocalInput on a local-player prefab variant; AI supplies the same commands.
- [ ] User runtime check of the input-source refactor: local controls, source removal/disable, and death/respawn. The attempted brief check was interrupted by leaving Play Mode.
- [x] Give gameplay ownership of firing, reload progress/completion/cancellation, and equipment switching; stop using Animator state as gameplay authority.
- [x] Keep animations synchronized with accepted actions and gameplay timings; retain animation curves/events for visual timing rather than ammo/damage authority.
- [x] Keep shared weapon settings in definitions and current action state on the player/loadout; add per-owned-weapon ammo in 3.2, never in shared ScriptableObjects.
- [x] Let arena setup select starting equipment using the current slots; defer dynamic pickup/inventory construction.
- [x] Verify existing FPS/TPS aim, firing, reload, sprint, and equipment action paths after these changes (runtime checks; visual polish remains in stage 1).

### 3.2 Make one weapon and one target playable

- [x] Add shared weapon settings for damage, range, magazine capacity, reserve ammo, and reload duration alongside the existing fire mode/rate.
- [x] Store magazine and reserve counts per owned weapon; keep counts when switching equipment.
- [x] Admit semi-auto and automatic shots using the existing fire cadence; consume one round only for an accepted shot and reject firing when empty.
- [x] Resolve hitscan aim from the camera center in both FPS and TPS; check muzzle obstruction so shots cannot pass through nearby cover. Exclude the shooter's own colliders.
- [x] Add shared damageable health and body/head hit regions with a configurable headshot multiplier.
- [x] Add a stationary damageable target in an arena gameplay test scene; verify hits, misses, range, cover obstruction, and headshots.
- [x] Add reload rules: reject full-magazine/no-reserve reloads, transfer only available ammunition, block conflicting actions, and expose cancellation through CancelActions (player death hookup in 3.3).
- [x] Verify shooting, damage, empty-magazine behavior, and reload completion with one weapon before applying settings to all seven weapons.

### 3.3 Add character death and arena respawning

- [x] Apply health/damage to the shared player; emit death once, stop movement/combat, and cancel pending actions.
- [x] Add arena-owned team assignment and friendly-fire policy.
- [x] Add team spawn points and a configurable respawn delay; reset player health and equipment state according to arena rules.
- [x] Verify that death during reload/switching cannot complete stale actions after respawn, and controls/presentation recover correctly.

### 3.4 Add the team deathmatch loop and feedback

- [x] Supply fixed starting loadouts from arena configuration, using the existing weapon catalog.
- [x] Award one team point per valid enemy kill; prevent duplicate points and define suicide/friendly-fire handling.
- [x] Add a configurable score limit and match timer, a match-end result, and a local restart.
- [x] Add a minimal arena HUD: crosshair, magazine/reserve ammo, health, team scores, timer, and respawn countdown.
- [x] Add basic hit/kill feedback while preserving existing recoil, camera shake, and weapon animation presentation.
- [ ] User Play Mode walkthrough of the complete local loop: spawn, move, aim, fire, reload, switch, die, respawn, score, and end/restart. Focused rule checks passed; visual/input walkthrough is deferred.

### 3.5 Build the local AI arena demo

- [x] Integrate the player and team spawn points into ArenaShooter/Scenes/LocalOnly on the prototype map.
- [x] Bake navigation for the map and add an AI input source; shared movement continues to move the character.
- [x] Add simple enemy selection, sight checks, path following, turning/aiming, firing, and reloading through SoldierInputFrame.
- [x] Create an AI character variant with local camera/audio disabled and third-person visuals visible.
- [x] Reset AI targets/path intent on death and respawn; keep team/match rules outside the AI provider.
- [x] Add reachable standing cover with peek/return behavior, combat strafing, sprint traversal, primary/sidearm selection, and smooth imperfect aim with bursts. Keep all actions on the shared input/controller path.
- [x] Check tactical decisions in a temporary arena: cover/peek geometry and transitions, strafing, sprint requests, weapon selection, bursts, reload requests, and death/respawn resets.
- [ ] User polish pass: cover movement and timing, aim accuracy, sprinting, and weapon switching on the prototype map.
- [x] Add AI_vs_AI observation scene: twelve bots per team by default, 64 connected shared spawn locations across all map floors, twelve independent patrol points, and spectator camera/team/state labels. Player count remains adjustable.
- [x] Discover active participants automatically; support unequal teams and adding/disabling bots without editing a roster. Check 1v1, 2v3, 8v5, one-team openings, and runtime joins.
- [x] Stabilize sprint traversal with separate start/stop distances and turn thresholds; use the shared movement controller for actual speed and animation.
- [x] Score respawn candidates for occupancy, enemy proximity/visibility, and recent use; retry occupied spawns instead of overlapping characters.
- [x] Start each match with two separated team anchors and complete non-overlapping nearby formations; all later respawns use the shared safety-scored pool. Check restarts and both teams using the same marker.
- [ ] Watch a full 12v12 match and tune spawn placement, patrol routes, cover behavior, and team balance.
- [ ] User end-to-end walkthrough: human versus bots, movement, combat, cover, reloads, deaths, respawns, scoring, and restart.

## 4. Add a map

- [x] Import AngeloMaN87 Prototype Map and convert its two materials to URP/Lit.
- [ ] Set up collision, spawn points, and the basic match layout.
- [ ] Test the local gameplay loop on the map.

## 5. Build the application flow

Most of this stage can use stub implementations before real networking is added.

- [x] Use a fresh SlimUI Demo1 copy as MainMenu. Play discovers project scenes at startup, prioritizes Assets/Game, and pages the list; builds list included scenes.
- [ ] Create the local lobby flow.
- [ ] Create matchmaking screens and a cancellable stub.
- [ ] Create a server browser with listings/join stubs.
- [ ] Connect lobby and matchmaking/server browser flows to gameplay. The temporary scene launcher already loads scenes directly.
- [x] Add gameplay pause/resume and return-to-menu controls.
- [ ] Add connection/loading/failure states for the final application flow.
- [ ] User walkthrough of the Demo1 scene launcher, settings, and Escape pause/resume. SlimUI settings remain demo controls until their gameplay integration is added.

## 5.1 Build gameplay UI

- [ ] Hit marker
- [ ] Team point count
- [ ] Weapon Name
- [ ] Ammo Counter
- [ ] Health Counter
- [ ] Recoil based working crosshair 
- [ ] kill feed
- [ ] Kill medal
- [ ] Minimap?

## 6. Create the working NGO baseline

- [ ] Clone the prototype into a separate NGO project.
- [ ] Implement real backend/networking using Netcode for GameObjects (NGO).
- [ ] Replace the application-flow stubs needed for an end-to-end networked experience.
- [ ] Network player spawning, movement/look, equipment, actions, damage, and respawning.
- [ ] Implement the required host/server, joining, lobby, scene transition, and disconnect flows.
- [ ] Test the complete product flow with multiple clients.
- [ ] Record the backend/networking features and responsibilities learned from this baseline.

## 7. Create the AtlasNet implementation

- [ ] Create another separate project clone for AtlasNet.
- [ ] Use the NGO baseline as a reference for expected behavior and backend/networking features.
- [ ] Implement the equivalent application and gameplay flows with AtlasNet.
- [ ] Identify gaps in the AtlasNet framework through the working game.
- [ ] Iterate on the framework to support those requirements.
- [ ] Test the complete end-to-end flow with multiple clients and compare against the NGO baseline.
