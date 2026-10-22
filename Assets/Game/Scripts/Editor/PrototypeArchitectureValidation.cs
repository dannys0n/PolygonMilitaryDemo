using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Read-only checks for the production data shared by offline and future network branches.
public static class PrototypeArchitectureValidation
{
    [MenuItem("Tools/Arena Shooter/Validate Prototype Foundation")]
    public static void Validate()
    {
        var errors=Inspect();
        if(errors.Length==0)Debug.Log("Prototype foundation: catalogs and character identities passed.");
        else foreach(var error in errors)Debug.LogError(error);
    }
    public static string[] Inspect()
    {
        var errors=new List<string>();
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaWeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        if(!catalog)return new[]{"Missing production weapon catalog"};
        foreach(var group in catalog.Weapons().GroupBy(e=>e.weapon.catalogId))
            if(string.IsNullOrWhiteSpace(group.Key)||group.Count()!=1)errors.Add("Invalid/duplicate weapon ID: "+group.Key);
        foreach(var entry in catalog.Weapons(true))
        {
            if(!entry.firstPersonPrefab||!entry.thirdPersonPrefab){errors.Add("Missing polished views: "+entry.weapon.displayName);continue;}
            var sights=entry.thirdPersonPrefab.GetComponent<SoldierWeaponSights>();
            if(!sights||!entry.defaultSight||!sights.Supports(entry.defaultSight))errors.Add("Unsupported default optic: "+entry.weapon.displayName);
        }
        foreach(var path in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Game/Prefabs/Players","Assets/Game/Prefabs/Matches"}).Select(AssetDatabase.GUIDToAssetPath))
            foreach(var controller in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SoldierController>(true))
                if(controller.IsAIControlled&&controller.isLocalPlayer)errors.Add("AI marked as local player: "+path);
        foreach(var scene in EditorBuildSettings.scenes)
            if(scene.enabled&&!AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path))errors.Add("Missing build scene: "+scene.path);
        return errors.ToArray();
    }
}
