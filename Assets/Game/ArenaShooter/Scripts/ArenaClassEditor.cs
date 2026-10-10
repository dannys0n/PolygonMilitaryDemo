using System;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ArenaClassEditor : MonoBehaviour
{
    public ArenaWeaponCatalog catalog;
    public GameObject panel;
    public TMP_Dropdown customSlot,primary,secondary,primaryOptic,secondaryOptic,grenade;
    public TMP_InputField className;
    public TMP_Dropdown[] primaryAttachments,secondaryAttachments;
    public UnityEngine.UI.Image primaryIcon,secondaryIcon;
    public TMP_Text summary,status;
    public UnityEngine.UI.Button save,back;
    ArenaClassDefinition draft;
    ArenaClassDefinition[] classes;
    SoldierWeaponDefinition[] primaryWeapons,secondaryWeapons;
    WeaponSightDefinition[] primarySights,secondarySights;
    WeaponAttachmentDefinition[][] primaryOptions,secondaryOptions;
    Action<ArenaClassDefinition> saved;
    Action closed;
    bool refreshing;
    public bool IsVisible=>panel.activeSelf;

    public static ArenaClassEditor Open(Action<ArenaClassDefinition> saved=null,Action closed=null,int selected=0)
    {
        var editor=FindAnyObjectByType<ArenaClassEditor>(FindObjectsInactive.Include);
        if(!editor)editor=Instantiate(Resources.Load<GameObject>("ArenaClassEditor")).GetComponent<ArenaClassEditor>();
        editor.saved=saved;editor.closed=closed;editor.Show(selected);return editor;
    }
    void Awake()
    {
        panel.SetActive(false);customSlot.onValueChanged.AddListener(SelectClass);
        primary.onValueChanged.AddListener(_=>ChangeWeapon(true));secondary.onValueChanged.AddListener(_=>ChangeWeapon(false));
        save.onClick.AddListener(Save);back.onClick.AddListener(Close);
    }
    public void Show(int index)
    {
        classes=ArenaCustomClassStore.Load(catalog);primaryWeapons=catalog.entries.Select(e=>e.weapon).Where(ArenaCustomClassStore.IsPrimary).OrderBy(w=>w.category).ThenBy(w=>w.displayName).ToArray();secondaryWeapons=catalog.entries.Select(e=>e.weapon).Where(w=>!ArenaCustomClassStore.IsPrimary(w)).OrderBy(w=>w.category).ThenBy(w=>w.displayName).ToArray();
        refreshing=true;Options(customSlot,classes.Select(c=>c.displayName));Options(primary,primaryWeapons.Select(w=>w.displayName));Options(secondary,secondaryWeapons.Select(w=>w.displayName));Options(grenade,new[]{"Frag","Smoke","Flashbang"});customSlot.SetValueWithoutNotify(Mathf.Clamp(index,0,4));refreshing=false;SelectClass(customSlot.value);panel.SetActive(true);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    static void Options(TMP_Dropdown control,System.Collections.Generic.IEnumerable<string> names){control.ClearOptions();control.AddOptions(names.ToList());control.RefreshShownValue();}
    void SelectClass(int index)
    {
        if(refreshing)return;draft=classes[index];refreshing=true;className.SetTextWithoutNotify(draft.displayName);primary.SetValueWithoutNotify(Array.IndexOf(primaryWeapons,draft.primary));secondary.SetValueWithoutNotify(Array.IndexOf(secondaryWeapons,draft.secondary));grenade.SetValueWithoutNotify(draft.grenadeIndex);refreshing=false;RefreshWeapon(true);RefreshWeapon(false);status.text="CHOOSE YOUR LOADOUT. CHANGES APPLY ON SPAWN.";
    }
    void ChangeWeapon(bool first)
    {
        if(refreshing)return;if(first){draft.primary=primaryWeapons[primary.value];draft.primarySight=null;draft.primaryAttachments=Array.Empty<WeaponAttachmentDefinition>();}else{draft.secondary=secondaryWeapons[secondary.value];draft.secondarySight=null;draft.secondaryAttachments=Array.Empty<WeaponAttachmentDefinition>();}RefreshWeapon(first);
    }
    void RefreshWeapon(bool first)
    {
        var entry=catalog.Find(first?draft.primary:draft.secondary);var optics=entry.sights;var dropdown=first?primaryOptic:secondaryOptic;Options(dropdown,new[]{"BUILT-IN / DEFAULT"}.Concat(optics.Select(s=>s.displayName)));dropdown.SetValueWithoutNotify((first?draft.primarySight:draft.secondarySight)?Array.IndexOf(optics,first?draft.primarySight:draft.secondarySight)+1:0);
        dropdown.interactable=optics.Length>0; if(first)primarySights=optics;else secondarySights=optics;
        var controls=first?primaryAttachments:secondaryAttachments;var selected=first?draft.primaryAttachments:draft.secondaryAttachments;var choices=new WeaponAttachmentDefinition[controls.Length][];
        for(int i=0;i<controls.Length;i++){var kind=(WeaponAttachmentDefinition.MountKind)i;choices[i]=entry.attachments.Where(a=>a.mount==kind).ToArray();Options(controls[i],new[]{"STANDARD / NONE"}.Concat(choices[i].Select(a=>a.displayName)));controls[i].SetValueWithoutNotify(Array.IndexOf(choices[i],selected.FirstOrDefault(a=>a.mount==kind))+1);controls[i].interactable=choices[i].Length>0;}
        if(first){primaryOptions=choices;primaryIcon.sprite=entry.weapon.icon;}else{secondaryOptions=choices;secondaryIcon.sprite=entry.weapon.icon;}
        summary.text=$"PRIMARY: {draft.primary.category}   {draft.primary.damage:0} DAMAGE   {draft.primary.roundsPerMinute:0} RPM\nSECONDARY: {draft.secondary.category}   {draft.secondary.damage:0} DAMAGE\n";
    }
    public void Save()
    {
        draft.displayName=string.IsNullOrWhiteSpace(className.text)?"CUSTOM CLASS "+(draft.customIndex+1):className.text.Trim();
        draft.primarySight=primaryOptic.value==0?null:primarySights[primaryOptic.value-1];draft.secondarySight=secondaryOptic.value==0?null:secondarySights[secondaryOptic.value-1];
        WeaponAttachmentDefinition[] Selected(TMP_Dropdown[] controls,WeaponAttachmentDefinition[][] options)=>controls.Select((c,i)=>c.value==0?null:options[i][c.value-1]).Where(a=>a).ToArray();
        draft.primaryAttachments=Selected(primaryAttachments,primaryOptions);draft.secondaryAttachments=Selected(secondaryAttachments,secondaryOptions);draft.grenadeIndex=grenade.value;ArenaCustomClassStore.Save(draft);customSlot.options[draft.customIndex].text=draft.displayName;customSlot.RefreshShownValue();status.text="CLASS SAVED — SELECT IT TO DEPLOY OR ON YOUR NEXT RESPAWN.";saved?.Invoke(draft);
    }
    public void Close(){panel.SetActive(false);closed?.Invoke();}
}
