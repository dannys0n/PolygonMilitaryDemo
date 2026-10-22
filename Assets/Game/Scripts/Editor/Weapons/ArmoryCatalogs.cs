using System.Linq;
using UnityEditor;
using UnityEngine;

// Scene-specific lists share the gameplay definitions and equipment prefabs.
public static class ArmoryCatalogs
{
    public const string ReviewPath="Assets/Game/AnimationTests/UE4RigProof/OriginalProportionsCatalog.asset";
    public static WeaponCatalog.Entry[] ReviewEntries(WeaponAnimationConversionProof[] proofs)
    {
        var source=AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        var fitted=proofs.Where(p=>p.originalProportions&&p.source.weapon!=p.converted.weapon).ToArray();
        var converted=fitted.Where(p=>!source.entries.Any(e=>e.polished&&(e.weapon==p.source.weapon||e.weapon==p.converted.weapon))).ToArray();
        return converted.Select(p=>p.converted).Concat(source.entries.Where(e=>!e.polished&&!fitted.Any(p=>p.source.weapon==e.weapon)))
            .OrderBy(e=>e.weapon.displayName).ToArray();
    }
    [MenuItem("Tools/Game/Refresh animation review catalog")]
    public static void Refresh()
    {
        var proofs=AssetDatabase.FindAssets("t:WeaponAnimationConversionProof",new[]{PolygonWeaponConversionBuilder.Root})
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>).ToArray();
        var review=AssetDatabase.LoadAssetAtPath<WeaponCatalog>(ReviewPath);
        review.entries=ReviewEntries(proofs);EditorUtility.SetDirty(review);AssetDatabase.SaveAssets();
    }
}
