using UnityEngine;
using TMPro;

// Sight presentation is independent of traversing, loading, and firing mechanics.
public class ArenaVehicleAttackHud : MonoBehaviour
{
    GameObject panel;
    VehicleGunnerReticleGraphic reticle,orientation;
    RectTransform actualDirection;
    TMP_Text heading,rangeText,sightText,weaponText,orientationText;
    readonly RaycastHit[] hits=new RaycastHit[64];
    public bool Visible=>panel.activeSelf;
    public void Build(TMP_Text template)
    {
        panel=new GameObject("Armored weapon sight",typeof(RectTransform));var rect=(RectTransform)panel.transform;rect.SetParent(transform,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(640,470);
        reticle=Graphic(panel.transform,"Gunner aiming reticle",Vector2.zero,new Vector2(300,260));
        orientation=Graphic(panel.transform,"Hull and turret orientation",new Vector2(-230,-180),new Vector2(100,100));orientation.orientation=true;
        heading=Label(template,"Sight heading",new Vector2(0,220),new Vector2(570,32),18);
        rangeText=Label(template,"Sight range",new Vector2(0,-130),new Vector2(300,30),22);
        sightText=Label(template,"Optical channel",new Vector2(-200,145),new Vector2(220,30),15);
        weaponText=Label(template,"Ammunition and readiness",new Vector2(190,-180),new Vector2(240,60),16);
        orientationText=Label(template,"Gun traverse and elevation",new Vector2(-230,-247),new Vector2(260,35),14);
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
        bool show=sight&&camera&&sight.DisplaysForSeat(occupant.SeatIndex);panel.SetActive(show);if(!show)return;
        var vehicle=sight.vehicle;reticle.style=sight.style;reticle.SetVerticesDirty();
        float sightHeading=Mathf.Repeat(camera.transform.eulerAngles.y,360);heading.text="HDG "+(Mathf.RoundToInt(sightHeading)%360).ToString("000")+"°";
        Ray view=new Ray(camera.transform.position,camera.transform.forward);float range=Range(view,sight.range,vehicle);
        rangeText.text=(range>=sight.range-.1f?">":"")+Mathf.RoundToInt(range).ToString("0000")+" M";
        float zoom=Mathf.Tan(35*Mathf.Deg2Rad)/Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad);
        sightText.text=(sight.style==VehicleAttackSight.SightStyle.Abrams?"GPS":sight.style==VehicleAttackSight.SightStyle.Leopard?"EMES":sight.style==VehicleAttackSight.SightStyle.T72?"TPD-K1":"DIRECTOR")+"  /  DAY  "+zoom.ToString("F1")+"X";
        weaponText.text=sight.ammunitionType+"\n<color=#FF9637>"+(sight.Operational?"READY":"SAFE")+"</color>";
        var ray=sight.WeaponRay;Vector3 local=vehicle.transform.InverseTransformDirection(ray.direction);float traverse=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;float elevation=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;orientation.SetTraverse(traverse);
        orientationText.text="TRV "+traverse.ToString("+000;-000;000")+"°   EL "+elevation.ToString("+00;-00;00")+"°";
        Vector3 point=ray.GetPoint(Range(ray,sight.range,vehicle));Vector3 screen=camera.WorldToScreenPoint(point);actualDirection.gameObject.SetActive(screen.z>0);if(screen.z>0){RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)actualDirection.parent,screen,null,out var position);actualDirection.anchoredPosition=position;}
    }
    float Range(Ray ray,float limit,Vehicle vehicle)
    {
        float nearest=limit;int count=Physics.RaycastNonAlloc(ray,hits,limit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);for(int i=0;i<count;i++)if(!hits[i].transform.IsChildOf(vehicle.transform))nearest=Mathf.Min(nearest,hits[i].distance);return nearest;
    }
}
