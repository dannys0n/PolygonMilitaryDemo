# Native UE4 skeleton proof

Open `Assets/Game/Scenes/Tests/Weapons/Armory_UE4RigProof.unity`. It starts with **MG5 (UE4 Rig Proof)** and displays all 22 UE4 weapon presets fitted with Polygon arms. Shoot a display to equip it. **R** reloads, **G** throws the selected grenade, **RMB** aims. Original-arm `(UE4)` comparisons remain in the regular armories.

For the complete imported animation library, use **Tools > Game > Animation Tests > Preview Polygon UE4 animations**. This opens the existing FPS Animation Preview with **PolygonArms_UE4** selected. Choose a weapon family/model and an action. The preview retains lazy clip loading, reload-first sorted dropdowns, speed/scrubbing, FPS/orbit views, reference scenery and source camera motion. Shared actions include grenades. Use **Rig compatible** to browse all UE4 character tracks, or **Both** for the selected weapon and shared actions. The preview does not add every raw action to the gameplay state machine.

The proof scales the Polygon arms uniformly to 93.863% based on average shoulder-to-wrist length, independent of T-pose versus A-pose. Remaining joint/hand fitting, finger weights, UVs and triangle winding are mirrored from the left arm. Native animation poses still differ between hands as authored. All weapons and the grenade reuse one fitted mesh/material and their existing source animation controllers and synchronized weapon-part tracks. No animation retarget solver runs during gameplay. The standalone `PolygonArms_UE4.prefab` uses the same mesh and source skeleton for previewing raw clips and montages without generating duplicate animation assets.

This is an FPS compatibility proof, not a full-body migration. Source weapon geometry is retained to preserve authored hand contacts. TPS still uses the source variants' previous placeholder presentation. The Polygon hand has three fingers: its combined fingers follow the UE4 ring finger and cannot reproduce five independent fingers. Skin/contact details remain prototype quality. Other weapon geometry still needs magazine, charging-handle and grip calibration.

The shared grenade grip is fitted to the native right-hand finger pocket and uses the existing grenade inventory, projectile and re-equip flow. All three game grenade types are supported.

All proof entries are **not polished**, appear only in isolated test catalogs, and stay out of gameplay random pools. The production player rig, weapons and source pack models/animations are unchanged. The preview tool accepts consumer-provided character models without altering the pack's export manifests.

Rebuild in Edit Mode with **Tools > Game > Animation Tests > Build all UE4 skeleton proofs**. The full animation-test rebuild also preserves this set once it exists.
