using UnityEngine;
using TMPro;

// Sight presentation is independent of traversing, loading, and firing mechanics.
public class ArenaVehicleAttackHud : MonoBehaviour
{
    GameObject panel;
    GameObject opticalLayer;
    VehicleGunnerReticleGraphic reticle,orientation,aperture;
    TMP_Text[] scaleLabels;
    RectTransform actualDirection;
    TMP_Text heading,rangeText,sightText,weaponText,orientationText;
    readonly RaycastHit[] hits=new RaycastHit[64];
    public bool Visible=>panel.activeSelf;
    public void Build(TMP_Text template)
    {
        panel=new GameObject("Armored weapon sight",typeof(RectTransform));var rect=(RectTransform)panel.transform;rect.SetParent(transform,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(640,470);
        opticalLayer=new GameObject("Optical mask behind HUD",typeof(RectTransform),typeof(Canvas));
        opticalLayer.transform.SetParent(transform,false);
        var opticalCanvas=opticalLayer.GetComponent<Canvas>();opticalCanvas.overrideSorting=true;opticalCanvas.sortingOrder=19;
        aperture=Graphic(opticalLayer.transform,"Optical eyepiece boundary",Vector2.zero,new Vector2(6000,6000));aperture.aperture=true;
        opticalLayer.SetActive(false);
        reticle=Graphic(panel.transform,"Gunner aiming reticle",Vector2.zero,new Vector2(460,440));
        orientation=Graphic(panel.transform,"Hull and turret orientation",new Vector2(-370,-190),new Vector2(100,100));orientation.orientation=true;
        heading=Label(template,"Sight heading",new Vector2(0,330),new Vector2(570,32),18);
        rangeText=Label(template,"Sight range",new Vector2(0,-130),new Vector2(300,30),22);
        sightText=Label(template,"Optical channel",new Vector2(0,300),new Vector2(440,30),15);
        weaponText=Label(template,"Ammunition and readiness",new Vector2(370,-190),new Vector2(240,60),16);
        orientationText=Label(template,"Gun traverse and elevation",new Vector2(-370,-257),new Vector2(260,35),14);
        scaleLabels=new TMP_Text[18];for(int i=0;i<scaleLabels.Length;i++)scaleLabels[i]=Label(template,"TPD scale "+i,Vector2.zero,new Vector2(34,20),11);
        var actual=Graphic(panel.transform,"Actual barrel direction",Vector2.zero,new Vector2(30,30));actual.barrelMarker=true;actualDirection=actual.rectTransform;
        panel.SetActive(false);
    }
    VehicleGunnerReticleGraphic Graphic(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var g=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleGunnerReticleGraphic));g.transform.SetParent(parent,false);var graphic=g.GetComponent<VehicleGunnerReticleGraphic>();graphic.rectTransform.anchoredPosition=position;graphic.rectTransform.sizeDelta=size;graphic.raycastTarget=false;return graphic;
    }
    TMP_Text Label(TMP_Text template,string name,Vector2 position,Vector2 size,float font)
    {
        var t=Instantiate(template,panel.transform);t.name=name;t.rectTransform.anchorMin=t.rectTransform.anchorMax=new Vector2(.5f,.5f);t.rectTransform.anchoredPosition=position;t.rectTransform.sizeDelta=size;t.fontSize=font;t.alignment=TextAlignmentOptions.Center;t.text="";t.gameObject.SetActive(true);t.raycastTarget=false;return t;
    }
    public void Present(VehicleOccupant occupant,VehicleAttackSight sight,Camera camera)
    {
        bool show=sight&&camera&&sight.DisplaysForSeat(occupant.SeatIndex);panel.SetActive(show);if(!show){opticalLayer.SetActive(false);return;}
        var vehicle=sight.vehicle;reticle.style=sight.style;reticle.SetVerticesDirty();
        float sightHeading=Mathf.Repeat(camera.transform.eulerAngles.y,360);heading.text="HDG "+(Mathf.RoundToInt(sightHeading)%360).ToString("000")+"°";
        Ray view=new Ray(camera.transform.position,camera.transform.forward);float range=Range(view,sight.range,vehicle);
        rangeText.text=(range>=sight.range-.1f?">":"")+Mathf.RoundToInt(range).ToString("0000")+" M";
        bool tank=sight.IsDocumentedTankSight;
        opticalLayer.SetActive(tank&&occupant.CockpitView);
        string rangeDigits=range<sight.minimumLaserRange||range>=sight.range-.1f?"----":Mathf.RoundToInt(range).ToString("0000");
        rangeText.rectTransform.anchoredPosition=sight.style==VehicleAttackSight.SightStyle.T72?new Vector2(370,190):new Vector2(0,-135);
        rangeText.color=Color.white;
        // GPS: range below reticle, hollow readiness box on its left; F is a
        // malfunction lamp, so it must not be shown as a permanent decoration.
        // EMES: range, KE ammunition selection and 0 (not released) below view.
        rangeText.text=sight.style==VehicleAttackSight.SightStyle.Abrams?rangeDigits:
            sight.style==VehicleAttackSight.SightStyle.Leopard?rangeDigits+"   KE   "+(sight.Operational?"F":"0"):
            sight.style==VehicleAttackSight.SightStyle.T72?"LRF  "+rangeDigits:rangeDigits+" M";
        sightText.text=(sight.style==VehicleAttackSight.SightStyle.Abrams?"M1A1  /  GPS":sight.style==VehicleAttackSight.SightStyle.Leopard?"LEOPARD 2  /  EMES-15":sight.style==VehicleAttackSight.SightStyle.T72?"T-72A  /  TPD-K1":"DIRECTOR")+"  /  DAY";
        for(int i=0;i<scaleLabels.Length;i++)
        {
            bool tpd=sight.style==VehicleAttackSight.SightStyle.T72;scaleLabels[i].gameObject.SetActive(tpd);if(!tpd)continue;
            if(i<9){scaleLabels[i].text=(i*2+2).ToString();scaleLabels[i].rectTransform.anchoredPosition=new Vector2(-31,45-i*24);}
            else if(i<16){scaleLabels[i].text=(40-(i-9)*5).ToString();scaleLabels[i].rectTransform.anchoredPosition=new Vector2(110+(i-9)*15,-75);}
            else {scaleLabels[i].text=i==16?"0.5":"2.7";scaleLabels[i].rectTransform.anchoredPosition=i==16?new Vector2(36,42):new Vector2(202,-105);}
        }
        var systems=vehicle.GetComponent<VehicleWeaponSystems>();
        var selected=systems && vehicle.turret?systems.Current(vehicle.turret.controlRole):null;
        float reload=selected!=null?selected.ReadyIn:vehicle.turret?vehicle.turret.ReadyIn:0;
        bool single=selected!=null?selected.IsSingleShot:vehicle.turret && vehicle.turret.IsSingleShot;
        string readiness=!sight.Operational?"SAFE":single && reload>0?"RELOAD "+reload.ToString("F1")+" S":"READY";
        weaponText.text=sight.ammunitionType+(single?(reload>0?"  0/1":"  1/1"):"")+"\n<color=#FF9637>"+readiness+"</color>";
        var ray=sight.WeaponRay;Vector3 local=vehicle.transform.InverseTransformDirection(ray.direction);float traverse=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;float elevation=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;orientation.SetTraverse(traverse);
        orientationText.text="TRV "+traverse.ToString("+000;-000;000")+"°   EL "+elevation.ToString("+00;-00;00")+"°";
        Vector3 point=ray.GetPoint(Range(ray,sight.range,vehicle));Vector3 screen=camera.WorldToScreenPoint(point);actualDirection.gameObject.SetActive(screen.z>0);if(screen.z>0){RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)actualDirection.parent,screen,null,out var position);actualDirection.anchoredPosition=position;}
    }
    float Range(Ray ray,float limit,Vehicle vehicle)
    {
        float nearest=limit;int count=Physics.RaycastNonAlloc(ray,hits,limit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);for(int i=0;i<count;i++)if(!hits[i].transform.IsChildOf(vehicle.transform))nearest=Mathf.Min(nearest,hits[i].distance);return nearest;
    }
}
