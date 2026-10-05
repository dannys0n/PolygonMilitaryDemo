# First-person visual prototype

Visual meshes, textures, materials, and rifle animations copied from the locally installed Infima Games Low Poly Shooter Pack Free Sample in AtlasNetUnityShooterDemo. The matching generic skeletons are preserved. Copied prefabs contain visual components only; networking, inventory, input, cameras, audio, and gameplay scripts were omitted. Copied materials use URP Lit. Imported animation events were removed because gameplay remains in the soldier scripts.

Player_Soldier_blk_m contains this setup. V switches first/third person, left click fires, right mouse aims down sights, and R reloads through the existing SoldierActions gates. SoldierFirstPersonView.ViewOffset adjusts arms placement. The full first-person reload uses the matching Reload Empty arms/rifle clips, including charging. Firing and repeat reloads wait until both first- and third-person reloads finish. Idle and firing blend between hip and ADS clips; ADS releases during reload. Aim Blend Speed, Aim Field Of View, and Aim Offset are configurable on SoldierFirstPersonView. Walking sway, hit recoil, and wall clipping are later polish.

These assets retain their original third-party license; copying them does not grant redistribution rights.

First person motion: the demo's additive hip/aim idle and walking clips play on the Locomotion layer. Aim In/Out clips play on the Aim Transitions layer. Controller velocity blends idle/walk; reload suppresses these layers. SoldierFirstPersonView exposes Locomotion Weight, Aim Transition Weight, Motion Blend Time, and Walking Speed for tuning.
