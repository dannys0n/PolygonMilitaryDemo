using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Class selection only queues a loadout. ArenaParticipant applies it at spawn.
[DefaultExecutionOrder(400)]
public class ArenaClassMenu : MonoBehaviour
{
    public ArenaPauseMenu pause;
    public GameObject panel;
    public Button rowTemplate;
    public TMP_Text primaryName, secondaryName, status;
    public Image primaryIcon, secondaryIcon;
    public Sprite rifleSprite, pistolSprite;
    public Button confirm, back, changeClass;
    ArenaWeaponCatalog catalog;
    public Button createClass;
    public Material normalText, selectedText;
    public Material selectionFilament;
    public TMP_FontAsset menuFont;
    readonly System.Collections.Generic.List<ArenaClassDefinition> choices=new System.Collections.Generic.List<ArenaClassDefinition>();
    public bool IsVisible => panel.activeSelf;
    ArenaParticipant participant;
    ArenaClassDefinition preview;
    ArenaAISpectator spectator;
    bool previousSpectatorBlocked;
    readonly System.Collections.Generic.List<Button> rows = new System.Collections.Generic.List<Button>();

    void Awake()
    {
        StylePanel();
        panel.SetActive(false);
        rowTemplate.gameObject.SetActive(false);
        var input = FindAnyObjectByType<SoldierLocalInput>();
        if (input) participant = input.GetComponent<ArenaParticipant>();
        spectator = FindAnyObjectByType<ArenaAISpectator>();
        changeClass.gameObject.SetActive(participant);
        confirm.onClick.AddListener(Choose);
        back.onClick.AddListener(Back);
        changeClass.onClick.AddListener(Open);
        createClass.onClick.AddListener(OpenEditor);
    }
    void Start()
    {
        if (!participant) return;
        catalog=participant.match.settings.weaponCatalog;
        if(!catalog)throw new System.InvalidOperationException("Assign a weapon catalog in the match settings.");
        RefreshChoices();
        participant.match.MatchEnded += OnMatchEnded;
        if (participant.NeedsClassSelection) Open();
    }
    void RefreshChoices()
    {
        foreach(var old in rows){old.gameObject.SetActive(false);Destroy(old.gameObject);}rows.Clear();choices.Clear();
        choices.AddRange(participant.match.settings.classes);
        foreach(var choice in ArenaCustomClassStore.Load(catalog))
            if(!participant.match.settings.polishedWeaponsOnly || catalog.Find(choice.primary).polished&&catalog.Find(choice.secondary).polished)choices.Add(choice);
        foreach (var choice in choices)
        {
            var row = Instantiate(rowTemplate, rowTemplate.transform.parent);
            StyleRow(row, choice);
            row.gameObject.SetActive(true);
            row.GetComponentInChildren<TMP_Text>().text = choice.displayName;
            row.onClick.AddListener(() => Preview(choice));
            rows.Add(row);
        }
    }
    public void OpenEditor()
    {
        ArenaClassEditor.Open(choice=>{RefreshChoices();Preview(choice);},null,preview&&preview.isCustom?preview.customIndex:0,catalog,participant.match.settings.polishedWeaponsOnly);
    }
    public void Open()
    {
        if (!participant || !participant.match.IsRunning || IsVisible) return;
        pause.Pause(); pause.panel.SetActive(false);
        if (spectator) { previousSpectatorBlocked = spectator.ControlsBlocked; spectator.ControlsBlocked = true; }
        panel.SetActive(true);
        Preview(participant.SelectedClass ? participant.SelectedClass : participant.match.settings.classes[0]);
        back.interactable = !participant.NeedsClassSelection;
    }
    void Preview(ArenaClassDefinition choice)
    {
        preview = choice;
        primaryName.text = choice.primary.displayName;
        secondaryName.text = choice.secondary.displayName;
        primaryIcon.sprite = choice.primary.icon; secondaryIcon.sprite = choice.secondary.icon;
        bool deployment = participant.match.rules && participant.match.rules.NeedsDeployment(participant);
        status.text = participant.NeedsClassSelection ? (deployment ? "SELECT A CLASS, THEN A DEPLOYMENT LOCATION" : "SELECT A CLASS TO SPAWN") : "CHANGES APPLY ON YOUR NEXT RESPAWN";
        confirm.GetComponentInChildren<TMP_Text>().text = participant.NeedsClassSelection && !deployment ? "DEPLOY" : "SELECT CLASS";
        for (int i = 0; i < rows.Count; i++)
        {
            bool selected = choices[i].id == choice.id;
            rows[i].GetComponent<Image>().color = new Color(1, 1, 1, selected ? .055f : .015f);
            rows[i].GetComponent<ArenaMenuSelectionFeedback>().selectionIndicator.SetActive(selected);
        }
    }
    void StylePanel()
    {
        var background = panel.GetComponent<Image>();
        background.color = new Color(.025f, .025f, .025f, .92f);
        foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            label.font = menuFont;
            label.fontSharedMaterial = normalText;
            label.fontStyle = FontStyles.Normal;
            label.color = new Color(.8f, .8f, .8f);
        }
        foreach (var button in new[] { confirm, back, createClass }) StyleButton(button);
        foreach (var scroll in panel.GetComponentsInChildren<ScrollRect>(true)) scroll.scrollSensitivity = 65f;
        var layout = rowTemplate.transform.parent.GetComponent<VerticalLayoutGroup>();
        if (layout) layout.spacing = 8f;
        primaryIcon.preserveAspect = secondaryIcon.preserveAspect = true;
        primaryIcon.color = secondaryIcon.color = Color.white;
        primaryName.color = secondaryName.color = Color.white;
        primaryName.fontSharedMaterial = secondaryName.fontSharedMaterial = selectedText;
        foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
            if (label.name.EndsWith("header") || label.name == "Presets")
            {
                label.fontSize = 19f;
                label.characterSpacing = 5f;
                label.color = new Color(1, .55f, .12f);
            }
        status.fontSize = 17f;
        status.color = new Color(.65f, .65f, .65f);
    }
    ArenaMenuSelectionFeedback StyleButton(Button button)
    {
        button.transition = Selectable.Transition.None;
        button.GetComponent<Image>().color = Color.clear;
        var feedback = button.GetComponent<ArenaMenuSelectionFeedback>();
        if (!feedback) feedback = button.gameObject.AddComponent<ArenaMenuSelectionFeedback>();
        feedback.control = button;
        feedback.labels = button.GetComponentsInChildren<TMP_Text>(true);
        feedback.normalText = normalText;
        feedback.selectedText = selectedText;
        feedback.brightenOnPress = true;
        return feedback;
    }
    void StyleRow(Button row, ArenaClassDefinition choice)
    {
        var feedback = StyleButton(row);
        var layout = row.GetComponent<LayoutElement>();
        if (!layout) layout = row.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = 64f;
        var label = row.GetComponentInChildren<TMP_Text>(true);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSize = 25f;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(28, 24);
        label.rectTransform.offsetMax = new Vector2(-16, -4);
        var detailObject = new GameObject("Loadout summary", typeof(RectTransform), typeof(TextMeshProUGUI));
        detailObject.transform.SetParent(row.transform, false);
        var detail = detailObject.GetComponent<TextMeshProUGUI>();
        detail.font = label.font;
        detail.fontSharedMaterial = normalText;
        detail.fontSize = 15f;
        detail.text = choice.primary.displayName + "  /  " + choice.secondary.displayName;
        detail.raycastTarget = false;
        detail.alignment = TextAlignmentOptions.MidlineLeft;
        detail.rectTransform.anchorMin = Vector2.zero;
        detail.rectTransform.anchorMax = Vector2.one;
        detail.rectTransform.offsetMin = new Vector2(28, 3);
        detail.rectTransform.offsetMax = new Vector2(-16, -35);
        var marker = new GameObject("Selected class", typeof(RectTransform), typeof(Image));
        marker.transform.SetParent(row.transform, false);
        var image = marker.GetComponent<Image>();
        image.material = selectionFilament;
        image.color = Color.white;
        image.raycastTarget = false;
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0, .5f);
        image.rectTransform.anchoredPosition = new Vector2(12, 0);
        image.rectTransform.sizeDelta = new Vector2(12, 40);
        marker.SetActive(false);
        feedback.selectionIndicator = marker;
        feedback.labels = new TMP_Text[] { label, detail };
    }
    void Choose()
    {
        participant.SelectClass(preview);
        Close(); pause.Resume();
    }
    public void Back()
    {
        if (participant.NeedsClassSelection) return;
        Close(); pause.panel.SetActive(true);
    }
    void Close()
    {
        panel.SetActive(false);
        if (spectator) spectator.ControlsBlocked = previousSpectatorBlocked;
    }
    void OnMatchEnded() { if (IsVisible) { Close(); pause.Resume(); } }
    void OnDestroy() { if (participant) participant.match.MatchEnded -= OnMatchEnded; }
}

