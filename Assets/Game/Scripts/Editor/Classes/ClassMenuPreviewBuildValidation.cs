using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Validate before Addressables' build step; refreshing is an explicit authoring action.
public class ClassMenuPreviewBuildValidation : IPreprocessBuildWithReport
{
    public int callbackOrder=>-10000;
    public void OnPreprocessBuild(BuildReport report)=>Validate();
    public static void Validate()
    {
        var source=AssetDatabase.LoadAssetAtPath<ClassMenuSources>("Assets/Game/Definitions/Editor/ClassMenuSources.asset");
        var menu=AssetDatabase.LoadAssetAtPath<ArenaClassMenuCatalog>("Assets/Game/Definitions/UI/ClassMenuCatalog.asset");
        if(!source||!source.catalog||!menu)throw new BuildFailedException("Class menu authoring assets are missing. Run Tools > Game > Refresh Class Menu Previews.");
        var expected=source.catalog.entries.Select(e=>e.weapon.catalogId).OrderBy(id=>id);
        if(!expected.SequenceEqual(menu.weapons.Select(w=>w.id).OrderBy(id=>id)))throw new BuildFailedException("Class menu weapons are out of date. Run Tools > Game > Refresh Class Menu Previews.");
        foreach(var entry in source.catalog.entries)
        {
            var actual=menu.FindWeapon(entry.weapon.catalogId);var weapon=entry.weapon;
            if(actual.name!=weapon.displayName||actual.category!=weapon.category||actual.damage!=weapon.damage||actual.rpm!=weapon.roundsPerMinute||actual.rounds!=weapon.magazineCapacity
                ||(actual.defaultSight??string.Empty)!=(entry.defaultSight?entry.defaultSight.id:string.Empty)
                ||!actual.sights.OrderBy(id=>id).SequenceEqual(entry.sights.Where(s=>s).Select(s=>s.id).OrderBy(id=>id))
                ||!actual.attachments.OrderBy(id=>id).SequenceEqual(entry.attachments.Where(a=>a).Select(a=>a.id).OrderBy(id=>id)))
                throw new BuildFailedException("Class menu metadata is out of date for "+weapon.displayName+". Run Tools > Game > Refresh Class Menu Previews.");
        }
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var group=settings?settings.FindGroup("Class Preview Models"):null;
        var addresses=menu.weapons.Select(w=>w.previewAddress).Concat(menu.parts.Select(p=>p.previewAddress)).Where(a=>!string.IsNullOrEmpty(a)).Distinct();
        foreach(var address in addresses)
        {
            var entry=group?group.entries.FirstOrDefault(e=>e.address==address):null;
            if(entry==null||string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.guid)))throw new BuildFailedException("Missing class preview "+address+". Run Tools > Game > Refresh Class Menu Previews.");
        }
    }
}
