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

Turret shots have infinite ammunition and retain their fire cadence. Muzzle flashes are emitted after the final turret pose, and locally controlled gunners eject the existing rifle casings from the model's ShellEjection transform. The socket's blue Z axis is the ejection direction. Casings inherit vehicle velocity at the port, including angular motion while turning. AI emits the flash but no casing, and all effects remain silent.

Vehicle presentation pass: turret flash renderers use the world layer rather than the hidden FPS-arms layer. Receiver casing ports are attached to the gun model and casings use size-aware launch clearance. The vehicle equipment panel duplicates infantry HUD borders, typography and tile styling, with white silhouette icons rendered from the actual minigun and rocket assets. Car driver controls have separate anchored columns to prevent the displaced control text from overlapping crew/equipment.

## Assisted physical helicopter flight (current)

The template's direct velocity steering and cosmetic-only banking have been replaced. The helicopter's Rigidbody, collision geometry, visible fuselage and weapon sockets now share the same orientation. Actual rotor-axis thrust produces travel when tilted; forward/lateral fuselage drag preserves momentum while resisting excessive speed. A bounded proportional/damping attitude controller stabilizes roll and tracks pilot pitch/yaw requests. Neutral collective assists zero climb rate and compensates the vertical component lost when banking. Ground contact is checked at the three landing-gear spheres.

ASSIST ON uses camera-relative movement commands and banks the whole airframe to accelerate or brake. Releasing movement captures a hover position; hover takes priority over nose pitch, while heading follows the desired aim direction. The camera remains independent. ASSIST OFF uses body-relative angular-rate commands and manual collective. Both modes apply rotor-axis thrust and Rigidbody torque; there is no separate sideways thrust cancellation. This is a simplified game flight model, without detailed rotor aerodynamics.

Tune HelicopterMotor on the helicopter prefab: Max Tilt, Yaw Rate, Attitude Spring/Damping and Max Angular Acceleration control assistance. Manual Max Angular Acceleration is separate and retains the original manual torque limit. Movement Velocity Response controls assisted acceleration demand; Position Hold Spring/Damping controls hover braking. Flight feel still needs the user's Play Mode walkthrough.

Primary implementation references:
- Microsoft Flight Simulator 2024 SDK, flight_model.cfg, HELICOPTER and FUSELAGE_AERODYNAMICS: https://docs.flightsimulator.com/msfs2024/retail/content-configuration/cfg-files/flight_model.cfg/ — documented helicopter stability assistance and axis-specific damping/drag.
- Bohemia Interactive, Take On Patches – Cougar: https://www.bohemia.net/en/blog/take-on-patches-cougar — developer account of the game's RotorLib flight-model work on aerodynamics, power and torque.
- FAA Helicopter Flying Handbook, flight controls: https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/helicopter_flying_handbook/hfh_ch03.pdf — cyclic tilt, collective and yaw-control fundamentals.
Dual vehicle aim markers: white indicates the requested direction/target; orange diamond indicates the actual turret ray or rocket launch trajectory (including vehicle velocity). They overlap when aligned and reveal turret/body response lag or muzzle parallax when apart. Helicopter pilot T toggles ASSIST, default ON. OFF uses rate controls and collective thrust without auto-leveling or climb-rate compensation; AI continues using assistance. The HUD displays the toggle and state.

ASSIST ON keeps the mouse-selected camera orientation independent of the aircraft. Rotor mounts remain fixed, with only blade spin animated. Requested and actual weapon aim remain separate so banking or hover can visibly prevent alignment.

Mouse input uses infantry sensitivity in assisted mode. On release, assistance brakes by banking against velocity and adjusting collective; it never directly changes velocity or cancels horizontal rotor thrust. Switching modes resets assistance targets and heading without changing Rigidbody momentum.

### Assisted flight braking refinement

Hover first arrests horizontal and vertical travel, then captures the resulting stop position/altitude. It no longer pulls the vehicle back to where controls were released. Cyclic acceleration requests ease over 0.10 seconds; attitude control requests bounded angular rates with damping and the existing strong torque limit. Bank angle and heading-rate limits remain 24 degrees and 65 degrees/second. Manual torque remains separate. No direct velocity assignments or sideways thrust cancellation are used.

The control roles follow FAA guidance: cyclic changes horizontal movement, collective coordinates lift, and stability augmentation damps rotation. The gains and timing are gameplay tuning, not aircraft specifications. References: [FAA basic flight maneuvers](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/helicopter_flying_handbook/hfh_ch09.pdf) and [FAA flight control systems](https://www.faa.gov/air_traffic/publications/aim_html/chap10_section_1.html).

Before the latest responsiveness tuning, a simplified planar braking check at 20 m/s reduced the maximum reverse speed from approximately 8.38 m/s to 0.09 m/s. This checks controller behavior, not full Unity physics, collisions or visual flight feel; Play Mode validation remains necessary.
### Arcade resistance profile

The shared flight model now uses Forward Drag 0.025, Sideways Drag 0.05, Quadratic Drag 0.0015, Rigidbody Linear Damping 0.015 and Angular Damping 0.08. These reduce resistance to actual rotor thrust and turning torque in both assisted and manual flight. At level attitude and 20 m/s, estimated forward resistance falls from 3.8 to 1.4 m/s² and sideways resistance from 6.0 to 1.9 m/s². Tilt/rate limits, gravity and rotor-thrust behavior are unchanged. Hover stopping uses the assisted braking controller instead of heavy passive drag. Manual flight consequently retains more momentum when controls release.