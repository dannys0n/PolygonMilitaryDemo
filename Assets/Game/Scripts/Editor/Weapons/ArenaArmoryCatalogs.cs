using System.Linq;
using UnityEditor;
using UnityEngine;

// Scene-specific lists share the gameplay definitions and equipment prefabs.
public static class ArenaArmoryCatalogs
{
    public const string PolishedPath="Assets/Game/Definitions/Weapons/Catalogs/ArmoryPolishedCatalog.asset";
    public const string ReviewPath="Assets/Game/AnimationTests/UE4RigProof/OriginalProportionsCatalog.asset";
    public static ArenaWeaponCatalog.Entry[] ReviewEntries(WeaponAnimationConversionProof[] proofs)
    {
        var source=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        var converted=proofs.Where(p=>p.originalProportions&&p.source.weapon!=p.converted.weapon&&!source.entries.Any(e=>e.weapon==p.source.weapon&&e.polished)).ToArray();
        return converted.Select(p=>p.converted).Concat(source.entries.Where(e=>!e.polished&&!converted.Any(p=>p.source.weapon==e.weapon)))
            .OrderBy(e=>e.weapon.displayName).ToArray();
    }
    [MenuItem("Tools/Arena Shooter/Refresh armory catalogs")]
    public static void Refresh()
    {
        var source=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        var polished=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>(PolishedPath);
        if(!polished){polished=ScriptableObject.CreateInstance<ArenaWeaponCatalog>();AssetDatabase.CreateAsset(polished,PolishedPath);}
        polished.entries=source.entries.Where(e=>e.polished).ToArray();EditorUtility.SetDirty(polished);
        var proofs=AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{ArenaPolygonWeaponConversionBuilder.Root})
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>).ToArray();
        var review=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>(ReviewPath);
        review.entries=ReviewEntries(proofs);EditorUtility.SetDirty(review);AssetDatabase.SaveAssets();
    }
}
