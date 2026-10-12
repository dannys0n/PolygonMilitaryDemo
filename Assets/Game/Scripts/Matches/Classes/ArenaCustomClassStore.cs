using System;
using System.Linq;
using UnityEngine;

// Only IDs and player choices are saved. Shared weapon/attachment assets stay immutable.
public static class ArenaCustomClassStore
{
    const string Key="Arena.CustomClasses.v1";
    [Serializable] class SaveData { public int version=1; public Record[] classes=new Record[5]; }
    [Serializable] public class Record
    {
        public string name,primary,secondary,primarySight,secondarySight;
        public string[] primaryAttachments=Array.Empty<string>(),secondaryAttachments=Array.Empty<string>();
        public int grenade;
    }
    static SaveData Read(){try{var data=JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key,""));if(data!=null&&data.version==1&&data.classes!=null&&data.classes.Length==5)return data;}catch(ArgumentException){}return new SaveData();}
    public static Record[] LoadRecords()=>Read().classes;
    public static void SaveRecord(int index,Record record)
    {
        if(index<0||index>=5)throw new ArgumentOutOfRangeException(nameof(index));
        var data=Read();data.classes[index]=record;PlayerPrefs.SetString(Key,JsonUtility.ToJson(data));PlayerPrefs.Save();
    }
    public static ArenaClassDefinition[] Load(ArenaWeaponCatalog catalog)
    {
        var data=Read();var choices=new ArenaClassDefinition[5];
        for(int i=0;i<choices.Length;i++)
        {
            var r=data.classes[i];var d=ScriptableObject.CreateInstance<ArenaClassDefinition>();d.isCustom=true;d.customIndex=i;d.id="custom-"+i;d.displayName=r!=null&&!string.IsNullOrWhiteSpace(r.name)?r.name:"CUSTOM CLASS "+(i+1);
            d.primary=r==null?null:catalog.FindWeapon(r.primary);d.secondary=r==null?null:catalog.FindWeapon(r.secondary);
            if(!d.primary||!IsPrimary(d.primary))d.primary=catalog.entries.First(e=>IsPrimary(e.weapon)).weapon;
            if(!d.secondary||IsPrimary(d.secondary))d.secondary=catalog.entries.First(e=>e.weapon.category==SoldierWeaponDefinition.WeaponCategory.Pistol).weapon;
            var primary=catalog.Find(d.primary);var secondary=catalog.Find(d.secondary);
            WeaponSightDefinition Sight(ArenaWeaponCatalog.Entry entry,string id)=>Array.Find(entry.sights,s=>s&&s.id==id);
            WeaponAttachmentDefinition[] Attachments(ArenaWeaponCatalog.Entry entry,string[] ids)=>entry.attachments.Where(a=>a&&ids!=null&&ids.Contains(a.id)).GroupBy(a=>a.mount).Select(g=>g.First()).ToArray();
            if(r==null){d.primarySight=primary.defaultSight;d.secondarySight=secondary.defaultSight;}
            if(r!=null){d.primarySight=Sight(primary,r.primarySight);d.secondarySight=Sight(secondary,r.secondarySight);d.primaryAttachments=Attachments(primary,r.primaryAttachments);d.secondaryAttachments=Attachments(secondary,r.secondaryAttachments);d.grenadeIndex=Mathf.Clamp(r.grenade,0,2);}
            choices[i]=d;
        }
        return choices;
    }
    public static bool IsPrimary(SoldierWeaponDefinition weapon)=>weapon.category!=SoldierWeaponDefinition.WeaponCategory.Pistol&&weapon.category!=SoldierWeaponDefinition.WeaponCategory.Launcher&&weapon.category!=SoldierWeaponDefinition.WeaponCategory.Melee&&weapon.category!=SoldierWeaponDefinition.WeaponCategory.Explosive;
    public static void Save(ArenaClassDefinition choice)
    {
        var data=Read();data.classes[choice.customIndex]=new Record{name=choice.displayName,primary=choice.primary.catalogId,secondary=choice.secondary.catalogId,primarySight=choice.primarySight?choice.primarySight.id:null,secondarySight=choice.secondarySight?choice.secondarySight.id:null,primaryAttachments=choice.primaryAttachments.Select(a=>a.id).ToArray(),secondaryAttachments=choice.secondaryAttachments.Select(a=>a.id).ToArray(),grenade=choice.grenadeIndex};PlayerPrefs.SetString(Key,JsonUtility.ToJson(data));PlayerPrefs.Save();
    }
}
