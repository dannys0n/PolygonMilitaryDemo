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
    float nextRefresh;
    Camera viewingCamera;
    ArenaAISpectator spectator;
    void Start() => spectator=FindAnyObjectByType<ArenaAISpectator>();
    void LateUpdate()
    {
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
            foreach(var label in flagLabels)label.gameObject.SetActive(hud.match.IsRunning);
            for(int i=0;i<rules.points.Length;i++)
            {
                var point=rules.points[i];
                flagLabels[i].text=point.letter+(point.IsContested?" !":"");
                flagLabels[i].color=ConquestCapturePoint.TeamColor(point.Owner);
            }
        }
        ConquestCapturePoint current=null;
        var actor=hud.player;
        if(actor && !actor.health.IsDead && !actor.IsAwaitingSpawn && hud.match.IsRunning)
            foreach(var point in rules.points)if(point.Contains(actor.transform.position)){current=point;break;}
        capturePanel.SetActive(current);
        if(current)
        {
            bool friendly=current.Owner==actor.team;
            bool opposing=current.Progress*(actor.team==ArenaParticipant.Team.Red?-1:1)<0;
            captureLabel.text=current.letter+"  /  "+(current.IsContested?"CONTESTED":friendly?"DEFENDING":opposing?"NEUTRALIZING":"CAPTURING")+"  /  "+current.Count(actor.team);
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
            label.color=ConquestCapturePoint.TeamColor(point.Owner);
        }
    }
}
