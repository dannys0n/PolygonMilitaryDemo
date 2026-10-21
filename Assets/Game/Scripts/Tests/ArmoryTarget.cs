using UnityEngine;
using TMPro;

// A shootable display. Catalog assets remain unchanged while testing equipment.
public class ArmoryTarget : MonoBehaviour
{
    [System.NonSerialized] public string weaponId;
    public bool IsWeapon => !string.IsNullOrEmpty(weaponId);
    public string WeaponId => weaponId;
    public WeaponSightDefinition sight;
    public bool resetSight;
    public WeaponAttachmentDefinition attachment;
    public Renderer plate;
    public TMP_Text label;
    public string title;

    MaterialPropertyBlock properties;
    bool hasDisplayState, lastCompatible, lastSelected;

    public void Show(bool compatible, bool selected)
    {
        if (hasDisplayState && compatible == lastCompatible && selected == lastSelected)
            return;
        hasDisplayState = true;
        lastCompatible = compatible;
        lastSelected = selected;
        var color = selected ? new Color(.72f,.29f,.055f) : compatible
            ? new Color(.105f,.13f,.15f) : new Color(.055f,.06f,.065f);
        properties ??= new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", color);
        plate.SetPropertyBlock(properties);
        label.color = compatible ? Color.white : new Color(.48f,.5f,.52f);
        label.text = title + (selected ? "  [EQUIPPED]" : compatible ? "" : "  [NOT COMPATIBLE]");
    }
}

