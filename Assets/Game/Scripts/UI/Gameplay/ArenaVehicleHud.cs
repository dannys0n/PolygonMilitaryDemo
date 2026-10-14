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
    public Material selectedIconGlow, filamentLine, accentLineGlow;
    public Material externalMonitorMaterial;
    public TMP_Text equippedName, ammunition, fireMode, slotKey, equipmentLabel;
    public Image[] seatMarkers, hullSegments;
    public TextMeshProUGUI[] seatNumbers;
    [Header("Vehicle instruments")]
    public VehicleDiagramGraphic speedTape, bankScale, verticalSpeedIndicator;
    public TMP_Text speedReadout, altitudeReadout, systemsReadout, gunnerReadout;
    public RectTransform flightPathMarker;
    float groundAltitude;
    CanvasGroup mountedFade;
    float presentationBlend;
    GameObject roadPanel, armoredPanel, dronePanel;
    TMP_Text armoredSpeed, armoredState, armoredHeading, armoredAttitude, droneTelemetry, droneHeading;
    VehicleDiagramGraphic armoredLevel;
    VehicleSpeedometerGraphic roadGauge;
    VehicleDialGraphic roadDial;
    TMP_Text[] roadTicks;
    VehicleSeatDiagramGraphic seatDiagramLinks;
    ArenaVehicleAttackHud attackHud;
    ArenaVehicleLockHud lockHud;
    VehicleReferenceInstruments referenceInstruments;
    VehicleFixedWingReadout fixedWingReadout;
    VehicleDiagramGraphic hoverCue, metricPitchScale;
    TMP_Text metricPitchReadout;
    TMP_Text roadSpeed, roadDetails;
    readonly Color accent=new Color(1,.55f,.12f);
    readonly System.Collections.Generic.List<RectTransform> systemTiles=new System.Collections.Generic.List<RectTransform>();
    readonly System.Collections.Generic.List<Image> systemIcons=new System.Collections.Generic.List<Image>();
    readonly System.Collections.Generic.List<TMP_Text> systemKeys=new System.Collections.Generic.List<TMP_Text>();
    readonly System.Collections.Generic.List<TMP_Text> systemCaptions=new System.Collections.Generic.List<TMP_Text>();
    bool seatHasWeapon;
    Vehicle radarStatusVehicle;
    bool radarWasLinked;
    float radarStatusChangedAt = float.NegativeInfinity;
    Vehicle displayedSystemsVehicle;
    Vehicle.SeatRole displayedSystemsRole;
    VehicleCountermeasures displayedDefenses;
    readonly System.Collections.Generic.List<VehicleWeaponSystems.Weapon> seatSystems=new System.Collections.Generic.List<VehicleWeaponSystems.Weapon>();
    void Awake()
    {
        ArenaHudTheme.StyleLines(transform, filamentLine, accentLineGlow);
        // Ammunition and weapon identity belong to the shared bottom-right equipment card.
        weapon.gameObject.SetActive(false);
        // Keep the lower equipment row clear even when the centered seat hints reach it.
        controls.rectTransform.anchoredPosition+=new Vector2(0,16);
        rightControls.rectTransform.anchoredPosition+=new Vector2(0,16);
        crew.gameObject.SetActive(false);
        controls.rectTransform.anchoredPosition=new Vector2(-230,16);
        controls.rectTransform.sizeDelta=new Vector2(260,150);
        controls.fontSize=14;
        rightControls.rectTransform.anchoredPosition=new Vector2(210,16);
        rightControls.rectTransform.sizeDelta=new Vector2(210,140);
        rightControls.fontSize=14;
        status.rectTransform.anchoredPosition=new Vector2(0,110);
        diagram.rectTransform.sizeDelta=new Vector2(160,160);diagram.preserveAspect=true;
        var links=new GameObject("Seat diagram callouts",typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleSeatDiagramGraphic));links.transform.SetParent(diagram.transform,false);links.transform.SetAsFirstSibling();seatDiagramLinks=links.GetComponent<VehicleSeatDiagramGraphic>();seatDiagramLinks.rectTransform.sizeDelta=new Vector2(160,160);seatDiagramLinks.raycastTarget=false;
        BuildRoadDashboard();
        BuildSpecializedInstruments();
        referenceInstruments=gameObject.AddComponent<VehicleReferenceInstruments>();referenceInstruments.Build(telemetry);
        fixedWingReadout=gameObject.AddComponent<VehicleFixedWingReadout>();fixedWingReadout.showAirData=false;fixedWingReadout.Build(telemetry);
        var hover=new GameObject("Apache hover velocity cue",typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleDiagramGraphic));hover.transform.SetParent(flightPanel.transform,false);hoverCue=hover.GetComponent<VehicleDiagramGraphic>();hoverCue.drawing=VehicleDiagramGraphic.Drawing.HoverCue;hoverCue.rectTransform.sizeDelta=new Vector2(110,110);hoverCue.raycastTarget=false;hover.SetActive(false);
        var pitchScale=new GameObject("Su27 right pitch scale",typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleDiagramGraphic));pitchScale.transform.SetParent(flightPanel.transform,false);metricPitchScale=pitchScale.GetComponent<VehicleDiagramGraphic>();metricPitchScale.drawing=VehicleDiagramGraphic.Drawing.PitchScale;metricPitchScale.rectTransform.anchoredPosition=new Vector2(150,0);metricPitchScale.rectTransform.sizeDelta=new Vector2(25,170);metricPitchScale.tickStep=10;metricPitchScale.pixelsPerUnit=2;metricPitchScale.raycastTarget=false;metricPitchScale.tickLabels=new TMP_Text[5];for(int i=0;i<5;i++)metricPitchScale.tickLabels[i]=DashboardLabel("Pitch tick "+i,metricPitchScale.transform,Vector2.zero,new Vector2(35,20),12);metricPitchReadout=DashboardLabel("Metric pitch",flightPanel.transform,new Vector2(160,-100),new Vector2(140,25),14);pitchScale.SetActive(false);metricPitchReadout.gameObject.SetActive(false);
        attackHud=gameObject.AddComponent<ArenaVehicleAttackHud>();attackHud.Build(telemetry);
        lockHud=gameObject.AddComponent<ArenaVehicleLockHud>();lockHud.Build(telemetry);
        mountedFade=mountedPanel.GetComponent<CanvasGroup>();
        var externalMonitor=gameObject.AddComponent<VehicleExternalViewMonitor>();
        externalMonitor.Build(this,telemetry,externalMonitorMaterial);
    }
    void Update()
    {
        if(!player.IsMounted || !VehicleReferenceInstruments.UsesDials(player.MountedVehicle) || !ShowVehicleInstruments(player.MountedVehicle))referenceInstruments.Present(null,0,false,false);
        bool mounted=player.IsMounted;prompt.text=mounted?"":player.Prompt;
        bool jetInstruments=mounted && player.MountedVehicle.motor is JetMotor && ShowVehicleInstruments(player.MountedVehicle);
        fixedWingReadout.Present(mounted?player.MountedVehicle:null,jetInstruments);
        telemetry.gameObject.SetActive(!jetInstruments);systemsReadout.gameObject.SetActive(!jetInstruments);
        mountedPanel.SetActive(mounted);
        infantryEquipment.SetActive(!mounted);infantryCrosshair.SetActive(!mounted);
        if(!mounted){flightPanel.SetActive(false);vehicleEquipment.SetActive(false);roadPanel.SetActive(false);armoredPanel.SetActive(false);dronePanel.SetActive(false);presentationBlend=0;displayedSystemsVehicle=null;seatHasWeapon=false;return;}
        presentationBlend=Mathf.MoveTowards(presentationBlend,1,Time.deltaTime/.18f);
        if(mountedFade)mountedFade.alpha=1-Mathf.Pow(1-presentationBlend,3);
        var v=player.MountedVehicle;bool air=v.IsAircraft;var role=v.seats[player.SeatIndex].role;
        diagram.sprite = v.seatDiagram ? v.seatDiagram : air ? helicopterDiagram : humveeDiagram;
        seatDiagramLinks.SetVehicle(v,player.SeatIndex,player.SeatPage);
        var attackSight=v.GetComponent<VehicleAttackSight>();bool showAttack=attackSight&&attackSight.DisplaysForSeat(player.SeatIndex);
        var weaponSystems=v.GetComponent<VehicleWeaponSystems>();
        var selectedWeapon=weaponSystems?weaponSystems.Current(player):null;
        bool armed = weaponSystems?selectedWeapon!=null:showAttack || (role == Vehicle.SeatRole.Gunner && v.turret) || (air && role == Vehicle.SeatRole.Driver && v.GetComponent<VehicleLauncher>());
        seatHasWeapon=armed;
        vehicleEquipment.SetActive(armed);
        if(selectedWeapon!=null)
        {
            equippedIcon.sprite=slotIcon.sprite=selectedWeapon.icon;
            equippedIcon.color=slotIcon.color=Color.white;
            if(selectedIconGlow)equippedIcon.material=slotIcon.material=selectedIconGlow;
            equippedName.text=selectedWeapon.name;
            var rack=selectedWeapon.launcher;
            float delay=selectedWeapon.ReadyIn;
            ammunition.text=selectedWeapon.system==VehicleWeaponSystems.FireSystem.Launcher?rack.Missiles+" / "+rack.capacity
                :selectedWeapon.IsSingleShot?(delay>0?"0 / 1":"1 / 1"):"INF";
            fireMode.text=delay>0?(selectedWeapon.IsSingleShot?"RELOAD ":"")+delay.ToString("F1")+" S":"READY";
            if(selectedWeapon.requiresRadarSupport)fireMode.text=weaponSystems.SupportStatus(selectedWeapon);
            slotKey.text=weaponSystems.IsAvailable(selectedWeapon)?"LMB":"";equipmentLabel.text="VEHICLE WEAPON";
        }
        else if(showAttack)
        {
            equippedIcon.sprite=slotIcon.sprite=attackSight.weaponIcon;equippedIcon.color=slotIcon.color=Color.white;
            if(selectedIconGlow)equippedIcon.material=slotIcon.material=selectedIconGlow;
            var attackLauncher=v.GetComponent<VehicleLauncher>();
            equippedName.text=attackSight.weaponName;
            bool single=v.turret && v.turret.IsSingleShot;
            float delay=attackLauncher?attackLauncher.ReadyIn:v.turret?v.turret.ReadyIn:0;
            ammunition.text=attackLauncher?attackLauncher.Missiles+" / "+attackLauncher.capacity:single?(delay>0?"0 / 1":"1 / 1"):attackSight.Operational?"INF":"--";
            fireMode.text=!attackSight.Operational?"SAFE":delay>0?(single?"RELOAD ":"")+delay.ToString("F1")+" S":single?"READY":"AUTO";
            slotKey.text=attackSight.Operational?"LMB":"";equipmentLabel.text="VEHICLE WEAPON";
        }
        else if (armed)
        {
            bool gunner = role == Vehicle.SeatRole.Gunner;
            var launcher = air && !gunner ? v.GetComponent<VehicleLauncher>() : null;
            equippedIcon.sprite = slotIcon.sprite = gunner ? v.turret.weaponIcon : launcher.weaponIcon;
            equippedIcon.color = slotIcon.color = Color.white;
            if (selectedIconGlow) equippedIcon.material = slotIcon.material = selectedIconGlow;
            equippedName.text = gunner ? v.turret.weaponName : "ROCKETS";
            ammunition.text = gunner ? v.turret.IsSingleShot?(v.turret.ReadyIn>0?"0 / 1":"1 / 1"):"INF" : launcher.Missiles + " / " + launcher.capacity;
            fireMode.text = gunner ? v.turret.IsSingleShot?(v.turret.ReadyIn>0?"RELOAD "+v.turret.ReadyIn.ToString("F1")+" S":"READY"):"AUTO"
                : launcher.ReadyIn <= 0 ? "READY" : launcher.ReadyIn.ToString("F1") + " S";
            slotKey.text = "LMB"; equipmentLabel.text = "VEHICLE WEAPON";
        }
        status.text=v.displayName.ToUpperInvariant()+" / "+v.seats[player.SeatIndex].name.ToUpperInvariant()+"    "+Mathf.RoundToInt(v.Body.linearVelocity.magnitude*3.6f)+" KPH";
        if (player.HornFeedback>0) status.text+="  HORN";
        var defenses=v.GetComponent<VehicleCountermeasures>();
        if(defenses && defenses.Incoming>0)status.text+="  <color=#FF9637>MISSILE INCOMING</color>";
        controls.text=role==Vehicle.SeatRole.Driver?(air?"CLIMB  [SPACE]\nDESCEND  [CTRL]\nMOVE  [WASD]\nAIM ALIGN  [RMB]":"BRAKE / REV  [S]\nACCELERATE  [W]\nBOOST  [SHIFT]\nHANDBRAKE  [Z]\nHORN  [H]"):role==Vehicle.SeatRole.Gunner?"FIRE  [LMB]\nZOOM  [RMB]\nAMMO  INF":v.seats[player.SeatIndex].showVehicleInstruments?(air?"FLIGHT MONITOR":"VEHICLE MONITOR"):"PASSENGER";
        if (air && role == Vehicle.SeatRole.Driver) controls.text += "\n[T] ASSIST " + (v.FlightAssistEnabled ? "ON" : "OFF");
        if (v.motor is JetMotor && role == Vehicle.SeatRole.Driver)
            controls.text = "THROTTLE  [W/S]\nRUDDER  [A/D]\nPITCH / ROLL  [MOUSE]"
                + (((JetMotor)v.motor).afterburnerMultiplier>1 ? "\nAFTERBURNER  [SHIFT]" : "")
                + "\nBRAKE  [SPACE]\n[T] ASSIST " + (v.FlightAssistEnabled ? "ON" : "OFF");
        if(weaponSystems && selectedWeapon!=null)
        {
            int count=System.Array.FindAll(weaponSystems.weapons,w=>w.role==role).Length;
            if(count>1)controls.text+="\n[1-"+count+"] WEAPONS / WHEEL";
        }
        if(weaponSystems && System.Array.Exists(weaponSystems.weapons,w=>w.requiresRadarSupport))
        {
            bool linked=VehicleRadarSupport.HasFriendlyRadar(v);
            if(radarStatusVehicle!=v){radarStatusVehicle=v;radarWasLinked=linked;radarStatusChangedAt=float.NegativeInfinity;}
            else if(linked!=radarWasLinked){radarWasLinked=linked;radarStatusChangedAt=Time.time;}
            string radio=Time.time-radarStatusChangedAt<2.5f?(linked?"RADAR LINK ACQUIRED":"RADAR LINK LOST"):(linked?"AA RADAR LINK: ONLINE":"AA RADAR LINK: OFFLINE");
            controls.text+="\n<color="+(linked?"#FF8C1F":"#FFFFFF")+">"+radio+"</color>";
        }
        if(!air && role==Vehicle.SeatRole.Driver && showAttack && attackSight.Operational) controls.text += "\nFIRE  [LMB]\nZOOM  [RMB]";
        UpdateEquipmentSystems(v,role,weaponSystems,selectedWeapon,defenses,armed);
        int pageSeats=Mathf.Min(12,v.seats.Length-player.SeatPage*12);
        rightControls.text=(v.hasRenderedInterior&&!v.seats[player.SeatIndex].externalView?"[V]  CAMERA":"EXTERNAL VIEW")+"\n[C]  FREELOOK"+(player.Freelook?" ON":"")+"\nHOLD [E]  EXIT\n"+(pageSeats==1?"[F1]  STATION":"[F1-F"+pageSeats+"]  SEATS");
        var doorControl=v.GetComponent<VehicleDoors>();
        if(doorControl)rightControls.text += "\nTAP [E]  " + (role==Vehicle.SeatRole.Driver || doorControl.operateTogether ? "DOORS" : "NEARBY DOOR");
        if(v.seats.Length>12)rightControls.text+="\n[PGUP/PGDN] PAGE "+(player.SeatPage+1)+"/"+((v.seats.Length+11)/12);
        EnsureSeatMarkers(v.seats.Length);
        crew.fontSize = v.seats.Length > 8 ? 10 : v.seats.Length > 5 ? 12 : 14;
        crew.text="";
        for(int i=0;i<seatMarkers.Length;i++)
        {
            int seatIndex=player.SeatPage*12+i;
            bool exists=seatIndex<v.seats.Length;seatMarkers[i].gameObject.SetActive(exists);if(!exists)continue;
            var seat=v.seats[seatIndex];bool occupied=seat.occupant;
            seatMarkers[i].rectTransform.anchoredPosition=v.seatDiagram?seat.diagramPosition:air?new Vector2(0,i==0?26:54):i==1?new Vector2(0,-7):new Vector2(i==0||i==3?-15:15,i<3?20:-30);
            seatMarkers[i].color=seatIndex==player.SeatIndex?accent:occupied?Color.white:new Color(1,1,1,.18f);
            seatNumbers[i].color=seatIndex==player.SeatIndex||occupied?Color.black:Color.white;
            seatNumbers[i].text=(seatIndex+1).ToString();
            string name=occupied?(seat.occupant==player?"YOU":seat.occupant.name):"EMPTY";
            crew.text+="<color="+(seatIndex==player.SeatIndex?"#FF8C1F":"#FFFFFF")+">"+(seatIndex+1)+"  "+name+"</color>  <size="+(v.seats.Length>8?10:12)+">"+seat.name.ToUpperInvariant()+"</size>\n";
        }
        float health=v.Health/v.maxHealth;for(int i=0;i<hullSegments.Length;i++)hullSegments[i].color=i<hullSegments.Length*health?Color.white:new Color(1,1,1,.15f);
        if(!air)
        {
            flightPanel.SetActive(false);dronePanel.SetActive(false);
            bool driver=ShowVehicleInstruments(v) && !showAttack;
            roadPanel.SetActive(driver && UsesAnalogGround(v));
            armoredPanel.SetActive(driver && !UsesAnalogGround(v));
            if (roadPanel.activeSelf) UpdateRoadDashboard(v);
            if (armoredPanel.activeSelf) UpdateArmoredInstruments(v);
            return;
        }
        roadPanel.SetActive(false);armoredPanel.SetActive(false);
        bool pilot=ShowVehicleInstruments(v);
        bool drone=v.instruments==Vehicle.InstrumentLayout.Drone;
        bool dials=VehicleReferenceInstruments.UsesDials(v);
        flightPanel.SetActive(pilot && !drone && !dials);
        dronePanel.SetActive(pilot && drone);
        if(!pilot)return;
        float altitude=0, nearest=float.PositiveInfinity;
        foreach(var hit in Physics.RaycastAll(v.transform.position,Vector3.down,1000,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(v.transform)&&!hit.collider.GetComponentInParent<SoldierLife>()&&hit.distance<nearest){nearest=hit.distance;altitude=hit.distance;}
        if(dials)referenceInstruments.Present(v,altitude,true,player.CockpitView);
        if(dronePanel.activeSelf) UpdateDroneInstruments(v,altitude);
        bool metric=v.instrumentReference==Vehicle.InstrumentReference.Su27;
        var jet=v.motor as JetMotor;
        float horizontalSpeed=Vector3.ProjectOnPlane(v.Body.linearVelocity,Vector3.up).magnitude;
        float instrumentSpeed=jet?jet.Airspeed*Mathf.Sqrt(Mathf.Max(0,jet.airDensity)/1.225f):horizontalSpeed;
        float instrumentAltitude=jet?v.Body.position.y-fixedWingReadout.altitudeDatum:altitude;
        float knots=instrumentSpeed*1.943844f;
        telemetry.text="V/S "+(v.Body.linearVelocity.y*196.8504f).ToString("+0;-0;0")+" FPM";
        speedReadout.text=(jet?"IAS":"GS")+" <size=26>"+Mathf.RoundToInt(metric?instrumentSpeed*3.6f:knots).ToString("000")+"</size> "+(metric?"KM/H":"KT");
        altitudeReadout.text=(jet?"ALT":"AGL")+" <size=26>"+Mathf.RoundToInt(metric?instrumentAltitude:instrumentAltitude*3.28084f).ToString("0000")+"</size> "+(metric?"M":"FT");
        if(metric)telemetry.text="V/S "+v.Body.linearVelocity.y.ToString("+0.0;-0.0;0.0")+" M/S";
        string assist=v.FlightAssistEnabled?"ASSIST":"MANUAL";
        systemsReadout.text=v.instruments==Vehicle.InstrumentLayout.Helicopter
            ? "ROTOR "+Mathf.RoundToInt(((HelicopterMotor)v.motor).RotorPower*100)+"%   "+assist
            : "FLIGHT CONTROL  /  "+assist;
        groundAltitude = altitude;
        var rockets=v.GetComponent<VehicleLauncher>();
        weapon.text=selectedWeapon!=null?selectedWeapon.name+"  [LMB]  "+ammunition.text
            :rockets?"RKT  [LMB]  "+rockets.Missiles+" / "+rockets.capacity+(rockets.Missiles<rockets.capacity ? "   +1 IN "+rockets.RechargeRemaining.ToString("F1")+" S" : ""):"NO PILOT WEAPON";
    }
    void UpdateEquipmentSystems(Vehicle vehicle,Vehicle.SeatRole role,VehicleWeaponSystems systems,
        VehicleWeaponSystems.Weapon selected,VehicleCountermeasures defenses,bool armed)
    {
        if(displayedSystemsVehicle!=vehicle || displayedSystemsRole!=role)
        {
            displayedSystemsVehicle=vehicle;displayedSystemsRole=role;displayedDefenses=defenses;
            seatSystems.Clear();
            if(systems)foreach(var entry in systems.weapons)if(entry.role==role)seatSystems.Add(entry);
            if(systemTiles.Count==0)
            {
                var template=(RectTransform)slotIcon.transform.parent;
                foreach(Transform child in template.parent)
                    if(child.name.StartsWith("Weapon Tile"))child.gameObject.SetActive(false);
            }
            int count=seatSystems.Count+(armed && defenses && role!=Vehicle.SeatRole.Passenger?1:0);
            if(count==0 && armed)count=1;
            var activeWeapon=(RectTransform)equippedIcon.transform.parent;
            var activePosition=activeWeapon.anchoredPosition;
            activePosition.y=58;
            activeWeapon.anchoredPosition=activePosition;
            while(systemTiles.Count<count)
            {
                var tile=Instantiate((RectTransform)slotIcon.transform.parent,slotIcon.transform.parent.parent);
                tile.name="Vehicle system "+systemTiles.Count;
                var icon=tile.Find("Weapon Icon").GetComponent<Image>();
                var key=tile.Find("Key").GetComponent<TMP_Text>();
                var caption=Instantiate(equippedName,tile);caption.name="System name and ammunition";
                caption.fontSize=14;caption.alignment=TextAlignmentOptions.MidlineLeft;caption.raycastTarget=false;
                caption.enableAutoSizing=true;caption.fontSizeMin=11;caption.fontSizeMax=14;
                caption.rectTransform.anchorMin=caption.rectTransform.anchorMax=new Vector2(.5f,.5f);
                caption.rectTransform.anchoredPosition=new Vector2(8,-22);caption.rectTransform.sizeDelta=new Vector2(142,20);
                icon.rectTransform.sizeDelta=new Vector2(128,44);icon.rectTransform.anchoredPosition=new Vector2(18,0);
                icon.preserveAspect=true;icon.raycastTarget=false;
                systemTiles.Add(tile);systemIcons.Add(icon);systemKeys.Add(key);systemCaptions.Add(caption);
            }
            for(int i=0;i<systemTiles.Count;i++)
            {
                var tile=systemTiles[i];tile.gameObject.SetActive(i<count);
                // Weapon slots stay below the active card; utility sits beside it like infantry launchers.
                bool defense=defenses && role!=Vehicle.SeatRole.Passenger && i==seatSystems.Count;
                int weaponCount=Mathf.Max(1,seatSystems.Count);
                float width=(350f-8f*(weaponCount-1))/weaponCount;
                tile.sizeDelta=new Vector2(defense?86:width,48);
                tile.anchoredPosition=defense?new Vector2(-226,0):new Vector2(-175+width*.5f+i*(width+8),0);
                var key=systemKeys[i].rectTransform;
                key.anchoredPosition=new Vector2(-tile.sizeDelta.x*.5f+20,0);
                systemIcons[i].rectTransform.sizeDelta=new Vector2(Mathf.Max(32,width-42),44);
                systemIcons[i].rectTransform.anchoredPosition=new Vector2(18,0);
                systemCaptions[i].gameObject.SetActive(defense);
            }
        }
        bool utility=displayedDefenses && role!=Vehicle.SeatRole.Passenger;
        vehicleEquipment.SetActive(armed || utility);
        bool primaryDefense=utility && !armed;
        equippedIcon.transform.parent.gameObject.SetActive(armed || primaryDefense);
        equippedIcon.gameObject.SetActive(armed);
        if(primaryDefense)
        {
            equippedName.text=defenses.SystemName;
            equipmentLabel.text="VEHICLE DEFENSE";
            ammunition.text=defenses.Charges+" / "+defenses.capacity;
            fireMode.text=defenses.ReadyIn>0 ? defenses.ReadyIn.ToString("F1")+" S  [X]"
                :defenses.Charges>0 ? "READY  [X]" : "+1 "+defenses.RechargeIn.ToString("F1")+" S  [X]";
        }
        for(int i=0;i<systemTiles.Count;i++)
        {
            if(!systemTiles[i].gameObject.activeSelf)continue;
            var entry=i<seatSystems.Count?seatSystems[i]:null;
            bool defense=utility && i==seatSystems.Count;
            if(primaryDefense && defense){systemTiles[i].gameObject.SetActive(false);continue;}
            bool chosen=entry!=null && entry==selected;
            var icon=systemIcons[i];var key=systemKeys[i];var caption=systemCaptions[i];
            icon.gameObject.SetActive(!defense);
            key.gameObject.SetActive(!defense);
            if(!defense)
            {
                caption.rectTransform.sizeDelta=new Vector2(142,20);
                caption.rectTransform.anchoredPosition=new Vector2(8,-22);
            }
            if(entry!=null)
            {
                icon.sprite=entry.icon;key.text=(i+1).ToString();
                caption.text=entry.name+"  "+(entry.launcher?entry.launcher.Missiles+"/"+entry.launcher.capacity:entry.IsSingleShot?(entry.ReadyIn>0?"0/1":"1/1"):"INF");
                if(entry.IsSingleShot && entry.ReadyIn>0)caption.text+="  "+entry.ReadyIn.ToString("F1")+"s";
            }
            else if(defense)
            {
                key.text="X";
                caption.text=(defenses.SystemName.Contains("FLARE")?"FLARES":defenses.SystemName)+"\n"+defenses.Charges+" / "+defenses.capacity+"  [X]";
                if(defenses.ReadyIn>0)caption.text+="  "+defenses.ReadyIn.ToString("0")+"s";
                else if(defenses.Charges<defenses.capacity)caption.text+="  +1 "+defenses.RechargeIn.ToString("0")+"s";
                caption.rectTransform.sizeDelta=new Vector2(78,44);caption.rectTransform.anchoredPosition=Vector2.zero;
                caption.alignment=TextAlignmentOptions.Center;

            }
            else
            {
                icon.sprite=equippedIcon.sprite;key.text="LMB";caption.text=equippedName.text;chosen=true;
            }
            bool available=entry==null || !systems || systems.IsAvailable(entry);
            icon.color=new Color(1,1,1,!available ? .25f : chosen ? 1 : .6f);icon.material=chosen&&available?selectedIconGlow:null;
            key.color=chosen?accent:Color.white;caption.color=Color.white;
            var background=systemTiles[i].GetComponent<Image>();
            if(background)background.color=chosen?new Color(accent.r,accent.g,accent.b,.12f):new Color(1,1,1,.035f);
        }
    }
    void LateUpdate()
    {
        var attackSight=player.IsMounted?player.MountedVehicle.GetComponent<VehicleAttackSight>():null;
        var sightCamera=player.ActiveVehicleCamera?player.ActiveVehicleCamera:Camera.main;
        attackHud.Present(player,attackSight,sightCamera);
        lockHud.Present(player,sightCamera);
        if(attackHud.Visible){gunnerReadout.gameObject.SetActive(false);desiredAim.gameObject.SetActive(false);actualAim.gameObject.SetActive(false);flightPathMarker.gameObject.SetActive(false);return;}
        if (player.IsMounted && player.MountedVehicle.IsAircraft && ShowVehicleInstruments(player.MountedVehicle))
            if(flightPanel.activeSelf) UpdateFlightInstruments(player.MountedVehicle, groundAltitude);
        flightPathMarker.gameObject.SetActive(player.IsMounted && flightPanel.activeSelf);
        if(flightPathMarker.gameObject.activeSelf)
        {
            var cam=player.ActiveVehicleCamera ? player.ActiveVehicleCamera : Camera.main;
            if(cam)
            {
                Vector3 velocity=player.MountedVehicle.Body.linearVelocity;
                float speed=velocity.magnitude;
                float travelWeight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,5,speed));
                Vector3 direction=speed>1?Vector3.Slerp(cam.transform.forward,velocity.normalized,travelWeight):cam.transform.forward;
                PlaceMarker(flightPathMarker,cam,cam.transform.position+direction*100,true);
                if(speed<=1)flightPathMarker.GetComponent<Graphic>().canvasRenderer.SetAlpha(.4f);
            }
            else flightPathMarker.gameObject.SetActive(false);
        }
        // Defense-only primary cards do not turn an unarmed seat into a weapon station.
        bool armed = player.IsMounted && seatHasWeapon;
        gunnerReadout.gameObject.SetActive(armed && player.MountedVehicle.seats[player.SeatIndex].role==Vehicle.SeatRole.Gunner);
        desiredAim.gameObject.SetActive(armed); actualAim.gameObject.SetActive(armed);
        if (!armed) return;
        var vehicle = player.MountedVehicle; Camera camera = player.ActiveVehicleCamera ? player.ActiveVehicleCamera : Camera.main;
        if(!camera){desiredAim.gameObject.SetActive(false);actualAim.gameObject.SetActive(false);return;}
        var systems=vehicle.GetComponent<VehicleWeaponSystems>();
        var current=systems?systems.Current(player):null;
        bool gunner = current!=null?current.system==VehicleWeaponSystems.FireSystem.Gun:vehicle.seats[player.SeatIndex].role == Vehicle.SeatRole.Gunner;
        var activeLauncher=current!=null?current.launcher:vehicle.GetComponent<VehicleLauncher>();
        var activeGun=current!=null?current.gun:vehicle.turret;
        gunnerReadout.gameObject.SetActive(gunner && activeGun);
        if(!gunner&&!activeLauncher){desiredAim.gameObject.SetActive(false);actualAim.gameObject.SetActive(false);return;}
        actualAim.GetComponent<Graphic>().canvasRenderer.SetAlpha(gunner || activeLauncher.Missiles > 0 ? 1f : .2f);

        Vector3 requested; Ray actual; float range;
        if (gunner)
        {
            requested = activeGun.RequestedAimDirection;
            actual = new Ray(activeGun.muzzle.position,activeGun.muzzle.forward); range = activeGun.range;
        }
        else
        {
            requested = vehicle.IsAircraft?vehicle.RequestedFlightAttitude * Vector3.forward:camera.transform.forward;
            actual = activeLauncher.NextShotRay; range = activeLauncher.NextShotRange;
        }
        if (!player.Freelook) requested = camera.transform.forward;
        Vector3 desiredPoint=ResolvePoint(new Ray(camera.transform.position,requested),range,vehicle);
        PlaceMarker(desiredAim,camera,desiredPoint);
        // Rockets predict their own impact over their lifetime, independently of the camera target distance.
        Vector3 impact=gunner
            ? ResolvePoint(actual,Mathf.Min(range,Vector3.Distance(actual.origin,desiredPoint)),vehicle)
            : activeLauncher.PredictedImpactPoint;
        PlaceMarker(actualAim,camera,impact);
        if(gunner){var direction=activeGun.muzzle.forward;float azimuth=Mathf.Repeat(Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,360);float elevation=Mathf.Asin(Mathf.Clamp(direction.y,-1,1))*Mathf.Rad2Deg;float zoom=Mathf.Tan(35*Mathf.Deg2Rad)/Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad);float relativeYaw=Mathf.DeltaAngle(vehicle.transform.eulerAngles.y,azimuth);float distance=Vector3.Distance(actual.origin,ResolvePoint(actual,range,vehicle));string station=vehicle.instruments==Vehicle.InstrumentLayout.Armored?"WEAPON STATION":"GUN SIGHT";gunnerReadout.text=station+"   RNG "+(distance>=range-.1f?">":"")+Mathf.RoundToInt(distance).ToString("0000")+" M   AZ "+relativeYaw.ToString("+000;-000;000")+"°   EL "+elevation.ToString("+00;-00;00")+"°   "+zoom.ToString("F1")+"X";}
    }
    void BuildRoadDashboard()
    {
        roadPanel = new GameObject("Road cockpit instruments", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        roadPanel.GetComponent<UnityEngine.UI.Image>().color=Color.clear;
        roadPanel.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        var rect=(RectTransform)roadPanel.transform;
        rect.SetParent(transform,false);
        rect.anchorMin=rect.anchorMax=new Vector2(1f,0f);rect.pivot=new Vector2(1f,0f);
        rect.anchoredPosition=new Vector2(-50,75);rect.sizeDelta=new Vector2(330,150);
        var gauge=new GameObject("Speedometer",typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleSpeedometerGraphic));
        gauge.transform.SetParent(rect,false);roadGauge=gauge.GetComponent<VehicleSpeedometerGraphic>();
        roadGauge.rectTransform.sizeDelta=new Vector2(200,115);roadGauge.rectTransform.anchoredPosition=new Vector2(-62,62);
        roadGauge.raycastTarget=false;
        var dial=new GameObject("Referenced military speed dial",typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleDialGraphic));dial.transform.SetParent(rect,false);roadDial=dial.GetComponent<VehicleDialGraphic>();roadDial.rectTransform.sizeDelta=new Vector2(125,125);roadDial.rectTransform.anchoredPosition=new Vector2(-62,75);roadDial.raycastTarget=false;
        roadTicks=new TMP_Text[5];for(int i=0;i<roadTicks.Length;i++){float angle=Mathf.Lerp(225,-45,i/4f)*Mathf.Deg2Rad;roadTicks[i]=DashboardLabel("Speed scale "+i,roadDial.transform,new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*40,new Vector2(25,18),10);}
        roadSpeed=DashboardLabel("Speed",rect,new Vector2(-62,26),new Vector2(140,48),30);
        roadDetails=DashboardLabel("Drivetrain",rect,new Vector2(95,66),new Vector2(175,105),16);
        roadDetails.alignment=TextAlignmentOptions.MidlineLeft;
        roadPanel.SetActive(false);
    }
    TMP_Text DashboardLabel(string name,Transform parent,Vector2 position,Vector2 size,float fontSize)
    {
        var label=Instantiate(telemetry,parent);label.name=name;
        label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.5f,.5f);
        label.rectTransform.anchoredPosition=position;label.rectTransform.sizeDelta=size;
        label.fontSize=fontSize;label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
        label.color=Color.white;label.text="";label.gameObject.SetActive(true);return label;
    }
    void UpdateRoadDashboard(Vehicle vehicle)
    {
        float speed=VehicleGroundReadout.SpeedKmh(vehicle);
        var motor=(HummerMotor)vehicle.motor;
        bool imperial=ImperialGround(vehicle);float reading=imperial?speed*.621371f:speed;
        bool referenced=vehicle.instrumentReference!=Vehicle.InstrumentReference.Generic;roadGauge.gameObject.SetActive(!referenced);roadDial.gameObject.SetActive(referenced);
        roadGauge.SetSpeed(reading,Mathf.Ceil(motor.maxSpeed*(imperial?2.236936f:3.6f)/20f)*20f);
        roadDial.maximum=VehicleGroundReadout.GaugeMaximum(vehicle,imperial);roadDial.SetValue(reading);
        for(int i=0;i<roadTicks.Length;i++)roadTicks[i].text=(roadDial.maximum*i/4).ToString("0");
        roadSpeed.text=Mathf.RoundToInt(reading)+" <size=12>"+(imperial?"MPH":"KM/H")+"</size>";
        var input=vehicle.DriverInput;
        string drive=DriveState(vehicle);
        roadDetails.text="<color=#FF9637>"+drive+"</color>\nHDG "+HeadingNumber(vehicle.transform.eulerAngles.y).ToString("000")+"\nHULL "+Mathf.RoundToInt(vehicle.Health/vehicle.maxHealth*100)+"%";
        if(input.boost)roadDetails.text+="\n<color=#FF9637>BOOST</color>";
    }
    void BuildSpecializedInstruments()
    {
        armoredPanel=InstrumentCard("Armored driver instruments",new Vector2(-50,85),new Vector2(340,200));
        armoredHeading=DashboardLabel("Hull heading",armoredPanel.transform,new Vector2(0,72),new Vector2(310,28),16);
        armoredSpeed=DashboardLabel("Digital speed",armoredPanel.transform,new Vector2(-85,18),new Vector2(140,65),38);
        armoredState=DashboardLabel("Drive state",armoredPanel.transform,new Vector2(-85,-42),new Vector2(155,35),14);
        armoredAttitude=DashboardLabel("Hull inclination",armoredPanel.transform,new Vector2(85,-42),new Vector2(160,40),14);
        var level=new GameObject("Hull level",typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleDiagramGraphic));
        level.transform.SetParent(armoredPanel.transform,false);armoredLevel=level.GetComponent<VehicleDiagramGraphic>();
        armoredLevel.drawing=VehicleDiagramGraphic.Drawing.HullLevel;armoredLevel.rectTransform.anchoredPosition=new Vector2(85,22);armoredLevel.rectTransform.sizeDelta=new Vector2(105,65);armoredLevel.raycastTarget=false;
        dronePanel=InstrumentCard("Unmanned aircraft telemetry",new Vector2(-50,85),new Vector2(350,210));
        droneHeading=DashboardLabel("UAS heading",dronePanel.transform,new Vector2(0,72),new Vector2(325,35),17);
        droneTelemetry=DashboardLabel("UAS telemetry",dronePanel.transform,new Vector2(0,-5),new Vector2(315,120),18);
        droneTelemetry.alignment=TextAlignmentOptions.MidlineLeft;
        armoredPanel.SetActive(false);dronePanel.SetActive(false);
    }
    GameObject InstrumentCard(string name,Vector2 position,Vector2 size)
    {
        var card=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image));
        var rect=(RectTransform)card.transform;rect.SetParent(transform,false);
        rect.anchorMin=rect.anchorMax=Vector2.right;rect.pivot=Vector2.right;rect.anchoredPosition=position;rect.sizeDelta=size;
        var image=card.GetComponent<UnityEngine.UI.Image>();image.color=name=="Armored driver instruments"?Color.clear:new Color(0,0,0,.32f);image.raycastTarget=false;return card;
    }
    static string DriveState(Vehicle vehicle)
    {
        if(vehicle.DriverInput.handbrake)return "PARK BRAKE";
        if(vehicle.DriverInput.brake)return "BRAKING";
        float longitudinal=Vector3.Dot(vehicle.Body.linearVelocity,vehicle.transform.forward);
        return longitudinal<-.3f?"REVERSE":longitudinal>.3f?"FORWARD":"STOPPED";
    }
    void UpdateArmoredInstruments(Vehicle v)
    {
        float heading=Mathf.Repeat(v.transform.eulerAngles.y,360);
        armoredHeading.text=VehicleGroundReadout.FamilyCaption(v)+" / HDG "+HeadingNumber(heading).ToString("000")+"°";
        bool imperial=ImperialGround(v);
        armoredSpeed.text=Mathf.RoundToInt(VehicleGroundReadout.SpeedKmh(v)*(imperial ? .621371f : 1f))+"<size=14> "+(imperial?"MPH":"KM/H")+"</size>";
        armoredState.text="<color=#FF9637>"+DriveState(v)+"</color>";
        Vector3 up=v.transform.InverseTransformDirection(Vector3.up);
        float roll=Mathf.Atan2(up.x,up.y)*Mathf.Rad2Deg;
        armoredLevel.SetReading(roll);
        armoredAttitude.text=VehicleGroundReadout.Station(v);
    }
    void UpdateDroneInstruments(Vehicle v,float altitude)
    {
        float heading=Mathf.Repeat(v.transform.eulerAngles.y,360);
        droneHeading.text="UAS  /  HDG "+HeadingNumber(heading).ToString("000")+"°";
        droneTelemetry.text="AGL     "+altitude.ToString("0.0")+" M\nGS       "+Vector3.ProjectOnPlane(v.Body.linearVelocity,Vector3.up).magnitude.ToString("0.0")+" M/S\nV/S      "+v.Body.linearVelocity.y.ToString("+0.0;-0.0;0.0")+" M/S\n<color=#FF9637>"+(v.FlightAssistEnabled?"POSITION ASSIST":"MANUAL CONTROL")+"</color>";
    }
    void EnsureSeatMarkers(int count)
    {
        count=Mathf.Min(12,count);
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
        var panel=(RectTransform)flightPanel.transform;
        bool fixedWing=vehicle.instruments==Vehicle.InstrumentLayout.FixedWing;
        bool apache=vehicle.instrumentReference==Vehicle.InstrumentReference.Apache;
        bool metric=vehicle.instrumentReference==Vehicle.InstrumentReference.Su27;
        panel.localScale=Vector3.one;
        bankScale.gameObject.SetActive(!fixedWing && !apache);
        verticalSpeedIndicator.gameObject.SetActive(!fixedWing && !apache);
        speedTape.gameObject.SetActive(!apache && !metric);
        altitudeTape.gameObject.SetActive(!metric);
        metricPitchScale.gameObject.SetActive(metric);metricPitchReadout.gameObject.SetActive(metric);
        telemetry.rectTransform.anchoredPosition=metric?new Vector2(0,-150):new Vector2(280,-125);
        horizon.GetComponent<VehicleDiagramGraphic>().drawing=metric?VehicleDiagramGraphic.Drawing.LevelHorizon:VehicleDiagramGraphic.Drawing.PitchLadder;
        foreach(Transform label in horizon)label.gameObject.SetActive(!metric);
        horizon.gameObject.SetActive(!apache);
        hoverCue.gameObject.SetActive(apache);
        if(hoverCue.gameObject.activeSelf)hoverCue.SetHoverVelocity(new Vector2(Vector3.Dot(vehicle.Body.linearVelocity,vehicle.transform.right),Vector3.Dot(vehicle.Body.linearVelocity,vehicle.transform.forward)));
        panel.anchoredPosition=new Vector2(0,35);
        Transform view = vehicle.transform;
        float heading = Mathf.Repeat(view.eulerAngles.y,360);
        compass.text = "HDG " + HeadingNumber(heading).ToString("000") + "  " + Cardinal(heading);
        float elevation = Mathf.Asin(Mathf.Clamp(view.forward.y,-1,1)) * Mathf.Rad2Deg;
        if(metric){metricPitchScale.SetReading(elevation);metricPitchReadout.text="PITCH "+elevation.ToString("+00;-00;00")+"°";}
        Vector3 up = view.InverseTransformDirection(Vector3.up);
        float bank = Mathf.Atan2(up.x,up.y) * Mathf.Rad2Deg;
        horizon.localEulerAngles = new Vector3(0,0,-bank);
        horizon.anchoredPosition = (Quaternion.Euler(0,0,-bank)*new Vector3(0,-elevation*2f,0));
        headingTape.SetReading(heading);
        altitudeTape.tickStep=metric?20:50;altitudeTape.pixelsPerUnit=metric?2:.8f;
        speedTape.tickStep=metric?50:10;speedTape.pixelsPerUnit=metric?.8f:4;
        var jet=vehicle.motor as JetMotor;
        float displayedAltitude=jet?vehicle.Body.position.y-fixedWingReadout.altitudeDatum:altitude;
        float displayedSpeed=jet?jet.Airspeed*Mathf.Sqrt(Mathf.Max(0,jet.airDensity)/1.225f):Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,Vector3.up).magnitude;
        altitudeTape.SetReading(displayedAltitude*(metric?1:3.28084f));
        speedTape.SetReading(displayedSpeed*(metric?3.6f:1.943844f));
        bankScale.SetReading(bank);
        verticalSpeedIndicator.SetReading(vehicle.Body.linearVelocity.y*196.8504f);
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
    static void PlaceMarker(RectTransform marker,Camera camera,Vector3 point,bool clampToView=false)
    {
        Vector3 screen=camera.WorldToScreenPoint(point);
        if(clampToView)
        {
            if(screen.z<=0)
            {
                var center=camera.pixelRect.center;
                var direction=center-new Vector2(screen.x,screen.y);
                direction=direction.sqrMagnitude<1?Vector2.down:direction.normalized;
                screen.x=center.x+direction.x*camera.pixelRect.width;
                screen.y=center.y+direction.y*camera.pixelRect.height;
            }
            const float margin=45;
            var rect=camera.pixelRect;
            screen.x=Mathf.Clamp(screen.x,rect.xMin+margin,rect.xMax-margin);
            screen.y=Mathf.Clamp(screen.y,rect.yMin+margin,rect.yMax-margin);
        }
        bool visible=clampToView||screen.z>0;
        marker.gameObject.SetActive(visible);if(!visible)return;
        // World placement accounts for the parent's pivot and the marker's anchors.
        if(RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)marker.parent,screen,null,out var world))
            marker.position=world;
        if(clampToView)marker.GetComponent<Graphic>().canvasRenderer.SetAlpha(screen.z>0?.9f:.35f);
    }
    bool ShowVehicleInstruments(Vehicle v)=>v.seats[player.SeatIndex].role==Vehicle.SeatRole.Driver||v.seats[player.SeatIndex].showVehicleInstruments;
    static bool UsesAnalogGround(Vehicle v)=>v.instruments==Vehicle.InstrumentLayout.Road || v.instrumentReference==Vehicle.InstrumentReference.HMMWV || v.instrumentReference==Vehicle.InstrumentReference.M113 || v.instrumentReference==Vehicle.InstrumentReference.Leopard2 || v.instrumentReference==Vehicle.InstrumentReference.T72 || v.instrumentReference==Vehicle.InstrumentReference.FMTV || v.instrumentReference==Vehicle.InstrumentReference.MRAP || v.instrumentReference==Vehicle.InstrumentReference.Bradley;
    static bool ImperialGround(Vehicle v)=>v.instrumentReference==Vehicle.InstrumentReference.HMMWV || v.instrumentReference==Vehicle.InstrumentReference.M113 || v.instrumentReference==Vehicle.InstrumentReference.Abrams || v.instrumentReference==Vehicle.InstrumentReference.FMTV || v.instrumentReference==Vehicle.InstrumentReference.HIMARS || v.instrumentReference==Vehicle.InstrumentReference.MRAP || v.instrumentReference==Vehicle.InstrumentReference.Bradley;
    static int HeadingNumber(float heading)=>Mathf.RoundToInt(Mathf.Repeat(heading,360))%360;
    static string Cardinal(float a){string[] directions={"N","NE","E","SE","S","SW","W","NW"};return directions[Mathf.RoundToInt(a/45)%8];}
}
