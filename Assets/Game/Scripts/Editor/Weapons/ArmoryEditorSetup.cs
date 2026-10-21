using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class ArmoryEditorSetup
{
    public static void ConfigureReview(Scene scene,ArenaWeaponCatalog catalog)
        => Configure(scene, AssetDatabase.GetAssetPath(catalog));

    public static void Configure(Scene scene, string catalogPath)
    {
        var roots=scene.GetRootGameObjects();var wall=roots.SelectMany(r=>r.GetComponentsInChildren<ArmoryWall>(true)).Single();
        wall.sourceCatalogPath=catalogPath;
        var armory=roots.SelectMany(r=>r.GetComponentsInChildren<ArmoryController>(true)).Single();
        var loadout=armory.loadout;
        // Review players own their scene setup; inherited catalog overrides pull every rig into memory.
        var instance=PrefabUtility.GetOutermostPrefabInstanceRoot(loadout.gameObject);
        if(instance)PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        // Script-driven aim input must remain outside the animated skeleton.
        // Review players are unpacked, so repair older saved scene instances too.
        if(loadout.presentation.aimTarget.IsChildOf(loadout.presentation.characterAnimator.transform))
            loadout.presentation.aimTarget.SetParent(loadout.transform,true);
        if(loadout.TryGetComponent<SoldierWeaponCatalogBinding>(out var binding))Object.DestroyImmediate(binding);
        var loader=loadout.GetComponent<ArmoryReviewLoadout>();if(!loader)loader=loadout.gameObject.AddComponent<ArmoryReviewLoadout>();
        loader.wall=wall;loader.loadout=loadout;loadout.slots=Array.Empty<SoldierWeaponLoadout.Slot>();loadout.selectedSlot=0;if(PrefabUtility.IsPartOfPrefabInstance(loadout))PrefabUtility.RecordPrefabInstancePropertyModifications(loadout);EditorUtility.SetDirty(loadout);EditorUtility.SetDirty(loader);
        EditorUtility.SetDirty(wall);
        EditorSceneManager.MarkSceneDirty(scene);
    }
}
