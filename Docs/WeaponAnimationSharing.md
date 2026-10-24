# Shared weapon animation pilot: Polygon M16A4

The Polygon M16A4 established this sharing layout. The six compatible conversions below use the same player-owned rig and graphs; unrelated weapons retain their existing controllers.

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


## Compatible Polygon conversions

P90, Colt Python, MP5K-PDW, KRISS Vector CRB, M1911, and Glock 17 now use this layout. Their definitions and catalog identities remain the same, so both armories and sandbox resolve the updated equipment without a parallel weapon catalog.

| Polygon model | Shared reference actions | Fitted actions retained |
| --- | --- | --- |
| P90 | FPS Pack P90 idle/fire/full/tactical | Model-specific mechanism tracks |
| Colt Python | FPS Pack Viper idle/fire | Cylinder-contact reload; full/tactical reference one action |
| MP5K-PDW | FPS Pack MP5 idle/fire/full/tactical base clips | Sparse left-arm contact overlay and fitted TPS reloads for the shorter receiver |
| KRISS Vector CRB | FPS Pack Vector idle/fire/full/tactical | Model-specific mechanism tracks |
| M1911 | FPS Pack M1911 idle/fire/tactical | Original legacy full reload, fitted to the new idle |
| Glock 17 | FPS Pack Glock 18 idle/fire/full/tactical | Model-specific mechanism tracks |

Compatible actions reference the same source clip object, rather than baking another set of arm curves. A fitted reload remains distinct when its magazine, cylinder, support grip, or finger contact differs. Similar-looking motions alone are not sufficient to share a baked hand trajectory. M1911 alone retains its adapted legacy full reload; Glock 17 now shares the native Glock 18 full reload.

Each pair of equipped views nests one model from `Prefabs/Weapons/Models`. A static `Fit` frame, where present, seats/scales the geometry and all its sockets without altering the shared arm skeleton or adding a runtime solver. FPS and TPS compensate for the scaled character body to keep the same physical weapon size. ADS uses the existing sight solver, recalibrated against the new hip frame.

Full/tactical actions and model-specific mechanisms use the shared graph clock and existing per-weapon playback-rate fields. Keep mechanism paths relative to the actual nested model, including `Model/Fit` when present. In the M1911 mesh, `Bone.002` carries magazine/ammunition and `Bone.004` carries the slide; the separate `holder` renderer is not a magazine and must remain on its original receiver bone.

Custom action bakes key neutral motion parents but must not key the Animator object's own Transform: its mounting position belongs to the player. Verify the entire action in actual Animator playback; endpoint-only SampleAnimation checks do not reveal additive-parent accumulation or player-root overrides. The runtime verification covers hip, ADS, firing, full/tactical reload, finger recovery, TPS grip targets, and equal model size.

### Grip fitting and pistol references

Use the actual trigger contact surface as the firing-hand reference. Whole-receiver or magazine bounding boxes are useful for initial scale, but their centers are not grip registration points. In particular, the native M1911 trigger bone includes guard geometry; its small trigger surface is a separate connected mesh component. P90 and MP5K geometry is fitted inside the shared model's static `Fit` frame, leaving the reference hand clips unchanged.

M1911 and Glock 17 tactical FPS clips are the original FPS Pack clip assets. Their tactical TPS controllers also reference the corresponding native body/hand actions directly; only the mechanism tracks differ. Keep M1911's adapted legacy full reload separate. The M1911 tactical source contains a short hand hold while the magazine is out (approximately one quarter to one third through the action). Live traces confirm its action clock continues through that hold in both native and Polygon playback; do not add a runtime hand solver or alter the source timing to conceal it.


### Shared reference reloads and hip placement

P90 and Glock 17 now reference the native full and tactical FPS arm clips directly, along with the native TPS body actions. Weapon mechanisms are sampled in the receiver frame: apply the source bone delta in gun space to the fitted target rest. Do not apply local-axis translation without accounting for the parent scale and receiver frame. P90 magazine thickness/width are fitted separately from the complete weapon scale.

MP5K uses the same native MP5 base clips, with only three left-arm joint tracks added to its existing mechanism layer where its shorter receiver requires different magazine/charging-handle contact. The firing arm and finger source data remain shared. Its TPS reloads contain the corresponding fitted support-arm motion. Keep its magazine seated in its own magwell; copying the MP5 magazine position directly would float it outside the receiver.

Weapons adopting a reference hip pose also adopt its presentation offsets. M1911 and Glock 17 had retained a legacy (0.09, -0.03, 0.15) metre hip offset on top of the native pose; both now use the reference zero offset. Check firing-hand distance from the actual camera in live comparison, not just local skeleton poses. Weapon-specific ADS calibration and playback-rate fields remain independent.

### Canonical placement and MP5K support variant

All polished catalog weapons using the shared FPS rig have zero `viewOffset`, `hipFireOffset`, `aimOffset`, `viewRotationOffset`, `viewRotationPivot`, `hipFireRotationOffset`, and `aimRotationOffset`. These legacy presentation corrections must not be layered over authored poses. Sight transforms and eye distances remain the ADS calibration; TPS placement fields remain separate.

