using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

// UI reads lightweight metadata and saves IDs; gameplay resolves those IDs at spawn.
public class ArenaClassEditor : MonoBehaviour
{
    public ArenaClassMenuCatalog menuCatalog;
    public GameObject panel;
    public TMP_Dropdown customSlot,primary,secondary,primaryOptic,secondaryOptic,grenade;
    public TMP_Dropdown[] utilities=Array.Empty<TMP_Dropdown>();
    public TMP_InputField className;
    public TMP_Dropdown[] primaryAttachments,secondaryAttachments;
    public UnityEngine.UI.Image primaryIcon,secondaryIcon;
    public TMP_Text summary,status;
    public UnityEngine.UI.Button save,back;
    public Sprite[] grenadeIcons;
    public ArenaWeaponPreview weaponPreview;
    public GameObject primaryGunsmith,secondaryGunsmith;
    public UnityEngine.UI.Button previewPrimary,previewSecondary;
    public UnityEngine.UI.Image primarySelection,secondarySelection;
    public TMP_Text previewTitle;
    bool previewFirst=true,refreshing;
    int selectedClass;
    ArenaCustomClassStore.Record draft;
    ArenaCustomClassStore.Record[] classes;
    ArenaClassMenuCatalog.Weapon[] primaryWeapons,secondaryWeapons,utilityWeapons;
    Action<ArenaClassDefinition> saved;
    Action closed;
    // Existing in-game callers already own this catalog. It is never serialized into the menu prefab.
    ArenaWeaponCatalog contextCatalog;
    bool onlyPolished;
    readonly Dictionary<string,WeaponOptions> options=new Dictionary<string,WeaponOptions>();
    readonly Dictionary<string,string> defaultSights=new Dictionary<string,string>();
    class WeaponOptions
    {
        public ArenaClassMenuCatalog.Part[] sights;
        public ArenaClassMenuCatalog.Part[][] attachments;
        public List<TMP_Dropdown.OptionData> sightLabels;
        public List<TMP_Dropdown.OptionData>[] attachmentLabels;
    }
    public bool IsVisible=>panel.activeSelf;
    public static ArenaClassEditor Open(Action<ArenaClassDefinition> saved=null,Action closed=null,int selected=0,ArenaWeaponCatalog weaponCatalog=null,bool polishedOnly=false)
    {
        if(saved!=null&&!weaponCatalog)throw new ArgumentException("A gameplay save callback requires its weapon catalog. Menu-only editing can omit both.",nameof(weaponCatalog));
        var editor=FindAnyObjectByType<ArenaClassEditor>(FindObjectsInactive.Include);
        if(!editor)editor=Instantiate(Resources.Load<GameObject>("ArenaClassEditor")).GetComponent<ArenaClassEditor>();
        editor.contextCatalog=weaponCatalog;editor.saved=saved;editor.closed=closed;editor.onlyPolished=polishedOnly;editor.menuCatalog.RefreshWeaponFacts();editor.ConfigureWeapons(weaponCatalog);editor.Show(selected);return editor;
    }
    void Awake()
    {
        panel.SetActive(false);customSlot.onValueChanged.AddListener(SelectClass);
        primary.onValueChanged.AddListener(_=>ChangeWeapon(true));secondary.onValueChanged.AddListener(_=>ChangeWeapon(false));
        save.onClick.AddListener(Save);back.onClick.AddListener(Close);
        previewPrimary.onClick.AddListener(()=>SetPreviewWeapon(true));previewSecondary.onClick.AddListener(()=>SetPreviewWeapon(false));
        primaryOptic.onValueChanged.AddListener(_=>RefreshPreview());secondaryOptic.onValueChanged.AddListener(_=>RefreshPreview());grenade.onValueChanged.AddListener(_=>{if(!refreshing)draft.grenade=grenade.value;});
        foreach(var d in primaryAttachments.Concat(secondaryAttachments))d.onValueChanged.AddListener(_=>RefreshPreview());
        foreach(var d in utilities)d.onValueChanged.AddListener(_=>{if(!refreshing)draft.utilities=utilities.Select(c=>c.value==0?null:utilityWeapons[c.value-1].id).ToArray();});
        grenade.options=new List<TMP_Dropdown.OptionData>{new TMP_Dropdown.OptionData("Frag",grenadeIcons[0],Color.white),new TMP_Dropdown.OptionData("Smoke",grenadeIcons[1],Color.white),new TMP_Dropdown.OptionData("Flashbang",grenadeIcons[2],Color.white)};
    }
    static List<TMP_Dropdown.OptionData> Labels(ArenaClassMenuCatalog.Part[] parts,string first)=>new[]{new TMP_Dropdown.OptionData(first)}.Concat(parts.Select(p=>new TMP_Dropdown.OptionData(p.name,p.icon,Color.white))).ToList();
    void ConfigureWeapons(ArenaWeaponCatalog catalog)
    {
        var allowed=catalog?new HashSet<string>(catalog.Weapons().Where(e=>!onlyPolished||e.polished||e.weapon.isUtility).Select(e=>e.weapon.catalogId)):null;
        var all=menuCatalog.weapons.Where(w=>allowed==null||allowed.Contains(w.id)).OrderBy(w=>w.category).ThenBy(w=>w.name).ToArray();
        options.Clear();defaultSights.Clear();
        foreach(var w in all)
        {
            var context=catalog?catalog.entries.First(e=>e.weapon.catalogId==w.id):null;
            defaultSights[w.id]=context!=null?(context.defaultSight?context.defaultSight.id:null):w.defaultSight;
            var o=new WeaponOptions();
            o.sights=(context!=null?context.sights.Where(s=>s).Select(s=>s.id):w.sights).Select(menuCatalog.FindPart).Where(p=>p!=null).ToArray();o.sightLabels=Labels(o.sights,"BUILT-IN / DEFAULT");
            o.attachments=new ArenaClassMenuCatalog.Part[primaryAttachments.Length][];o.attachmentLabels=new List<TMP_Dropdown.OptionData>[primaryAttachments.Length];
            var compatible=(context!=null?context.attachments.Where(a=>a).Select(a=>a.id):w.attachments).Select(menuCatalog.FindPart).Where(p=>p!=null).ToArray();
            for(int i=0;i<o.attachments.Length;i++){o.attachments[i]=compatible.Where(p=>p.mount==(WeaponAttachmentDefinition.MountKind)i).ToArray();o.attachmentLabels[i]=Labels(o.attachments[i],"STANDARD / NONE");}
            options[w.id]=o;
        }
        primaryWeapons=all.Where(ArenaClassMenuCatalog.IsPrimary).ToArray();secondaryWeapons=all.Where(w=>!w.isUtility&&!ArenaClassMenuCatalog.IsPrimary(w)).ToArray();
        utilityWeapons=all.Where(w=>w.isUtility).ToArray();
        foreach(var d in utilities)d.options=new[]{new TMP_Dropdown.OptionData("NONE")}.Concat(utilityWeapons.Select(w=>new TMP_Dropdown.OptionData(w.name,w.icon,Color.white))).ToList();
        primary.options=primaryWeapons.Select(w=>new TMP_Dropdown.OptionData(w.name,w.icon,Color.white)).ToList();secondary.options=secondaryWeapons.Select(w=>new TMP_Dropdown.OptionData(w.name,w.icon,Color.white)).ToList();
    }
    public void Show(int index)
    {
        if(primaryWeapons==null)ConfigureWeapons(contextCatalog);
        classes=ArenaCustomClassStore.LoadRecords();
        for(int i=0;i<classes.Length;i++)
        {
            var r=classes[i]??new ArenaCustomClassStore.Record{name="CUSTOM CLASS "+(i+1)};
            bool newPrimary=!primaryWeapons.Any(w=>w.id==r.primary),newSecondary=!secondaryWeapons.Any(w=>w.id==r.secondary);
            if(newPrimary){r.primary=primaryWeapons[0].id;r.primarySight=defaultSights[r.primary];r.primaryAttachments=Array.Empty<string>();}
            if(newSecondary){var pistol=secondaryWeapons.First(w=>w.category==SoldierWeaponDefinition.WeaponCategory.Pistol);r.secondary=pistol.id;r.secondarySight=defaultSights[r.secondary];r.secondaryAttachments=Array.Empty<string>();}
            if(r.utilities==null)r.utilities=new[]{utilityWeapons.FirstOrDefault(w=>w.id=="military-rpg")?.id,utilityWeapons.FirstOrDefault(w=>w.id=="military-rocketlauncher")?.id};
            r.primaryAttachments=r.primaryAttachments??Array.Empty<string>();r.secondaryAttachments=r.secondaryAttachments??Array.Empty<string>();classes[i]=r;
        }
        refreshing=true;customSlot.options=classes.Select(r=>new TMP_Dropdown.OptionData(r.name)).ToList();customSlot.SetValueWithoutNotify(Mathf.Clamp(index,0,4));refreshing=false;
        panel.SetActive(true);SelectClass(customSlot.value);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    void SelectClass(int index)
    {
        if(refreshing)return;selectedClass=index;draft=classes[index];refreshing=true;className.SetTextWithoutNotify(draft.name);
        primary.SetValueWithoutNotify(Array.FindIndex(primaryWeapons,w=>w.id==draft.primary));secondary.SetValueWithoutNotify(Array.FindIndex(secondaryWeapons,w=>w.id==draft.secondary));grenade.SetValueWithoutNotify(Mathf.Clamp(draft.grenade,0,2));refreshing=false;
        refreshing=true;for(int i=0;i<utilities.Length;i++)utilities[i].SetValueWithoutNotify(Array.FindIndex(utilityWeapons,w=>draft.utilities!=null&&i<draft.utilities.Length&&w.id==draft.utilities[i])+1);refreshing=false;
        RefreshWeapon(true);RefreshWeapon(false);SetPreviewWeapon(previewFirst);status.text="CHOOSE YOUR LOADOUT. CHANGES APPLY ON SPAWN.";
    }
    void ChangeWeapon(bool first)
    {
        if(refreshing)return;var weapon=first?primaryWeapons[primary.value]:secondaryWeapons[secondary.value];
        if(first){draft.primary=weapon.id;draft.primarySight=defaultSights[weapon.id];draft.primaryAttachments=Array.Empty<string>();}else{draft.secondary=weapon.id;draft.secondarySight=defaultSights[weapon.id];draft.secondaryAttachments=Array.Empty<string>();}
        RefreshWeapon(first);SetPreviewWeapon(first);
    }
    void RefreshWeapon(bool first)
    {
        refreshing=true;
        var w=menuCatalog.FindWeapon(first?draft.primary:draft.secondary);var o=options[w.id];var optic=first?primaryOptic:secondaryOptic;
        optic.options=o.sightLabels;optic.SetValueWithoutNotify(Array.FindIndex(o.sights,p=>p.id==(first?draft.primarySight:draft.secondarySight))+1);optic.interactable=o.sights.Length>0;
        var controls=first?primaryAttachments:secondaryAttachments;var selected=first?draft.primaryAttachments:draft.secondaryAttachments;
        for(int i=0;i<controls.Length;i++){controls[i].options=o.attachmentLabels[i];controls[i].SetValueWithoutNotify(Array.FindIndex(o.attachments[i],p=>selected.Contains(p.id))+1);controls[i].interactable=o.attachments[i].Length>0;}
        (first?primaryIcon:secondaryIcon).sprite=w.icon;refreshing=false;
    }
    public void SetPreviewWeapon(bool first){previewFirst=first;primaryGunsmith.SetActive(first);secondaryGunsmith.SetActive(!first);primarySelection.gameObject.SetActive(first);secondarySelection.gameObject.SetActive(!first);RefreshPreview();}
    string[] Selected(TMP_Dropdown[] controls,WeaponOptions o)=>controls.Select((c,i)=>c.value==0?null:o.attachments[i][c.value-1].id).Where(id=>id!=null).ToArray();
    void UpdateDraft()
    {
        var p=options[draft.primary];var s=options[draft.secondary];draft.primarySight=primaryOptic.value==0?null:p.sights[primaryOptic.value-1].id;draft.secondarySight=secondaryOptic.value==0?null:s.sights[secondaryOptic.value-1].id;
        draft.primaryAttachments=Selected(primaryAttachments,p);draft.secondaryAttachments=Selected(secondaryAttachments,s);draft.grenade=grenade.value;draft.utilities=utilities.Select(c=>c.value==0?null:utilityWeapons[c.value-1].id).ToArray();
    }
    void RefreshPreview()
    {
        if(refreshing||draft==null)return;UpdateDraft();
        var w=menuCatalog.FindWeapon(previewFirst?draft.primary:draft.secondary);weaponPreview.Show(menuCatalog,w,previewFirst?draft.primarySight:draft.secondarySight,previewFirst?draft.primaryAttachments:draft.secondaryAttachments);
        previewTitle.text=w.name.ToUpperInvariant()+"  /  GUNSMITH";summary.text=$"{w.category.ToString().ToUpperInvariant()}    {w.damage:0} DAMAGE    {w.rpm:0} RPM    {w.rounds} ROUNDS\nDRAG THE MODEL TO ROTATE. SELECT A SLOT TO PREVIEW ATTACHMENTS.";
    }
    public void Save()
    {
        UpdateDraft();draft.name=string.IsNullOrWhiteSpace(className.text)?"CUSTOM CLASS "+(selectedClass+1):className.text.Trim();ArenaCustomClassStore.SaveRecord(selectedClass,draft);
        customSlot.options[selectedClass].text=draft.name;customSlot.RefreshShownValue();status.text="CLASS SAVED — SELECT IT TO DEPLOY OR ON YOUR NEXT RESPAWN.";
        if(saved!=null&&contextCatalog)saved(ArenaCustomClassStore.Load(contextCatalog)[selectedClass]);
    }
    public void Close(){panel.SetActive(false);var callback=closed;saved=null;closed=null;contextCatalog=null;callback?.Invoke();}
    void OnDestroy(){saved=null;closed=null;contextCatalog=null;}
}
