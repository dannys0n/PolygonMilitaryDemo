# Shared weapon animation pilot: Polygon M16A4

The Polygon M16A4 is the first migrated weapon. Other weapon controllers are unchanged.

- FPS and TPS both nest `Assets/Game/Prefabs/Weapons/Models/M16A4_Model.prefab`. Change geometry, internal scale, pivots, and sockets there. View prefabs provide camera/body mounting only.
- FPS idle, fire, and tactical arm motion reference the existing FPS Pack M16A4 clips directly. Tactical playback defaults to its source length, 2.666667 seconds.
- The accepted Unity mannequin empty reload remains a distinct, adapted arm clip. Its fitted fingers/contact and motion are preserved at 2.85 seconds. The FPS weapon frame uses the original polished Polygon `MK18_FirstPerson_Reload_GripReturn` motion, rebased once onto the accepted UE4 hip frame. Apply the same rigid frame change to both hands; do not reconstruct this frame from mannequin world-space wrist motion or scale its displacement by forearm length. Verify the entire path against this source, including the latter half, rather than checking only grip contact and endpoints.
- Custom full reloads must key the neutral `ViewMotion` and `ViewMotion/WeaponMotion` parents on the base layer. The shared graph uses explicit bindings with Write Defaults disabled; leaving these parents unbound lets additive sway accumulate instead of applying around a fixed base. Check runtime playback against the source path, not just sampled clip endpoints.
- Small model-specific mechanism clips live in `Animations/Weapons/Reloads/M16A4/Mechanisms`. A synchronized layer on the existing FPS Animator evaluates those clips; it does not add another Animator or a runtime retarget solver. The base layer owns its clock.
- TPS reuses the existing FPS Pack M16A4 idle and tactical body/weapon-control clips. Only the legacy empty reload has distinct retargeted TPS clips. TPS retains locomotion, aiming, and grip IK.
- The definition has independent idle/fire playback speeds and a playback speed in each reload row. One means original timing. Reload duration is the source duration divided by playback speed; the gameplay ammo clock uses that same duration. Older reload assets with zero stored speed retain their original timing.
- The TPS view uses its existing mount scale and a grip-centered mount offset to compensate for the body rig scale. The shared model has the same world-space size in both views; no per-frame scale correction is added.

`WeaponMotionLayerBuilder` authors the reusable graph and speed parameters. Compatible future weapons can reference existing arm actions independently, select legacy adapted actions for individual states, and supply only their mechanism clips. Do not rebake shared arm curves solely to change playback speed or magazine hierarchy.

The removed tactical combined clip was a migration artifact. Do not regenerate it: the tactical controller now references the shared source directly. Serialized file sizes are not a measurement of runtime memory or FPS gains.


