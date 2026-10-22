using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Deployment queues an ordinary ArenaMatch spawn; it never pauses the battle.
[DefaultExecutionOrder(500)]
public class ConquestDeploymentMenu : MonoBehaviour
{
    public ConquestRules rules;
    public ArenaParticipant participant;
    public SoldierLocalInput input;
    public ArenaPauseMenu pause;
    public Material NormalText => pause.normalText;
    public Material SelectedText => pause.selectedText;
    public ArenaAISpectator spectator;
    public GameObject panel;
    public Button baseButton, deployButton;
    [UnityEngine.Serialization.FormerlySerializedAs("classButton")] public Button returnButton;
    public Button[] flagButtons;
    public Button[] squadButtons=System.Array.Empty<Button>();
    readonly System.Collections.Generic.List<ArenaParticipant> squadMembers=new System.Collections.Generic.List<ArenaParticipant>();
    ArenaParticipant selectedMember;
    public TMP_Text status;
    [UnityEngine.Serialization.FormerlySerializedAs("className")] public TMP_Text equipmentLabel;
    ConquestCapturePoint selected;
    bool visible, requested, previousInputBlocked, previousSpectatorBlocked, restoreOnRestart;
    float nextAttempt;
    bool tacticalMap;
    bool redeploying;
    public bool IsSandbox => rules is SandboxConquestRules;
    public void OpenRedeployment()
    {
        if(!IsSandbox || participant.IsSpectator)return;
        pause.Resume();tacticalMap=false;redeploying=true;requested=false;
        SelectBase();SetVisible(true);
    }
    public void RequestDeployment()
    {
        if(tacticalMap || participant.RespawnRemaining>0)return;
        requested=true;nextAttempt=0;rules.ApproveDeployment(participant);
    }
    public bool IsTacticalMap => tacticalMap;
    public bool CloseTacticalMap()
    {
        if(!tacticalMap && !redeploying)return false;
        tacticalMap=false;redeploying=false;requested=false;rules.CancelDeployment(participant);SetVisible(false);return true;
    }
    public bool IsVisible => visible;
    public ConquestCapturePoint SelectedPoint => selected;
    public ArenaParticipant SelectedMember => selectedMember;
    public void SelectBase() => Select(null);
    public void SelectFlag(ConquestCapturePoint point) { if(tacticalMap || rules.CanDeploy(participant,point)) Select(point); }
    public void SelectSquadmate(ArenaParticipant member)
    {
        if(tacticalMap){selected=null;selectedMember=member;return;}
        if(!rules.squads || !rules.squads.CanDeploy(participant,member)) return;
        requested=false;rules.CancelDeployment(participant);
        selected=null;selectedMember=member;rules.SelectSquadDeployment(participant,member);
    }

