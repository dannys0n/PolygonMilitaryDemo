# Vehicle prototype

Open `Assets/Game/ArenaShooter/Scenes/Vehicle_Test.unity` and enter Play Mode. Restart Play Mode after changing scripts or prefab settings. This is an open 6v6 proving ground with one local player, eleven bots, two Hummers and two helicopters. The local player starts with the first default arena class. Existing arena maps and ordinary player prefabs are unchanged.

## Controls

- Walk near a door and press **E** to enter. **F1-F5** selects the corresponding seat (**F1-F2** in the helicopter). Hold **E** to exit, including while moving or airborne; collision checks prevent exiting inside solid geometry. AI still dismounts only at safe speed/on the ground. Escape releases the mouse; click to capture it again.
- Hummer driver: **W/S** forward/reverse, **A/D** steer, **S** brakes then reverses, **Z** applies the rear-wheel handbrake, **Left Shift** boosts, **H** gives horn feedback (audio remains unimplemented). Mouse looks around; **C** toggles chase/cockpit views.
- Helicopter pilot: **WASD** supplies directional movement, **Space** climbs, **Left Ctrl** descends. Mouse steers the view and the aircraft heading/nose; **V** toggles independent freelook, **C** switches chase/cockpit view. **Left mouse** fires alternating unguided rockets; **right mouse** zooms. This local-control mode follows Project 3 HelicopterAttack (bounded input, view-driven heading, movement banking and damped translation), implemented through forces rather than direct transform rotation. AI retains the attitude/lift motor commands.
- Gunner: mouse aims the turret; hold **left mouse** to fire. Right mouse zooms, **C** switches view, **V** enables freelook without moving the turret. Weapons and effects are silent.

The Hummer has driver, roof gunner and three passenger seats. The helicopter has pilot and gunner seats. Bodies and carried weapons are hidden while mounted; seated character/hand animations are deliberately unfinished.

## Assets and tuning

Gameplay vehicles are `Assets/Game/Vehicles/Prefabs/Hummer.prefab` and `Helicopter.prefab`. They reference the original ithappy meshes/materials without modifying the imported pack. Muzzle points are measured from the machine-gun geometry. Vehicles, rockets and impact particles use URP materials.

- **Vehicle**: health, seats, door exit points, camera target and motor reference. Seat reservations prevent two bots claiming the same seat. Occupants from opposing factions cannot share an occupied vehicle.
- **HummerMotor**: torque, brakes, maximum speed, steering falloff, anti-roll and center of mass. Each wheel has a separate WheelCollider. Visual wheel positions, steering and spin come from `GetWorldPose`, rather than a cosmetic speed approximation. Tune suspension and tire friction on the WheelCollider objects.
- **HelicopterMotor**: climb speed, tilt limits, yaw rate, attitude response, rotor RPM and spool time. Both rotors spin independently. Lift and torque act on a dynamic Rigidbody; tilt produces horizontal travel. This is an assisted game flight model, not a rotorcraft simulator: blade aerodynamics, ground effect and autorotation are not modeled.
- **VehicleTurret / VehicleLauncher**: weapon cadence, damage, range, turn limits and projectile settings. Gunner damage is attributed to the occupant and uses arena friendly-fire rules. The helicopter's rockets are simple physical projectiles with cover-checked blast damage.
- **VehicleSpawnPoint**: one current vehicle per pad. Defaults: 15-second replacement after destruction, 7-second wreck cleanup, 90-second reset for an abandoned vehicle away from its pad. Occupied spawn pads delay replacement. A lost vehicle below the map also resets.

Use `Player_Polygon_Vehicles.prefab` for a local actor and `Player_Polygon_VehicleAI.prefab` for a bot, under `Game/ArenaShooter/Prefabs/Vehicles`. These are variants of the existing player and AI prefabs. VehicleOccupant suspends on-foot controls, renderers, hit colliders, animators and rig constraints; exits restore their previous states. Death and respawn release seat ownership.

## AI and navigation

ArenaVehicleAI supplies commands to the same motors used by the local player. It considers a ride only when it saves time over walking, including walking to the vehicle and from the drop-off, boarding overhead, and helicopter takeoff/landing overhead. Bots prioritize current combat over boarding, reserve a reachable seat, and allow a brief crew boarding window before departure.

