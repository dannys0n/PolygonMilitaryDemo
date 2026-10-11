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
    public TMP_Text matchInfo, redHeader, blueHeader, instructions;
    public RectTransform redContent, blueContent, rowTemplate;
    public Material normalText, highlightedText;
    public Color accent = new Color(1,.686f,0);
    public bool IsVisible { get; private set; }
    public bool ResultsVisible { get; private set; }
    readonly List<RectTransform> redRows = new List<RectTransform>(), blueRows = new List<RectTransform>();
    ArenaAISpectator spectator;
    float nextRefresh;

    void Awake()
    {
        spectator = FindAnyObjectByType<ArenaAISpectator>();
        rowTemplate.gameObject.SetActive(false); panel.SetActive(false);
    }
    void Update()
    {
        SetVisible(ResultsVisible || Input.GetKey(KeyCode.Tab));
        if (!IsVisible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .25f;
        RefreshRows();
    }
    public void SetResultsVisible(bool visible)
    {
        ResultsVisible = visible;
        SetVisible(visible);
    }
    public void SetVisible(bool visible)
    {
        if (visible == IsVisible) return;
        IsVisible = visible;
        panel.SetActive(visible);
        if (visible) { nextRefresh = 0f; RefreshRows(); }
    }
    void OnDisable() => SetVisible(false);
    public void RefreshRows()
    {
        instructions.text = ResultsVisible ? "MATCH COMPLETE  /  FINAL STANDINGS" : (match.rules ? "HOLD TAB TO VIEW  /  100 PER KILL  /  200 PER CAPTURE" : "HOLD TAB TO VIEW  /  SCROLL EACH TEAM  /  100 POINTS PER ENEMY KILL");
        int seconds = Mathf.CeilToInt(match.TimeRemaining);
        matchInfo.text = $"{match.DisplayName}  /  {match.RedScore} : {match.BlueScore}  /  {seconds/60:00}:{seconds%60:00}";
        UpdateTeam(ArenaParticipant.Team.Red,redContent,redRows,redHeader);
        UpdateTeam(ArenaParticipant.Team.Blue,blueContent,blueRows,blueHeader);
        var presence = GetComponent<ArenaUiPresence>();
        if (presence) presence.RefreshGraphics();
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
            row.GetComponent<UnityEngine.UI.Image>().color=new Color(1,1,1,highlighted?.075f:i%2==0?.025f:.012f);
            row.Find("Rank").GetComponent<TMP_Text>().text=(i+1).ToString("00");
            var name=row.Find("Player").GetComponent<TMP_Text>();name.text=actor.name.Replace("<","").Replace(">","");
            name.color=highlighted?Color.white:new Color(.85f,.85f,.85f);
            var material=highlighted?highlightedText:normalText;
            if(name.fontSharedMaterial!=material){name.fontSharedMaterial=material;name.UpdateMeshPadding();}
            row.Find("Score").GetComponent<TMP_Text>().text=actor.Score.ToString("N0");
            row.Find("Kills").GetComponent<TMP_Text>().text=actor.Kills.ToString();
            row.Find("Deaths").GetComponent<TMP_Text>().text=actor.Deaths.ToString();
            // Ping will be supplied by the networking backend; this demo is local.
            row.Find("Ping").GetComponent<TMP_Text>().text="—";
        }
    }
}