    void Awake()
    {
        panel.SetActive(false);
        if(panel.TryGetComponent<Image>(out var background))background.color=ArenaHudTheme.FullBackground;
        ArenaHudTheme.StyleBackgrounds(panel.transform);
        StyleLocation(baseButton);
        foreach(var button in flagButtons)StyleLocation(button);
        foreach(var button in squadButtons)StyleLocation(button);
        baseButton.onClick.AddListener(()=>Select(null));
        for(int i=0;i<flagButtons.Length && i<rules.points.Length;i++)
        {
            var point=rules.points[i];
            flagButtons[i].onClick.AddListener(()=>Select(point));
        }
        for(int i=0;i<squadButtons.Length;i++)
        {
            int index=i;
            squadButtons[i].onClick.AddListener(()=>
            {
                SelectSquadmate(squadMembers[index]);
            });
        }
        deployButton.onClick.AddListener(RequestDeployment);
        returnButton.onClick.AddListener(()=>CloseTacticalMap());
    }
    static void StyleLocation(Button button)
    {
        if(!button || !button.TryGetComponent<Image>(out var image))return;
        image.color=ArenaHudTheme.SectionBackground;
        var colors=button.colors;
        colors.normalColor=Color.white;colors.highlightedColor=Color.white;
        colors.pressedColor=Color.white;colors.selectedColor=Color.white;
        button.colors=colors;
    }
    void Start() { participant.match.MatchStarted+=ResetSelection; }
    void ResetSelection()
    {
        tacticalMap=false;redeploying=false;requested=false;Select(null);
        if(visible)SetVisible(false);
        if(restoreOnRestart)
        {
            input.InputBlocked=previousInputBlocked;
            if(spectator)spectator.ControlsBlocked=previousSpectatorBlocked;
            restoreOnRestart=false;
        }
    }
    void Select(ConquestCapturePoint point) { if(tacticalMap){selected=point;selectedMember=null;return;} requested=false;rules.CancelDeployment(participant);selected=point;selectedMember=null;rules.SelectDeployment(participant,point); }
    void LateUpdate()
    {
        bool waiting=participant.health.IsDead || participant.IsAwaitingSpawn;
        if(!waiting && !redeploying)requested=false;
        bool unobstructed=participant.match.IsRunning && !pause.IsPaused;
        if(Input.GetKeyDown(KeyCode.M) && unobstructed && !waiting)
        {
            tacticalMap=!tacticalMap;
            if(tacticalMap){selected=null;selectedMember=null;}
        }
        if(waiting || !unobstructed)tacticalMap=false;
        bool show=rules.NeedsDeployment(participant) && (!spectator || !spectator.IsWatchingDeath)
            && !participant.IsSpectator && participant.match.IsRunning && waiting && !pause.IsPaused;
        show|=tacticalMap || redeploying && unobstructed;
        SetVisible(show);
        if(!show)return;
        deployButton.gameObject.SetActive(!tacticalMap);
        returnButton.gameObject.SetActive(!tacticalMap && redeploying);
        returnButton.GetComponentInChildren<TMP_Text>().text="RETURN TO BATTLE";
        if(!tacticalMap && (!rules.CanDeploy(participant,selected) || selectedMember && !rules.squads.CanDeploy(participant,selectedMember)))Select(null);
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        var sandbox=rules as SandboxConquestRules;
        equipmentLabel.text=IsSandbox ? selected?"INFANTRY / RANDOM POLISHED EQUIPMENT":sandbox.SelectedVehicle?sandbox.SelectedVehicle.displayName.ToUpperInvariant():"SELECT AN HQ VEHICLE" : "RANDOM EQUIPMENT";
        baseButton.GetComponentInChildren<TMP_Text>().text=(selected || selectedMember?"":"> ")+"HQ";
        for(int i=0;i<flagButtons.Length;i++)
        {
            flagButtons[i].gameObject.SetActive(i<rules.points.Length);
            if(i>=rules.points.Length)continue;
            var p=rules.points[i];var b=flagButtons[i];
            b.interactable=tacticalMap || rules.CanDeploy(participant,p);
            var label=b.GetComponentInChildren<TMP_Text>();
            label.text=(selected==p?"> ":"")+p.letter+"  /  "+p.displayName.ToUpperInvariant()+(p.IsContested?"  /  CONTESTED":"");
            label.color=b.interactable?ConquestCapturePoint.TeamColor(p.Owner):new Color(.4f,.4f,.4f);
        }
        squadMembers.Clear();
        if(rules.squads)foreach(var member in rules.squads.Members(participant))if(member!=participant)squadMembers.Add(member);
        for(int i=0;i<squadButtons.Length;i++)
        {
            var button=squadButtons[i];button.gameObject.SetActive(i<squadMembers.Count);
            if(i>=squadMembers.Count)continue;
            var member=squadMembers[i];string reason=rules.squads.UnavailableReason(participant,member);
            button.interactable=tacticalMap || reason=="";
            button.GetComponentInChildren<TMP_Text>().text=(selectedMember==member?"> ":"")+member.name.ToUpperInvariant()+(reason==""?"": " / "+reason);
        }
        if(rules.squads)equipmentLabel.text+="  /  "+ConquestSquads.SquadName(rules.squads.SquadOf(participant));
        bool ready=participant.RespawnRemaining<=0;
        deployButton.interactable=ready && !requested && (!sandbox || selected || sandbox.SelectedVehicle);
        if(!tacticalMap && deployButton.interactable && Input.GetKeyDown(KeyCode.Space))RequestDeployment();
        status.text=tacticalMap?"TACTICAL MAP / M OR ESC TO CLOSE":!ready?$"DEPLOY IN {Mathf.CeilToInt(participant.RespawnRemaining)}" : requested?"WAITING FOR A CLEAR SPAWN" : IsSandbox?"SELECT A CAPTURE POINT FOR INFANTRY / HQ FOR VEHICLES":"SELECT BASE, FRIENDLY FLAG, OR SAFE SQUADMATE";
        if(!tacticalMap && requested && ready && Time.time>=nextAttempt)
        {
            nextAttempt=Time.time+.5f;
            if(participant.match.Spawn(participant)){requested=false;redeploying=false;SetVisible(false);}
        }
    }
    void SetVisible(bool value)
    {
        if(value==visible)return;
        visible=value;panel.SetActive(value);
        if(value)
        {
            previousInputBlocked=input.InputBlocked;input.InputBlocked=true;
            if(spectator){previousSpectatorBlocked=spectator.ControlsBlocked;spectator.ControlsBlocked=true;}
        }
        else
        {
            // Results own the input lock once the match finishes.
            if(participant.match.IsRunning)
            {
                input.InputBlocked=previousInputBlocked;
                if(spectator)spectator.ControlsBlocked=previousSpectatorBlocked;
            }
            else restoreOnRestart=true;
            if(!pause.IsPaused && participant.match.IsRunning)
            {Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
        }
    }
    void OnDestroy() { participant.match.MatchStarted-=ResetSelection; }
}
