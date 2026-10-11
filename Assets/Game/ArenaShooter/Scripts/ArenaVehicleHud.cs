using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(300)]
public class ArenaVehicleHud : MonoBehaviour
{
    public VehicleOccupant player;
    public TextMeshProUGUI prompt, status, controls, rightControls, crew, compass, telemetry, weapon;
    public GameObject mountedPanel, flightPanel;
    public GameObject infantryEquipment, infantryCrosshair;
    public Image diagram;
    public Sprite humveeDiagram, helicopterDiagram;
    public RectTransform horizon, desiredAim, actualAim;
    public VehicleDiagramGraphic headingTape, altitudeTape;
    public GameObject vehicleEquipment;
    public Image equippedIcon, slotIcon;
    public TMP_Text equippedName, ammunition, fireMode, slotKey, equipmentLabel;
    public Image[] seatMarkers, hullSegments;
    public TextMeshProUGUI[] seatNumbers;
    float groundAltitude;
    readonly Color accent=new Color(1,.68f,0);
    void Update()
    {
        bool mounted=player.IsMounted;prompt.text=mounted?"":player.Prompt;
        mountedPanel.SetActive(mounted);flightPanel.SetActive(false);
        infantryEquipment.SetActive(!mounted);infantryCrosshair.SetActive(!mounted);
        vehicleEquipment.SetActive(false);
        if(!mounted)return;
        var v=player.MountedVehicle;bool air=v.motor is HelicopterMotor;var role=v.seats[player.SeatIndex].role;
        diagram.sprite = v.seatDiagram ? v.seatDiagram : air ? helicopterDiagram : humveeDiagram;
        bool armed = (role == Vehicle.SeatRole.Gunner && v.turret) || (air && role == Vehicle.SeatRole.Driver && v.GetComponent<VehicleLauncher>());
        vehicleEquipment.SetActive(armed);
        if (armed)
        {
            bool gunner = role == Vehicle.SeatRole.Gunner;
            var launcher = air && !gunner ? v.GetComponent<VehicleLauncher>() : null;
            equippedIcon.sprite = slotIcon.sprite = gunner ? v.turret.weaponIcon : launcher.weaponIcon;
            equippedName.text = gunner ? "MINIGUN" : "ROCKETS"; ammunition.text = gunner ? "INF" : launcher.Missiles + " / " + launcher.capacity;
            fireMode.text = gunner ? "AUTO" : launcher.ReadyIn <= 0 ? "READY" : launcher.ReadyIn.ToString("F1") + " S";
            slotKey.text = "LMB"; equipmentLabel.text = "VEHICLE WEAPON";
        }
        status.text=v.displayName.ToUpperInvariant()+" / "+v.seats[player.SeatIndex].name.ToUpperInvariant()+"    "+Mathf.RoundToInt(v.Body.linearVelocity.magnitude*3.6f)+" KPH";
        if (player.HornFeedback>0) status.text+="  HORN";
        controls.text=role==Vehicle.SeatRole.Driver?(air?"CLIMB  [SPACE]\nDESCEND  [CTRL]\nMOVE  [WASD]\nAIM ALIGN  [RMB]":"BRAKE / REV  [S]\nACCELERATE  [W]\nBOOST  [SHIFT]\nHANDBRAKE  [Z]\nHORN  [H]"):role==Vehicle.SeatRole.Gunner?"FIRE  [LMB]\nZOOM  [RMB]\nAMMO  INF":"PASSENGER";
        if (air && role == Vehicle.SeatRole.Driver) controls.text += "\n[T] ASSIST " + (((HelicopterMotor)v.motor).assistEnabled ? "ON" : "OFF");
        rightControls.text="[C]  CAMERA\n[V]  FREELOOK"+(player.Freelook?" ON":"")+"\nHOLD [E]  EXIT\n[F1-F"+v.seats.Length+"]  SEATS";
        EnsureSeatMarkers(v.seats.Length);
        crew.fontSize = v.seats.Length > 5 ? 13 : 18;
        crew.text="";
        for(int i=0;i<seatMarkers.Length;i++)
        {
            bool exists=i<v.seats.Length;seatMarkers[i].gameObject.SetActive(exists);if(!exists)continue;
            var seat=v.seats[i];bool occupied=seat.occupant;
            seatMarkers[i].rectTransform.anchoredPosition=v.seatDiagram?seat.diagramPosition:air?new Vector2(0,i==0?26:54):i==1?new Vector2(0,-7):new Vector2(i==0||i==3?-15:15,i<3?20:-30);
            seatMarkers[i].color=i==player.SeatIndex?accent:occupied?Color.white:new Color(1,1,1,.18f);
            seatNumbers[i].color=i==player.SeatIndex||occupied?Color.black:Color.white;
            string name=occupied?(seat.occupant==player?"YOU":seat.occupant.name):"EMPTY";
            crew.text+="<color="+(i==player.SeatIndex?"#FFAE00":"#FFFFFF")+">"+(i+1)+"  "+name+"</color>  <size=12>"+seat.name.ToUpperInvariant()+"</size>\n";
        }
        float health=v.Health/v.maxHealth;for(int i=0;i<hullSegments.Length;i++)hullSegments[i].color=i<hullSegments.Length*health?Color.white:new Color(1,1,1,.15f);
        if(!air)return;
        flightPanel.SetActive(role==Vehicle.SeatRole.Driver);
        float altitude=0, nearest=float.PositiveInfinity;
        foreach(var hit in Physics.RaycastAll(v.transform.position,Vector3.down,1000,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(v.transform)&&!hit.collider.GetComponentInParent<SoldierLife>()&&hit.distance<nearest){nearest=hit.distance;altitude=hit.distance;}
        telemetry.text=Mathf.RoundToInt(v.Body.linearVelocity.magnitude*3.6f)+" KPH    ALT "+altitude.ToString("F0")+" M    V/S "+v.Body.linearVelocity.y.ToString("+0.0;-0.0;0.0");
        groundAltitude = altitude;
        var rockets=v.GetComponent<VehicleLauncher>();
        weapon.text=rockets?"RKT  [LMB]  "+rockets.Missiles+" / "+rockets.capacity+(rockets.Missiles<rockets.capacity ? "   +1 IN "+rockets.RechargeRemaining.ToString("F1")+" S" : ""):"TRANSPORT / NO PILOT WEAPON";
    }
    void LateUpdate()
    {
        if (player.IsMounted && player.MountedVehicle.motor is HelicopterMotor && player.MountedVehicle.seats[player.SeatIndex].role == Vehicle.SeatRole.Driver)
            UpdateFlightInstruments(player.MountedVehicle, groundAltitude);
        bool armed = player.IsMounted && vehicleEquipment.activeSelf;
        desiredAim.gameObject.SetActive(armed); actualAim.gameObject.SetActive(armed);
        if (!armed) return;
        var vehicle = player.MountedVehicle; Camera camera = player.ActiveVehicleCamera;
        bool gunner = vehicle.seats[player.SeatIndex].role == Vehicle.SeatRole.Gunner;
        actualAim.GetComponent<Graphic>().canvasRenderer.SetAlpha(gunner || vehicle.GetComponent<VehicleLauncher>().Missiles > 0 ? 1f : .2f);

        Vector3 requested; Ray actual; float range;
        if (gunner)
        {
            requested = vehicle.turret.RequestedAimDirection;
            actual = new Ray(vehicle.turret.muzzle.position,vehicle.turret.muzzle.forward); range = vehicle.turret.range;
        }
        else
        {
            var launcher = vehicle.GetComponent<VehicleLauncher>();
            requested = ((HelicopterMotor)vehicle.motor).RequestedAttitude * Vector3.forward;
            actual = launcher.NextShotRay; range = 180;
        }
        if (!player.Freelook) requested = camera.transform.forward;
        Vector3 desiredPoint=ResolvePoint(new Ray(camera.transform.position,requested),range,vehicle);
        PlaceMarker(desiredAim,camera,desiredPoint);
        // Rockets predict their own impact over their lifetime, independently of the camera target distance.
        Vector3 impact=gunner
            ? ResolvePoint(actual,Mathf.Min(range,Vector3.Distance(actual.origin,desiredPoint)),vehicle)
            : vehicle.GetComponent<VehicleLauncher>().PredictedImpactPoint;
        PlaceMarker(actualAim,camera,impact);
    }
    void EnsureSeatMarkers(int count)
    {
        int previous = seatMarkers.Length;
        if (count <= previous) return;
        System.Array.Resize(ref seatMarkers, count);
        System.Array.Resize(ref seatNumbers, count);
        for (int i = previous; i < count; i++)
        {
            seatMarkers[i] = Instantiate(seatMarkers[0], seatMarkers[0].transform.parent);
            seatMarkers[i].name = "Seat " + (i + 1);
            seatNumbers[i] = seatMarkers[i].GetComponentInChildren<TextMeshProUGUI>();
            seatNumbers[i].text = (i + 1).ToString();
        }
    }
    void UpdateFlightInstruments(Vehicle vehicle, float altitude)
    {
        // Flight instruments describe aircraft attitude, independent of the pilot camera.
        Transform view = vehicle.transform;
        float heading = Mathf.Repeat(view.eulerAngles.y,360);
        compass.text = "HDG " + Mathf.RoundToInt(heading).ToString("000") + "  " + Cardinal(heading);
        float elevation = Mathf.Asin(Mathf.Clamp(view.forward.y,-1,1)) * Mathf.Rad2Deg;
        Vector3 up = view.InverseTransformDirection(Vector3.up);
        float bank = Mathf.Atan2(up.x,up.y) * Mathf.Rad2Deg;
        horizon.localEulerAngles = new Vector3(0,0,-bank);
        horizon.anchoredPosition = (Quaternion.Euler(0,0,-bank)*new Vector3(0,-elevation*2f,0));
        headingTape.SetReading(heading);
        altitudeTape.SetReading(altitude);
    }
    static Vector3 ResolvePoint(Ray ray,float range,Vehicle owner)
    {
        float nearest=range;
        foreach(var hit in Physics.RaycastAll(ray,range,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide))
        {
            if(hit.transform.IsChildOf(owner.transform)||hit.collider is CharacterController||hit.collider.GetComponentInParent<VehicleRocket>())continue;
            if(hit.collider.isTrigger&&!hit.collider.GetComponent<SoldierHitRegion>())continue;
            nearest=Mathf.Min(nearest,hit.distance);
        }
        return ray.GetPoint(nearest);
    }
    static void PlaceMarker(RectTransform marker,Camera camera,Vector3 point)
    {
        Vector3 screen=camera.WorldToScreenPoint(point);bool visible=screen.z>0;
        marker.gameObject.SetActive(visible);if(!visible)return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)marker.parent,screen,null,out var local);
        marker.anchoredPosition=local;
    }
    static string Cardinal(float a){string[] directions={"N","NE","E","SE","S","SW","W","NW"};return directions[Mathf.RoundToInt(a/45)%8];}
}
