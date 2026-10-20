# Animation review variants

Open the regular `Armory` or `Armory_AnimationTests` scene. Shoot a display to equip, RMB aims, R reloads, V changes perspective, G throws a grenade. Production match catalogs and existing weapon definitions remain unchanged.

`(UE4 Alt: ...)` identifies the 22 alternatives using existing Polygon weapons and the existing Polygon arm skeleton. Imported UE4 motion is baked offline to that skeleton, the existing weapon-motion transform, and matching magazine/bolt tracks. The regular infantry Animator graph, grip IK, aim placement, attachments, inventory and action flow remain in use. The variants also have baked TPS reload/weapon-motion and grenade clips. Their original equip/unequip and ordinary recoil remain available through the shared graph. Hand/finger offsets are calibrated from each equivalent weapon's current grip pose; bespoke reloads on different gun geometry remain review candidates, not polished random-loadout entries.

`(UE4)` keeps the original source arm/weapon pairing for comparison. `(Source)` identifies the three additional downloaded shotgun, Benelli M4 and revolver rigs. They keep their original paired arms; they are not Polygon-arm alternatives. Their source FBXs and textures remain under `Assets/Imported/FPSAnimationTests`. The Lab's original converted assets remain under `Assets/FPSAnimationPack`.

Source idle weapon tracks are frozen at their reference frame, rather than replaying the animation used to extract that pose. Source equip/unequip play on the base layer so an empty override layer cannot retain the previous switch pose. The Polygon alternatives use the established game controllers and their original switch layers.

Rebuild in Edit Mode using **Tools > Arena Shooter > Animation Tests > Rebuild test weapon variants**. The full command rebuilds UE4 source actions, source variants, Polygon alternatives, and armory references. **Bake Polygon alternatives** only updates the Polygon set. Generated clips are stored in Git LFS. No retargeting or clip sampling runs during gameplay.

Default sights/socket calibration on imported source guns still needs per-weapon polish. Polygon alternatives retain the existing equivalent weapon's sights, socket positions and compatible attachments. These test entries stay out of the production weapon catalog.

Armory supply is unlimited reserve, with the real reload action on an empty magazine. It no longer silently refills a single-shot weapon and bypasses its animation.
