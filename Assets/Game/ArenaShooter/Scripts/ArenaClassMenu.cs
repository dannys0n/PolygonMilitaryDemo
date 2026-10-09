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
    }
    void Start()
    {
        if (!participant) return;
        foreach (var choice in participant.match.settings.classes)
        {
            var row = Instantiate(rowTemplate, rowTemplate.transform.parent);
            row.gameObject.SetActive(true);
            row.GetComponentInChildren<TMP_Text>().text = choice.displayName;
            row.onClick.AddListener(() => Preview(choice));
            rows.Add(row);
        }
        participant.match.MatchEnded += OnMatchEnded;
        if (participant.NeedsClassSelection) Open();
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
        primaryName.text = choice.primary.name;
        secondaryName.text = choice.secondary.name;
        primaryIcon.sprite = choice.primary.icon; secondaryIcon.sprite = choice.secondary.icon;
        status.text = participant.NeedsClassSelection ? "SELECT A CLASS TO SPAWN" : "CHANGES APPLY ON YOUR NEXT RESPAWN";
        confirm.GetComponentInChildren<TMP_Text>().text = participant.NeedsClassSelection ? "DEPLOY" : "SELECT CLASS";
        for (int i = 0; i < rows.Count; i++)
            rows[i].GetComponent<Image>().color = participant.match.settings.classes[i] == choice
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