MP5K's dedicated support-arm variant brings its resting left hand 0.04 m back from the previous fitted pose. The same pose is baked into firing and reload entry/exit, with contact weighting preserving magazine and charging-handle movement during reload. Only `upperarm_l`, `lowerarm_l`, and `hand_l` are overridden in the existing FPS mechanism layer; the right arm, fingers, and base action clips stay shared with MP5. Its TPS idle/hold and reload variants are produced with `TpsAnimationAdapter.BuildActions` / `BuildReload`, using the same offline conversion path. No gameplay-time hand solver is added.


### Magazine-contact refinement

MP5K reload contact uses matching frames of the native MP5 full and tactical actions. Fit both magazine and charging-handle contact to each part's geometry; do not use a differently timed legacy full reload as a per-frame hand target. Finger proximity blends these contacts against the accepted MP5K foregrip offset. When the source hand returns to the foregrip, settle onto the exact accepted idle wrist pose and hold it through the remaining reload frames. Matching only the final key causes a visible grip correction during the reload-to-idle transition. Keep these corrections in the existing three-joint reload overlay; idle/fire overlays, the reference firing arm, source timing, and finger clips remain unchanged. TPS reloads are rebuilt from the same combined pose. No additional runtime solver is used.

Vector's shared model is registered approximately 0.01695 m lower and 0.05878 m forward to align its receiver/magazine with the native Vector frame. Its full/tactical FPS arm clips and TPS body controllers reference the native assets directly. Only fitted mechanisms remain model-specific; sights travel with the model and TPS ADS references are recalibrated.


### Second shared-reference batch

USP .45 Tactical and Beretta M9A3 share the M1911 FPS Pack idle, firing, and tactical arm clips; each retains its fitted legacy full reload. SCAR-H, HK416, and MK18 share the M16A4 FPS Pack idle, firing, and tactical clips. SCAR-H additionally references the accepted Polygon M16A4 full reload instead of its former full action. HK416 and MK18 retain fitted full reloads. Tactical TPS body actions reference the native controllers directly; full TPS variants are retained only for differing motion.

Model placement uses the accepted Polygon M1911/M16A4 geometry as the broad registration reference. Static model Fit carriers move sockets and geometry together, and FPS/TPS nest the same model prefab. Weapon-specific mechanism tracks retain their own hinge axes and are sampled on the shared action clock. Legacy FPS placement offsets remain zero. Playback duration remains configured per weapon.


### Final SVD and AK batch

Polygon SVD adopts the native SVD FPS idle, firing, full and tactical actions, with shared native TPS body actions and fitted Polygon mechanisms. Polygon AK-12, AK-74 and AKM adopt the native AK-12 idle, firing and tactical actions. Their existing full reload movement is retained and fitted to the new idle grip and finger contacts. FPS/TPS nest the same model prefab, use zero legacy FPS presentation offsets and keep individual playback configuration. The separate AKX200 FPS Pack definition is displayed as AKX200 rather than AK-12 / AKX200; stable catalog identity is retained.


### Geometry registration follow-up

SVD's previous magazine was roughly 7 cm behind its source frame. Its receiver is registered forward to the native SVD and its moving magazine is separately dimension-fitted to the native magazine. Reload mechanisms are resampled on the shared full/tactical action clock, rather than correcting the timing of the hand clips. AK receivers are registered forward against the UE4 frame with static model Fit carriers. FAL adopts G3 idle/fire/tactical reference actions, retaining its fitted legacy full reload. These are model-authoring corrections; no gameplay hand-position solver is introduced.

### Repeatable Polygon AK full-reload conversion

`Tools > Weapons > Rebuild Polygon AK Full Reloads` runs `AKFullReloadConversion` in Edit Mode. It always reads the preserved polished clips in `Animations/Weapons/Scaled/legacy-ak12`; it never rebakes a previous conversion. Tactical actions and fitted magazine/mechanism clips are not modified.

The shared arm root and presentation wrappers retain their idle transforms. The `ik_hand_gun` bone owns weapon motion; `SourceWeapon` retains its fixed local placement. Arm goals are rebased in metres and rigid rotations relative to the weapon, while wrist and finger deltas are registered against each idle pose. The magazine-grasp interval is fitted offline against the actual moving magazine. Its frame is `currentMagazineRotation * inverse(idleMagazineRotation) * idleWeaponRotation`; using the current weapon rotation in the final factor applies the weapon motion twice.

Only skeletal/mount tracks belong to the FPS action; moving mechanisms belong to their synchronized part layer. Rebuild validation rejects overlapping bindings or arm-root drift. Both action boundaries match idle. TPS reloads are generated from the same combined FPS action and retained mechanisms. No gameplay-time retargeting or additional hand solver is introduced. Visual contact still requires review when changing model dimensions or source poses.

Weapon motion uses the source camera-space delta (`sourceCurrent * inverse(sourceIdle)`) applied before the target idle orientation. Hand and elbow deltas use the corresponding change of basis. Reusing the source weapon-local axes directly on the registered UE4 frame turns the authored roll into yaw. The AK rotation audit compares 601 samples per weapon against the source, in addition to checking root stability and boundaries.

Charging-handle contact uses each model's outer charging-tab geometry, transformed with the existing animated charging bone. The right distal index bone is fitted during the pull interval, with smooth entry/release and the original wrist rotation retained. The idle return blends both wrist goals and elbow hints; the last 0.12 seconds hold the exact idle skeletal pose. These constraints are baked by the same repeatable converter and do not modify mechanism tracks.
