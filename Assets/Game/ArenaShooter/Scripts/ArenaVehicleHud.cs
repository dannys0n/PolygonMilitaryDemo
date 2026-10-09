using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ArenaVehicleHud : MonoBehaviour
{
    public VehicleOccupant player;
    public TextMeshProUGUI prompt, status, controls, rightControls, crew, compass, telemetry, weapon;
    public GameObject mountedPanel, flightPanel;
    public GameObject infantryEquipment, infantryCrosshair;
    public Image diagram;
    public Sprite humveeDiagram, helicopterDiagram;
    public RectTransform horizon;
    public GameObject vehicleEquipment;
    public Image equippedIcon, slotIcon;
    public TMP_Text equippedName, ammunition, fireMode, slotKey, equipmentLabel;
    public Image[] seatMarkers, hullSegments;
    public TextMeshProUGUI[] seatNumbers;
    readonly Color accent=new Color(1,.68f,0);
    void Update()
    {
        bool mounted=player.IsMounted;prompt.text=mounted?"":player.Prompt;
        mountedPanel.SetActive(mounted);flightPanel.SetActive(false);
        infantryEquipment.SetActive(!mounted);infantryCrosshair.SetActive(!mounted||player.MountedVehicle.seats[player.SeatIndex].role==Vehicle.SeatRole.Gunner);
        vehicleEquipment.SetActive(false);
        if(!mounted)return;
        var v=player.MountedVehicle;bool air=v.motor is HelicopterMotor;var role=v.seats[player.SeatIndex].role;
        diagram.sprite = air ? helicopterDiagram : humveeDiagram;
        bool armed = role == Vehicle.SeatRole.Gunner || (air && role == Vehicle.SeatRole.Driver);
        vehicleEquipment.SetActive(armed);
        if (armed)
        {
            bool gunner = role == Vehicle.SeatRole.Gunner;
            var launcher = air && !gunner ? v.GetComponent<VehicleLauncher>() : null;
            equippedIcon.sprite = slotIcon.sprite = gunner ? v.turret.weaponIcon : launcher.weaponIcon;
            equippedName.text = gunner ? "MINIGUN" : "ROCKETS"; ammunition.text = "INF";
            fireMode.text = gunner ? "AUTO" : launcher.ReadyIn <= 0 ? "READY" : launcher.ReadyIn.ToString("F1") + " S";
            slotKey.text = "LMB"; equipmentLabel.text = "VEHICLE WEAPON";
        }
        status.text=v.displayName.ToUpperInvariant()+" / "+v.seats[player.SeatIndex].name.ToUpperInvariant()+"    "+Mathf.RoundToInt(v.Body.linearVelocity.magnitude*3.6f)+" KPH";
        if (player.HornFeedback>0) status.text+="  HORN";
        controls.text=role==Vehicle.SeatRole.Driver?(air?"CLIMB  [SPACE]\nDESCEND  [CTRL]\nMOVE  [WASD]":"BRAKE / REV  [S]\nACCELERATE  [W]\nBOOST  [SHIFT]\nHANDBRAKE  [Z]\nHORN  [H]"):"FIRE  [LMB]\nZOOM  [RMB]\nAMMO  INF";
        rightControls.text="[C]  CAMERA\n[V]  FREELOOK"+(player.Freelook?" ON":"")+"\nHOLD [E]  EXIT\n[F1-F"+v.seats.Length+"]  SEATS";
        crew.text="";
        for(int i=0;i<seatMarkers.Length;i++)
        {
            bool exists=i<v.seats.Length;seatMarkers[i].gameObject.SetActive(exists);if(!exists)continue;
            var seat=v.seats[i];bool occupied=seat.occupant;
            seatMarkers[i].rectTransform.anchoredPosition=air?new Vector2(0,i==0?26:54):i==1?new Vector2(0,-7):new Vector2(i==0||i==3?-15:15,i<3?20:-30);
            seatMarkers[i].color=i==player.SeatIndex?accent:occupied?Color.white:new Color(1,1,1,.18f);
            seatNumbers[i].color=i==player.SeatIndex||occupied?Color.black:Color.white;
            string name=occupied?(seat.occupant==player?"YOU":seat.occupant.name):"EMPTY";
            crew.text+="<color="+(i==player.SeatIndex?"#FFAE00":"#FFFFFF")+">"+(i+1)+"  "+name+"</color>  <size=12>"+seat.name.ToUpperInvariant()+"</size>\n";
        }
        float health=v.Health/v.maxHealth;for(int i=0;i<hullSegments.Length;i++)hullSegments[i].color=i<hullSegments.Length*health?Color.white:new Color(1,1,1,.15f);
        if(!air)return;
        flightPanel.SetActive(role==Vehicle.SeatRole.Driver);
        float heading=Mathf.Repeat(player.Freelook ? player.ViewYaw : v.transform.eulerAngles.y,360);compass.text="HDG  "+Mathf.RoundToInt(heading).ToString("000")+"    "+Cardinal(heading);
        float altitude=0, nearest=float.PositiveInfinity;
        foreach(var hit in Physics.RaycastAll(v.transform.position,Vector3.down,1000,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(v.transform)&&!hit.collider.GetComponentInParent<SoldierLife>()&&hit.distance<nearest){nearest=hit.distance;altitude=hit.distance;}
        telemetry.text=Mathf.RoundToInt(v.Body.linearVelocity.magnitude*3.6f)+" KPH    ALT "+altitude.ToString("F0")+" M    V/S "+v.Body.linearVelocity.y.ToString("+0.0;-0.0;0.0");
        var motor=(HelicopterMotor)v.motor;Transform frame=motor.airframe;
        horizon.localEulerAngles=new Vector3(0,0,-Mathf.DeltaAngle(0,frame.eulerAngles.z));horizon.anchoredPosition=new Vector2(0,-Mathf.Clamp(Mathf.DeltaAngle(0,frame.eulerAngles.x),-40,40)*2);
        weapon.text="RKT  [LMB]     "+(v.GetComponent<VehicleLauncher>().ReadyIn<=0?"RDY":v.GetComponent<VehicleLauncher>().ReadyIn.ToString("F1")+" S");
    }
    static string Cardinal(float a){string[] directions={"N","NE","E","SE","S","SW","W","NW"};return directions[Mathf.RoundToInt(a/45)%8];}
}
