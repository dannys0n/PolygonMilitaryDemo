# FPS Animation Pack - converted source art and preview

## Install in another project

In Unity, choose **Assets > Import Package > Custom Package** and select `FPSAnimationPack-Unity-URP.unitypackage`. Import the whole `Assets/FPSAnimationPack` folder. The package preserves asset GUIDs and source names. It does not include the lab's scenes, project settings, Pipeline connection, or installed packages.

Prepared and checked in Unity **6000.6.2f1** with **URP**. The destination project must already have URP installed. Preview materials are simple URP conversions; Unreal's material graphs and gameplay systems are not transferred.

Open **Tools > FPS Animation Pack > Animation Preview**. Choose a weapon/model and action, toggle the weapon and character, and switch between orbit and FPS views. Reloads sort first. Use **All animations**, **Weapon compatible**, **Rig compatible**, or **Both** to control filtering. The reference environment is stationary to make camera animation visible. Cross-rig sampling does not automatically retarget the animation.

The dropdown catalog reads `export-manifest.json`, `action-manifest.json`, `source-layout.json`, and `preview-index.json`. It does not load every FBX to display the list. Only selected playback tracks load, with a small clip cache. Models and the reference environment are reused between action selections. The first selection of a previously unloaded model or clip can still take time.

If source animations are added or reimported, use **Tools > FPS Animation Pack > Rebuild animation index**. This is an explicit offline scan, so it can load assets; ordinary browsing does not. To rebuild the distribution archive, choose **Export import package**. The archive is saved under the project's `Exports` folder.

Original contents: 315 playable FBX clips, one static Kar98K weapon pose, 45 meshes, and 54 textures. The original FBX files and names are unchanged. `native-motion.json` preserves native Unreal translation and rotation samples. The importer restores discrepant channels, including substituted skeleton-reference translations, lost inter-frame rotation detail, and short spurious finger keys. This applies to the imported animation assets, including full-body animation, not only the viewer.

`Actions` adds 176 reconstructed Unreal montage clips and 56 synchronized camera actions. These preserve segment order, trimming, loops, authored playback speed, and magazine/cartridge visibility changes. The selector pairs character and weapon actions using the original preset settings. Raw-sequence previews map partner tracks onto the native segment timeline, preserving authored playback-speed differences. Weapon-only actions hold the preset's reference body pose. Unreal montage transition blending, Animation Blueprints, Control Rig, and procedural recoil/IK are not Unity runtime implementations.

The character selector includes the full UE4 mannequin and full UEFN mannequin alongside the arms. The UEFN source mesh has no assigned Unreal Skeleton; `preview-rigs.json` records its verified UE5 preview compatibility separately, without changing the native manifest. Missing UE5 helper bones do not affect this mesh's weighted bones.

To rebuild the derived actions from the saved source metadata, choose **Rebuild authored actions**. Rebuild the animation index afterward if the original FBX list changed. The action builder uses batched curve updates and compacts only constant channels below sub-micron precision. No runtime player or weapon controller is installed. Retargeting and integration with your game's animation system are separate steps.

Weapon materials are rebuilt from native graph connections and assigned by explicit renderer slot using `material-layout.json`. Textured materials retain their original base maps; untextured finishes retain source colors, metallic, and roughness values. Full mannequins are intended for TPS inspection.