Hummers follow a NavMesh path built for the **Hummer** agent type (1.55 m radius), slow for corners, brake for obstacles, and attempt short reverse recovery before abandoning a stuck trip. Infantry queries explicitly use their own agent type. No NavMeshAgent moves a dynamic vehicle Rigidbody. Parked vehicles carve obstacles; occupied/moving vehicles stop carving.

Helicopters climb before translating, use obstacle probes, and land at authored open landing zones. Landing selection excludes occupied zones and other inbound helicopters. Drivers dismount at arrival, nearby combat (cars), serious damage, or repeated route failure. Passengers leave when a landed vehicle has no driver or is critically damaged. These are prototype policies inspired by combined-arms play, not a reproduction of Battlefield's proprietary behavior.

For another arena map: add ArenaVehicleScenario to the match, add VehicleSpawnPoint objects, provide open landing zones, and bake an additional NavMeshSurface for the Hummer agent. Keep infantry and vehicle baking limited to static ground/obstacle layers. Use the vehicle-ready actor variants. Ordinary arena scenes do not enable the vehicle policy.

## Verification and remaining work

Verified in Play Mode: wheel-driven acceleration/braking, roof-gun damage, helicopter takeoff/landing, the initial safe-exit policy (local players can now bail out), exclusive seat reservation, control restoration, vehicle destruction/replacement, and autonomous vehicle trips/dismounts. The main project TODO records additional checks and polish.

Remaining: a full human playthrough, tuning on the actual arena map, seated characters and hand grips, vehicle-specific death/entry animations, rotor blur, richer destruction effects, cockpit interior polish/vehicle spectator views, parachutes and fall damage, and combat-aware air attack maneuvers. There is no audio or networking implementation in this prototype.

## Implementation references

- [Unity: create a car with WheelColliders](https://docs.unity3d.com/6000.0/Documentation/Manual/WheelColliderTutorial.html) — Rigidbody chassis, wheel contact/suspension, steering/torque and visual wheel poses.
- [Unity: WheelCollider suspension](https://docs.unity3d.com/6000.0/Documentation/Manual/wheel-colliders-suspension.html) — spring/damper and suspension travel configuration.
- [Craig Reynolds, Steering Behaviors for Autonomous Characters, GDC 1999](https://www.red3d.com/cwr/steer/gdc99/) — separating action selection, steering and locomotion, with arrival/path following/obstacle avoidance.
- [FAA Helicopter Flying Handbook, flight controls](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/helicopter_flying_handbook/hfh_ch03.pdf) — distinction between cyclic tilt, collective lift, yaw control and governed rotor speed. These concepts inform the assisted controls; this prototype simplifies their physical mechanisms.

## Controller and HUD pass
The local controls were compared with Project 3: HelicopterAttack source and MVC configured pedal/steering/camera inputs. MVC's core is shipped as DLLs, so its framework was not copied. Car controls retain WheelColliders, smooth steering, automatic brake-to-reverse, rear-wheel handbrake and boost. The HUD uses the supplied Battlefield references: numbered role/occupancy diagrams, matching crew rows, hull segments, context hotkeys, heading, artificial horizon, AGL altitude/speed/vertical speed and actual rocket cooldown. Unimplemented countermeasures are not shown as ready.
Battlefield interaction reference: https://help.ea.com/en/articles/battlefield/battlefield-6/vehicles-guide/

## Responsive Humvee handling
The trial BMW/RWD gearing profile was removed after playtesting. The current Humvee uses strong four-wheel drive, fast steering, engine braking, grippier tires and grounded Rigidbody yaw assistance. The assistance is limited to ground contact and follows wheel angle/speed; airborne vehicle rotation is left to physics. Handling values live on HummerMotor in the Hummer prefab.

Seat assignment follows the serialized seat array order: driver/pilot first, then gunner, then passengers. Interaction uses the closest vehicle entry point but does not choose a seat by door location. Human entry takes precedence over AI seat reservations. VehicleOccupant records the actual entry point, vehicle-local approach position/rotation, and exit position for future entry/exit animations. AI walks to a nearby entry point independently of its ordered seat assignment.
