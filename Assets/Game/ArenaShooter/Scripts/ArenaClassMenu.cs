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
    public ArenaWeaponCatalog catalog;
    public Button createClass;
    readonly System.Collections.Generic.List<ArenaClassDefinition> choices=new System.Collections.Generic.List<ArenaClassDefinition>();
    public bool IsVisible => panel.activeSelf;
    ArenaParticipant participant;
    ArenaClassDefinition preview;
    ArenaAISpectator spectator;
    bool previousSpectatorBlocked;
    readonly System.Collections.Generic.List<Button> rows = new System.Collections.Generic.List<Button>();

    void Awake()
    {
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
        RefreshChoices();
        participant.match.MatchEnded += OnMatchEnded;
        if (participant.NeedsClassSelection) Open();
    }
    void RefreshChoices()
    {
        foreach(var old in rows){old.gameObject.SetActive(false);Destroy(old.gameObject);}rows.Clear();choices.Clear();
        choices.AddRange(participant.match.settings.classes);choices.AddRange(ArenaCustomClassStore.Load(catalog));
        foreach (var choice in choices)
        {
            var row = Instantiate(rowTemplate, rowTemplate.transform.parent);
            row.gameObject.SetActive(true);
            row.GetComponentInChildren<TMP_Text>().text = choice.displayName;
            row.onClick.AddListener(() => Preview(choice));
            rows.Add(row);
        }
    }
    public void OpenEditor()
    {
        ArenaClassEditor.Open(choice=>{RefreshChoices();Preview(choice);},null,preview&&preview.isCustom?preview.customIndex:0,catalog);
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
            rows[i].GetComponent<Image>().color = choices[i].id == choice.id
                ? new Color(1f, .55f, .12f, .3f) : new Color(.12f, .14f, .16f, .65f);
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

