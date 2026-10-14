using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Presentation only. Works with the player already handed to ArenaHud by spectators.
public class ConquestHud : MonoBehaviour
{
    public ConquestRules rules;
    public ArenaHud hud;
    public RectTransform root;
    public TMP_Text[] flagLabels, worldLabels;
    public GameObject capturePanel;
    public TMP_Text captureLabel;
    public Image captureProgress;
    [Header("Objective presentation")]
    public Image[] flagBackgrounds = System.Array.Empty<Image>(), flagOwnershipBars = System.Array.Empty<Image>();
    public Image captureFilament;
    float nextRefresh;
    Camera viewingCamera;
    ArenaAISpectator spectator;
    int presentedPoints=-1;
    void Start(){spectator=FindAnyObjectByType<ArenaAISpectator>();EnsureObjectiveEntries();}
    void EnsureObjectiveEntries()
    {
        int count=rules.points.Length;
        if(presentedPoints==count)return;
        int authoredCount=flagLabels.Length;
        var labelTemplate=flagLabels[0];var worldTemplate=worldLabels[0];
        var backingTemplate=flagBackgrounds.Length>0?flagBackgrounds[0]:null;
        var barTemplate=flagOwnershipBars.Length>0?flagOwnershipBars[0]:null;
        System.Array.Resize(ref flagLabels,Mathf.Max(count,flagLabels.Length));
        System.Array.Resize(ref worldLabels,Mathf.Max(count,worldLabels.Length));
        System.Array.Resize(ref flagBackgrounds,Mathf.Max(count,flagBackgrounds.Length));
        System.Array.Resize(ref flagOwnershipBars,Mathf.Max(count,flagOwnershipBars.Length));
        for(int i=0;i<count;i++)
        {
            if(!flagLabels[i])flagLabels[i]=Instantiate(labelTemplate,labelTemplate.transform.parent);
            if(!worldLabels[i])worldLabels[i]=Instantiate(worldTemplate,worldTemplate.transform.parent);
            if(!flagBackgrounds[i]&&backingTemplate)
            {
                flagBackgrounds[i]=Instantiate(backingTemplate,backingTemplate.transform.parent);
                if(barTemplate)foreach(var image in flagBackgrounds[i].GetComponentsInChildren<Image>(true))
                    if(image.name==barTemplate.name){flagOwnershipBars[i]=image;break;}
            }
            // Keep larger modes readable without changing the five-point layout.
            if(count>authoredCount)
            {
                int columns=Mathf.Min(10,count), row=i/columns, rowCount=Mathf.Min(columns,count-row*columns);
                Vector2 position=labelTemplate.rectTransform.anchoredPosition;
                position.x=(i%columns-(rowCount-1)*.5f)*60;position.y=-140-row*42;
                flagLabels[i].rectTransform.anchoredPosition=position;
                if(flagBackgrounds[i])flagBackgrounds[i].rectTransform.anchoredPosition=position;
            }
            flagLabels[i].transform.SetAsLastSibling();
        }
        presentedPoints=count;
    }
    void LateUpdate()
    {
        EnsureObjectiveEntries();
        if(Time.unscaledTime>=nextRefresh)
        {
            nextRefresh=Time.unscaledTime+.15f;
            viewingCamera=null;
            var observed=hud.player;
            if(observed)
            {
                var occupant=observed.GetComponent<VehicleOccupant>();
                if(occupant && occupant.IsMounted && occupant.ActiveVehicleCamera && occupant.ActiveVehicleCamera.isActiveAndEnabled)
                    viewingCamera=occupant.ActiveVehicleCamera;
                else if(observed.player.look.aimCamera.isActiveAndEnabled)viewingCamera=observed.player.look.aimCamera;
            }
            if(!viewingCamera && spectator && spectator.spectatorCamera.isActiveAndEnabled)viewingCamera=spectator.spectatorCamera;
            for(int i=0;i<flagLabels.Length;i++)if(flagLabels[i])flagLabels[i].gameObject.SetActive(i<rules.points.Length&&hud.match.IsRunning);
            for(int i=0;i<rules.points.Length;i++)
            {
                var point=rules.points[i];
                flagLabels[i].text=point.letter+(point.IsContested?" !":"");
                flagLabels[i].color=point.Owner.HasValue?ConquestCapturePoint.TeamColor(point.Owner):new Color(.82f,.84f,.86f);
                if(i<flagOwnershipBars.Length && flagOwnershipBars[i])
                {
                    flagOwnershipBars[i].color=point.IsContested?hud.accent:flagLabels[i].color;
                    flagOwnershipBars[i].fillAmount=Mathf.Abs(point.Progress);
                    flagOwnershipBars[i].gameObject.SetActive(hud.match.IsRunning);
                }
            }
        }
        ConquestCapturePoint current=null;
        var actor=hud.player;
        if(actor && !actor.health.IsDead && !actor.IsAwaitingSpawn && hud.match.IsRunning)
            foreach(var point in rules.points)if(point.Contains(actor.transform.position)){current=point;break;}
        capturePanel.SetActive(current);
        for(int i=0;i<flagBackgrounds.Length;i++)
        {
            if(!flagBackgrounds[i])continue;
            bool active=i<rules.points.Length && rules.points[i]==current;
            flagBackgrounds[i].gameObject.SetActive(i<rules.points.Length && hud.match.IsRunning);
            flagBackgrounds[i].color=active?new Color(.16f,.10f,.035f,.86f):new Color(.018f,.018f,.02f,.62f);
        }
        if(current)
        {
            bool friendly=current.Owner==actor.team;
            bool opposing=current.Progress*(actor.team==ArenaParticipant.Team.Red?-1:1)<0;
            string status=current.IsContested?"CONTESTED":friendly?"DEFENDING":opposing?"NEUTRALIZING":"CAPTURING";
            captureLabel.text="<color=#FFAF00>"+current.letter+"</color>  "+current.displayName.ToUpperInvariant()+"\n<size=14>"+status+"  /  "+current.Count(actor.team)+" FRIENDLY</size>";
            if(captureFilament)captureFilament.color=new Color(1,1,1,current.IsContested?.72f:.46f);
            captureProgress.fillAmount=Mathf.Abs(current.Progress);
            captureProgress.color=ConquestCapturePoint.TeamColor(current.Progress<0?ArenaParticipant.Team.Red:ArenaParticipant.Team.Blue);
        }
        for(int i=0;i<rules.points.Length;i++)
        {
            var label=worldLabels[i];var point=rules.points[i];
            if(!viewingCamera || !hud.match.IsRunning){label.gameObject.SetActive(false);continue;}
            var screen=viewingCamera.WorldToScreenPoint(point.transform.position+Vector3.up*4);
            bool visible=screen.z>0 && screen.x>40 && screen.x<Screen.width-40 && screen.y>100 && screen.y<Screen.height-160;
            label.gameObject.SetActive(visible);
            if(!visible)continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,null,out var position);
            label.rectTransform.anchoredPosition=position;
            label.text=point.letter+"\n<size=13>"+Mathf.RoundToInt(Vector3.Distance(viewingCamera.transform.position,point.transform.position))+" m</size>";
            label.color=point.Owner.HasValue?ConquestCapturePoint.TeamColor(point.Owner):new Color(.82f,.84f,.86f);
        }
    }
}
