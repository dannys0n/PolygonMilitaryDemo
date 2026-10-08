using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

[DefaultExecutionOrder(-40)]
public class ArenaScoreboard : MonoBehaviour
{
    public ArenaMatch match;
    public ArenaParticipant localPlayer;
    public GameObject panel;
    public TMP_Text matchInfo, redHeader, blueHeader;
    public RectTransform redContent, blueContent, rowTemplate;
    public Color accent = new Color(1,.686f,0);
    public bool IsVisible { get; private set; }
    readonly List<RectTransform> redRows = new List<RectTransform>(), blueRows = new List<RectTransform>();
    SoldierLocalInput localInput;
    ArenaAISpectator spectator;
    bool inputWasBlocked, spectatorWasBlocked, previousVisible;
    CursorLockMode previousCursor;
    float nextRefresh;

    void Awake()
    {
        localInput = FindAnyObjectByType<SoldierLocalInput>();
        spectator = FindAnyObjectByType<ArenaAISpectator>();
        rowTemplate.gameObject.SetActive(false); panel.SetActive(false);
    }
    void Update()
    {
        SetVisible(Input.GetKey(KeyCode.Tab));
        if (!IsVisible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .25f;
        RefreshRows();
    }
    void LateUpdate() { if (IsVisible) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }
    public void SetVisible(bool visible)
    {
        if (visible == IsVisible) return;
        IsVisible = visible; panel.SetActive(visible);
        if (visible)
        {
            previousCursor = Cursor.lockState; previousVisible = Cursor.visible;
            if (localInput) { inputWasBlocked = localInput.InputBlocked; localInput.InputBlocked = true; }
            if (spectator) { spectatorWasBlocked = spectator.ControlsBlocked; spectator.ControlsBlocked = true; }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            nextRefresh = 0f; RefreshRows();
        }
        else
        {
            if (localInput) localInput.InputBlocked = inputWasBlocked;
            if (spectator) spectator.ControlsBlocked = spectatorWasBlocked;
            Cursor.lockState = previousCursor; Cursor.visible = previousVisible;
        }
    }
    void OnDisable() => SetVisible(false);
    public void RefreshRows()
    {
        int seconds = Mathf.CeilToInt(match.TimeRemaining);
        matchInfo.text = $"TEAM DEATHMATCH  /  {match.RedScore} : {match.BlueScore}  /  {seconds/60:00}:{seconds%60:00}";
        UpdateTeam(ArenaParticipant.Team.Red,redContent,redRows,redHeader);
        UpdateTeam(ArenaParticipant.Team.Blue,blueContent,blueRows,blueHeader);
    }
    void UpdateTeam(ArenaParticipant.Team team,RectTransform content,List<RectTransform> rows,TMP_Text header)
    {
        var actors = match.participants.Where(p=>p.team==team).OrderByDescending(p=>p.Score).ThenBy(p=>p.Deaths).ThenBy(p=>p.name).ToArray();
        header.text = $"{team.ToString().ToUpperInvariant()} TEAM   |   {actors.Length} PLAYERS";
        while(rows.Count<actors.Length) {var row=Instantiate(rowTemplate,content,false);rows.Add(row);}
        for(int i=0;i<rows.Count;i++)
        {
            rows[i].gameObject.SetActive(i<actors.Length); if(i>=actors.Length)continue;
            var actor=actors[i];var row=rows[i];
            bool highlighted=actor==localPlayer || !localPlayer && spectator && actor==spectator.FollowedCharacter;
            row.GetComponent<UnityEngine.UI.Image>().color=highlighted?new Color(accent.r*.17f,accent.g*.17f,accent.b*.17f,.82f):new Color(.025f,.035f,.045f,i%2==0?.68f:.54f);
            row.Find("Rank").GetComponent<TMP_Text>().text=(i+1).ToString("00");
            var name=row.Find("Player").GetComponent<TMP_Text>();name.text=actor.name.Replace("<","").Replace(">","");name.color=highlighted?accent:Color.white;
            row.Find("Score").GetComponent<TMP_Text>().text=actor.Score.ToString("N0");
            row.Find("Kills").GetComponent<TMP_Text>().text=actor.Kills.ToString();
            row.Find("Deaths").GetComponent<TMP_Text>().text=actor.Deaths.ToString();
            row.Find("Status").GetComponent<TMP_Text>().text=actor.health.IsDead?"DOWN":"--";
        }
    }
}
