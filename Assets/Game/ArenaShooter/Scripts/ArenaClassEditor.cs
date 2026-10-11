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
    [Serializable] public class OptionIcon { public UnityEngine.Object asset; public Sprite icon; }
    public OptionIcon[] optionIcons=Array.Empty<OptionIcon>();
    public Sprite[] grenadeIcons;
    public ArenaWeaponPreview weaponPreview;
    public GameObject primaryGunsmith,secondaryGunsmith;
    public UnityEngine.UI.Button previewPrimary,previewSecondary;
    public UnityEngine.UI.Image primarySelection, secondarySelection;
    public TMP_Text previewTitle;
    bool previewFirst=true;
    ArenaClassDefinition draft;
    ArenaClassDefinition[] classes;
    SoldierWeaponDefinition[] primaryWeapons,secondaryWeapons;
    WeaponSightDefinition[] primarySights,secondarySights;
    WeaponAttachmentDefinition[][] primaryOptions,secondaryOptions;
    Action<ArenaClassDefinition> saved;
    Action closed;
    bool refreshing;
    public bool IsVisible=>panel.activeSelf;

    public static ArenaClassEditor Open(Action<ArenaClassDefinition> saved=null,Action closed=null,int selected=0,ArenaWeaponCatalog weaponCatalog=null)
    {
        var editor=FindAnyObjectByType<ArenaClassEditor>(FindObjectsInactive.Include);
        if(!editor)editor=Instantiate(Resources.Load<GameObject>("ArenaClassEditor")).GetComponent<ArenaClassEditor>();
        if(weaponCatalog)editor.catalog=weaponCatalog;
        editor.saved=saved;editor.closed=closed;editor.Show(selected);return editor;
    }
    void Awake()
    {
        panel.SetActive(false);customSlot.onValueChanged.AddListener(SelectClass);
        primary.onValueChanged.AddListener(_=>ChangeWeapon(true));secondary.onValueChanged.AddListener(_=>ChangeWeapon(false));
        save.onClick.AddListener(Save);back.onClick.AddListener(Close);
        previewPrimary.onClick.AddListener(()=>SetPreviewWeapon(true));previewSecondary.onClick.AddListener(()=>SetPreviewWeapon(false));
        primaryOptic.onValueChanged.AddListener(_=>RefreshPreview());secondaryOptic.onValueChanged.AddListener(_=>RefreshPreview());
        foreach(var dropdown in primaryAttachments.Concat(secondaryAttachments))dropdown.onValueChanged.AddListener(_=>RefreshPreview());
    }
    public void Show(int index)
    {
        classes=ArenaCustomClassStore.Load(catalog);primaryWeapons=catalog.entries.Select(e=>e.weapon).Where(ArenaCustomClassStore.IsPrimary).OrderBy(w=>w.category).ThenBy(w=>w.displayName).ToArray();secondaryWeapons=catalog.entries.Select(e=>e.weapon).Where(w=>!ArenaCustomClassStore.IsPrimary(w)).OrderBy(w=>w.category).ThenBy(w=>w.displayName).ToArray();
        refreshing=true;Options(customSlot,classes.Select(c=>c.displayName));Options(primary,primaryWeapons.Select(w=>w.displayName),primaryWeapons.Select(w=>w.icon));Options(secondary,secondaryWeapons.Select(w=>w.displayName),secondaryWeapons.Select(w=>w.icon));Options(grenade,new[]{"Frag","Smoke","Flashbang"},grenadeIcons);customSlot.SetValueWithoutNotify(Mathf.Clamp(index,0,4));refreshing=false;panel.SetActive(true);SelectClass(customSlot.value);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    static void Options(TMP_Dropdown control,System.Collections.Generic.IEnumerable<string> names,System.Collections.Generic.IEnumerable<Sprite> icons=null)
    {
        var labels=names.ToArray();var images=icons?.ToArray();control.ClearOptions();
        control.AddOptions(labels.Select((name,i)=>new TMP_Dropdown.OptionData(name,images!=null&&i<images.Length?images[i]:null,Color.white)).ToList());control.RefreshShownValue();
    }
    Sprite Icon(UnityEngine.Object asset)=>optionIcons.FirstOrDefault(x=>x.asset==asset)?.icon;
    void SelectClass(int index)
    {
        if(refreshing)return;draft=classes[index];refreshing=true;className.SetTextWithoutNotify(draft.displayName);primary.SetValueWithoutNotify(Array.IndexOf(primaryWeapons,draft.primary));secondary.SetValueWithoutNotify(Array.IndexOf(secondaryWeapons,draft.secondary));grenade.SetValueWithoutNotify(draft.grenadeIndex);refreshing=false;RefreshWeapon(true);RefreshWeapon(false);SetPreviewWeapon(previewFirst);status.text="CHOOSE YOUR LOADOUT. CHANGES APPLY ON SPAWN.";
    }
    void ChangeWeapon(bool first)
    {
        if(refreshing)return;if(first){draft.primary=primaryWeapons[primary.value];draft.primarySight=catalog.Find(draft.primary).defaultSight;draft.primaryAttachments=Array.Empty<WeaponAttachmentDefinition>();}else{draft.secondary=secondaryWeapons[secondary.value];draft.secondarySight=catalog.Find(draft.secondary).defaultSight;draft.secondaryAttachments=Array.Empty<WeaponAttachmentDefinition>();}RefreshWeapon(first);SetPreviewWeapon(first);
    }
    void RefreshWeapon(bool first)
    {
        var entry=catalog.Find(first?draft.primary:draft.secondary);var optics=entry.sights;var dropdown=first?primaryOptic:secondaryOptic;Options(dropdown,new[]{"BUILT-IN / DEFAULT"}.Concat(optics.Select(s=>s.displayName)),new Sprite[]{null}.Concat(optics.Select(s=>Icon(s))));dropdown.SetValueWithoutNotify((first?draft.primarySight:draft.secondarySight)?Array.IndexOf(optics,first?draft.primarySight:draft.secondarySight)+1:0);
        dropdown.interactable=optics.Length>0; if(first)primarySights=optics;else secondarySights=optics;
        var controls=first?primaryAttachments:secondaryAttachments;var selected=first?draft.primaryAttachments:draft.secondaryAttachments;var choices=new WeaponAttachmentDefinition[controls.Length][];
        for(int i=0;i<controls.Length;i++){var kind=(WeaponAttachmentDefinition.MountKind)i;choices[i]=entry.attachments.Where(a=>a.mount==kind).ToArray();Options(controls[i],new[]{"STANDARD / NONE"}.Concat(choices[i].Select(a=>a.displayName)),new Sprite[]{null}.Concat(choices[i].Select(a=>Icon(a))));controls[i].SetValueWithoutNotify(Array.IndexOf(choices[i],selected.FirstOrDefault(a=>a.mount==kind))+1);controls[i].interactable=choices[i].Length>0;}
        if(first){primaryOptions=choices;primaryIcon.sprite=entry.weapon.icon;}else{secondaryOptions=choices;secondaryIcon.sprite=entry.weapon.icon;}
        summary.text=$"PRIMARY: {draft.primary.category}   {draft.primary.damage:0} DAMAGE   {draft.primary.roundsPerMinute:0} RPM\nSECONDARY: {draft.secondary.category}   {draft.secondary.damage:0} DAMAGE\n";
    }
    public void SetPreviewWeapon(bool first)
    {
        previewFirst=first;primaryGunsmith.SetActive(first);secondaryGunsmith.SetActive(!first);
        // Keep the selected state for text feedback instead of repainting the cards.
        primarySelection.gameObject.SetActive(first);
        secondarySelection.gameObject.SetActive(!first);
        RefreshPreview();
    }
    WeaponAttachmentDefinition[] Selected(TMP_Dropdown[] controls,WeaponAttachmentDefinition[][] options)
        =>controls.Select((c,i)=>c.value==0?null:options[i][c.value-1]).Where(a=>a).ToArray();
    void RefreshPreview()
    {
        if(refreshing || !draft || primaryOptions==null || secondaryOptions==null)return;
        var weapon=previewFirst?draft.primary:draft.secondary;
        var sights=previewFirst?primarySights:secondarySights;var optic=previewFirst?primaryOptic:secondaryOptic;
        var attachments=Selected(previewFirst?primaryAttachments:secondaryAttachments,previewFirst?primaryOptions:secondaryOptions);
        weaponPreview.Show(catalog.Find(weapon),optic.value==0?null:sights[optic.value-1],attachments);
        previewTitle.text=weapon.displayName.ToUpperInvariant()+"  /  GUNSMITH";
        summary.text=$"{weapon.category.ToString().ToUpperInvariant()}    {weapon.damage:0} DAMAGE    {weapon.roundsPerMinute:0} RPM    {weapon.magazineCapacity} ROUNDS\nDRAG THE MODEL TO ROTATE. SELECT A SLOT TO PREVIEW ATTACHMENTS.";
    }
    public void Save()
    {
        draft.displayName=string.IsNullOrWhiteSpace(className.text)?"CUSTOM CLASS "+(draft.customIndex+1):className.text.Trim();
        draft.primarySight=primaryOptic.value==0?null:primarySights[primaryOptic.value-1];draft.secondarySight=secondaryOptic.value==0?null:secondarySights[secondaryOptic.value-1];
        draft.primaryAttachments=Selected(primaryAttachments,primaryOptions);draft.secondaryAttachments=Selected(secondaryAttachments,secondaryOptions);draft.grenadeIndex=grenade.value;ArenaCustomClassStore.Save(draft);customSlot.options[draft.customIndex].text=draft.displayName;customSlot.RefreshShownValue();status.text="CLASS SAVED — SELECT IT TO DEPLOY OR ON YOUR NEXT RESPAWN.";saved?.Invoke(draft);
    }
    public void Close(){panel.SetActive(false);closed?.Invoke();}
}
