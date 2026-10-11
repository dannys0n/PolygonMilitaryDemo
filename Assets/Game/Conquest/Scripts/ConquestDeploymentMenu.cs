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
    public ArenaClassMenu classMenu;
    public ArenaAISpectator spectator;
    public GameObject panel;
    public Button baseButton, deployButton, classButton;
    public Button[] flagButtons;
    public TMP_Text status, className;
    ConquestCapturePoint selected;
    bool visible, requested, previousInputBlocked, previousSpectatorBlocked, restoreOnRestart;
    float nextAttempt;

    void Awake()
    {
        panel.SetActive(false);
        baseButton.onClick.AddListener(()=>Select(null));
        for(int i=0;i<flagButtons.Length;i++)
        {
            var point=rules.points[i];
            flagButtons[i].onClick.AddListener(()=>Select(point));
        }
        deployButton.onClick.AddListener(()=>{requested=true;nextAttempt=0;rules.ApproveDeployment(participant);});
        classButton.onClick.AddListener(()=>
        {
            requested=false;rules.CancelDeployment(participant);
            // Hand input ownership back before the class menu takes it.
            SetVisible(false);classMenu.Open();
        });
    }
    void Start() { participant.match.MatchStarted+=ResetSelection; }
    void ResetSelection()
    {
        requested=false;Select(null);
        if(visible)SetVisible(false);
        if(restoreOnRestart)
        {
            input.InputBlocked=previousInputBlocked;
            if(spectator)spectator.ControlsBlocked=previousSpectatorBlocked;
            restoreOnRestart=false;
        }
    }
    void Select(ConquestCapturePoint point) { selected=point;rules.SelectDeployment(participant,point); }
    void LateUpdate()
    {
        bool waiting=participant.health.IsDead || participant.IsAwaitingSpawn;
        if(!waiting)requested=false;
        bool show=participant.match.IsRunning && waiting && !participant.NeedsClassSelection && !pause.IsPaused && !classMenu.IsVisible;
        SetVisible(show);
        if(!show)return;
        if(!rules.CanDeploy(participant,selected))Select(null);
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        className.text=participant.SelectedClass.displayName;
        baseButton.GetComponentInChildren<TMP_Text>().text=(selected?"":"> ")+"TEAM BASE";
        for(int i=0;i<flagButtons.Length;i++)
        {
            var p=rules.points[i];var b=flagButtons[i];
            b.interactable=rules.CanDeploy(participant,p);
            var label=b.GetComponentInChildren<TMP_Text>();
            label.text=(selected==p?"> ":"")+p.letter+"  /  "+p.displayName.ToUpperInvariant()+(p.IsContested?"  /  CONTESTED":"");
            label.color=b.interactable?ConquestCapturePoint.TeamColor(p.Owner):new Color(.4f,.4f,.4f);
        }
        bool ready=participant.RespawnRemaining<=0;
        deployButton.interactable=ready && !requested;
        status.text=!ready?$"DEPLOY IN {Mathf.CeilToInt(participant.RespawnRemaining)}" : requested?"WAITING FOR A CLEAR SPAWN" : "SELECT A FRIENDLY FLAG OR YOUR TEAM BASE";
        if(requested && ready && Time.time>=nextAttempt)
        {
            nextAttempt=Time.time+.5f;
            if(participant.match.Spawn(participant)){requested=false;SetVisible(false);}
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
            if(!pause.IsPaused && !classMenu.IsVisible && participant.match.IsRunning)
            {Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
        }
    }
    void OnDestroy() { participant.match.MatchStarted-=ResetSelection; }
}
