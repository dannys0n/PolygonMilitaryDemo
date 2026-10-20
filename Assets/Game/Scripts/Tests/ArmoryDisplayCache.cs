using System;
using System.Linq;
using UnityEngine;

// Lightweight editor-armory index. Paths deliberately avoid loading every animation rig.
public sealed class ArmoryDisplayCache : ScriptableObject
{
    public enum Kind { Weapon, Sight, Attachment }
    [Serializable] public sealed class Item
    {
        public Kind kind;
        public string key, title, assetPath, proofPath, signature, firstPersonPath, thirdPersonPath;
        public string[] sourceFiles=Array.Empty<string>();
        public string[] compatibleWeapons=Array.Empty<string>();
        public GameObject display;
        public bool Supports(string weapon)=>kind==Kind.Weapon||Array.IndexOf(compatibleWeapons,weapon)>=0;
    }
    public string catalogPath, signature, defaultWeapon;
    public string[] sourceFiles=Array.Empty<string>();
    public Item[] items=Array.Empty<Item>();
    public Item Find(string key)=>Array.Find(items,i=>i.key==key);
    public ArenaWeaponCatalog.Entry LoadWeapon(string key)
    {
#if UNITY_EDITOR
        var item=Find(key)??throw new ArgumentException("Unknown armory weapon: "+key);
        if(!string.IsNullOrEmpty(item.firstPersonPath))
        {
            var first=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(item.firstPersonPath).GetComponent<SoldierWeaponView>();
            var third=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(item.thirdPersonPath).GetComponent<SoldierWeaponView>();
            return new ArenaWeaponCatalog.Entry{weapon=UnityEditor.AssetDatabase.LoadAssetAtPath<SoldierWeaponDefinition>(item.assetPath),firstPersonPrefab=first,thirdPersonPrefab=third,basePrefab=third.gameObject};
        }
        if(!string.IsNullOrEmpty(item.proofPath))return UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(item.proofPath).converted;
        throw new InvalidOperationException("This armory entry has no conversion proof: "+item.title);
#else
        throw new InvalidOperationException("Animation review armories use editor-only asset loading.");
#endif
    }
    public void Resolve(ArmoryTarget target)
    {
#if UNITY_EDITOR
        if(target.cachedItem==null)return;
        if(target.cachedItem.kind==Kind.Sight)target.sight=UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponSightDefinition>(target.cachedItem.assetPath);
        if(target.cachedItem.kind==Kind.Attachment)target.attachment=UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(target.cachedItem.assetPath);
#endif
    }
}
