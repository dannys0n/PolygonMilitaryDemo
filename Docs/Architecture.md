# Prototype architecture

The offline sandbox is the common starting point for separate NGO and AtlasNet branches. There is no runtime backend selector or networking dependency in gameplay.

## Source of truth

- Gameplay map: `Assets/Game/Scenes/Gameplay/MilitaryBase/MilitaryBase_Sandbox.unity`.
- Equipment: `Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset`. Polished flags drive sandbox availability. IDs and Unity GUIDs must remain stable; display names may change.
- Shared character rigs and equipped weapon views stay under the existing game prefab/model folders. Source animation imports remain authoring inputs, not duplicate gameplay implementations.

## Boundaries

- `Game.Runtime`: offline gameplay, presentation, and scene-based prototype fixtures.
- `Game.Editor`: game authoring, import/conversion helpers, and validation; excluded from players.
- `FPSAnimationPack.Editor`: imported animation lab tooling, referenced only by editor tools.
- `SlimUI.Runtime`: imported menu implementation.
- Test assemblies reference the game runtime, not the editor tool assembly.

## Character control

`SoldierController.controlRole` explicitly identifies Human or AI. `isLocalPlayer` separately identifies local input/camera ownership. A remote human is Human with local ownership disabled; neither field determines network simulation authority.

Configure dynamically created inactive actors through `SetControl(source, role, localPlayer)` before activation. The serialized prefab values cover ordinary scene actors. `SoldierLife.ResetHealth()` applies 300 health to humans and 150 to AI at initialization and respawn.

Infantry input adapters produce `SoldierInputFrame`. Local vehicle input is collected in `SoldierLocalInput.ReadVehicleInput`; `VehicleOccupant.ApplyControl` accepts the resulting `VehicleControlFrame`. AI flight adapters retain the shared `VehicleInput` motor path. Cameras and HUD read the simulated state.

`SoldierWeaponCatalogBinding.Initialize(catalog)` configures inactive actors before presentation is spawned. Its Awake remains an offline convenience fallback. Match registration and actual network spawn/despawn must be wired by the chosen networking branch.

## Combat

`SoldierActions.Tick(now)`, `TryFire(now, aim, up)` and `TryReload(now)` accept a simulation clock. Existing offline wrappers use Unity time. `SoldierHitscan.Fire` also accepts world-space aim and inherited velocity without requiring a camera. `PresentFire()` plays cosmetics without resolving damage or consuming ammunition. Gameplay listeners use `ShotFired`; cosmetic listeners use `FireStarted`, so replayed effects do not increment AI burst counts or shot history.

Movement and flight physics remain in FixedUpdate. Camera, UI and animation easing remain frame-driven. The networking integration must choose one simulation owner and avoid running gameplay twice; a transform replicator alone is not a prediction/reconciliation implementation.

## Verification

Use `Tools > Arena Shooter > Validate Prototype Foundation` for catalog/identity checks and the `Game.Foundation.EditModeTests` test assembly for role/health and ammunition timing checks. Keep the existing armory, AI behavior, vehicle garage and entity scaling scenes as focused regression/performance controls.

For framework comparison, use the same sandbox content, entity counts, fixed timestep, visibility settings and network impairment conditions. Record server/client frame times, memory, bandwidth and corrections separately.
