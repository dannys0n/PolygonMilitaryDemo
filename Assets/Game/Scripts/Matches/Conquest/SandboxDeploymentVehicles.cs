using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A view over the sandbox's permitted HQ vehicles, not another spawn controller.
public sealed class SandboxDeploymentVehicles : MonoBehaviour
{
    public ConquestDeploymentMenu menu;
    public GameObject picker;
    public RectTransform content;
    public Button rowTemplate;
    sealed class Row
    {
        public Vehicle vehicle;
        public TMP_Text label;
        public Image background;
        public ArenaMenuSelectionFeedback feedback;
    }
    readonly List<Row> rows=new List<Row>();
    Vehicle selectedVehicle;
    Material normalText, selectedText;
    bool refreshSelection=true;
    SandboxConquestRules rules;
    void Start()
    {
        rules=menu.rules as SandboxConquestRules;
        if(!rules){picker.SetActive(false);return;}
        rowTemplate.gameObject.SetActive(false);
        foreach(var vehicle in rules.deploymentVehicles)
        {
            if(!vehicle)continue;
            var row=Instantiate(rowTemplate,content);row.name=vehicle.displayName;
            row.onClick.RemoveAllListeners();row.onClick.AddListener(()=>rules.SelectVehicle(vehicle));
            var label=row.GetComponentInChildren<TMP_Text>();label.text=vehicle.displayName.ToUpperInvariant();
            var icon=row.transform.Find("Vehicle icon").GetComponent<Image>();icon.sprite=vehicle.seatDiagram;icon.enabled=icon.sprite;
            row.gameObject.SetActive(true);
            rows.Add(new Row { vehicle=vehicle, label=label, background=row.GetComponent<Image>(), feedback=row.GetComponent<ArenaMenuSelectionFeedback>() });
        }
    }
    void LateUpdate()
    {
        if(!rules)return;
        bool visible=menu.IsVisible && !menu.IsTacticalMap && !menu.SelectedPoint && !menu.SelectedMember;
        if(picker.activeSelf!=visible)picker.SetActive(visible);
        if(!visible)return;
        if(!refreshSelection && selectedVehicle==rules.SelectedVehicle
            && normalText==menu.NormalText && selectedText==menu.SelectedText)return;
        refreshSelection=false;selectedVehicle=rules.SelectedVehicle;
        normalText=menu.NormalText;selectedText=menu.SelectedText;
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i];bool selected=row.vehicle==selectedVehicle;var label=row.label;
            if(row.feedback)row.feedback.Chosen=selected;
            else
            {
                var material=selected?selectedText:normalText;
                if(label.fontSharedMaterial!=material)label.fontSharedMaterial=material;
                label.color=selected?Color.white:new Color(.75f,.75f,.75f);
            }
            row.background.color=selected?ArenaHudTheme.SectionBackground:Color.clear;
        }
    }
}
