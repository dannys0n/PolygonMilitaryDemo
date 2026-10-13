using TMPro;
using UnityEngine;

// A/L-era Black Hawk and MD-500-inspired flight gauges, separate from helmet/HUD symbology.
public class VehicleReferenceInstruments : MonoBehaviour
{
    GameObject panel;
    VehicleDialGraphic speed, attitude, altitude, rotor, verticalSpeed;
    TMP_Text speedText, altitudeText, rotorText, verticalText, heading;
    RectTransform attitudeRect, verticalRect;
    public static bool UsesDials(Vehicle vehicle)=>vehicle.instrumentReference==Vehicle.InstrumentReference.BlackHawk||vehicle.instrumentReference==Vehicle.InstrumentReference.LittleBird;
    public void Build(TMP_Text template)
    {
        panel=new GameObject("Referenced helicopter cockpit gauges",typeof(RectTransform));
        var rect=(RectTransform)panel.transform;rect.SetParent(transform,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.anchoredPosition=new Vector2(0,330);rect.sizeDelta=new Vector2(740,210);
        speed=Dial("Ground speed",new Vector2(-260,0),0,160);speedText=Label(template,"Speed readout",speed.transform,new Vector2(0,-12),new Vector2(100,40));
        attitude=Dial("Attitude",new Vector2(-130,0),0,1);attitude.attitude=true;attitudeRect=attitude.rectTransform;
        altitude=Dial("Altimeter",new Vector2(0,0),0,1000);altitudeText=Label(template,"Altitude readout",altitude.transform,new Vector2(0,-12),new Vector2(100,40));
        verticalSpeed=Dial("Vertical speed",new Vector2(130,0),-2000,2000);verticalText=Label(template,"Vertical speed readout",verticalSpeed.transform,new Vector2(0,-12),new Vector2(100,40));verticalRect=verticalSpeed.rectTransform;
        rotor=Dial("Rotor tachometer",new Vector2(260,0),0,110);rotorText=Label(template,"Rotor readout",rotor.transform,new Vector2(0,-12),new Vector2(100,40));
        DialNumbers(template,speed);DialNumbers(template,altitude);DialNumbers(template,rotor);DialNumbers(template,verticalSpeed);
        heading=Label(template,"Heading and mode",panel.transform,new Vector2(0,92),new Vector2(700,35));heading.fontSize=17;
        panel.SetActive(false);
    }
    VehicleDialGraphic Dial(string name,Vector2 position,float min,float max)
    {
        var g=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleDialGraphic));g.transform.SetParent(panel.transform,false);
        var dial=g.GetComponent<VehicleDialGraphic>();dial.rectTransform.anchoredPosition=position;dial.rectTransform.sizeDelta=new Vector2(125,125);dial.minimum=min;dial.maximum=max;dial.raycastTarget=false;return dial;
    }
    void DialNumbers(TMP_Text template,VehicleDialGraphic dial)
    {
        for(int i=0;i<5;i++)
        {
            float angle=Mathf.Lerp(225,-45,i/4f)*Mathf.Deg2Rad;
            var number=Label(template,"Scale "+i,dial.transform,new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*40,new Vector2(28,16));number.fontSize=10;
            float value=Mathf.Lerp(dial.minimum,dial.maximum,i/4f);number.text=dial==verticalSpeed?(value/1000).ToString("+0;-0;0"):value.ToString("0");
        }
    }
    static TMP_Text Label(TMP_Text template,string name,Transform parent,Vector2 position,Vector2 size)
    {
        var t=Instantiate(template,parent);t.name=name;t.rectTransform.anchorMin=t.rectTransform.anchorMax=new Vector2(.5f,.5f);t.rectTransform.anchoredPosition=position;t.rectTransform.sizeDelta=size;
        t.fontSize=13;t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;t.text="";t.gameObject.SetActive(true);return t;
    }
    public void Present(Vehicle vehicle,float groundHeight,bool show,bool cockpit)
    {
        panel.SetActive(show);if(!show)return;
        bool littleBird=vehicle.instrumentReference==Vehicle.InstrumentReference.LittleBird;
        attitudeRect.gameObject.SetActive(!littleBird);verticalRect.gameObject.SetActive(!littleBird);
        speed.rectTransform.anchoredPosition=new Vector2(littleBird?-145:-260,0);rotor.rectTransform.anchoredPosition=new Vector2(littleBird?145:260,0);
        ((RectTransform)panel.transform).localScale=Vector3.one;
        float knots=Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,Vector3.up).magnitude*1.943844f;
        float feet=groundHeight*3.28084f,climb=vehicle.Body.linearVelocity.y*196.8504f;
        var motor=(HelicopterMotor)vehicle.motor;float nr=motor.RotorPower*100;
        speed.SetValue(knots);altitude.SetValue(Mathf.Repeat(feet,1000));rotor.SetValue(nr);verticalSpeed.SetValue(climb);
        Vector3 up=vehicle.transform.InverseTransformDirection(Vector3.up);float roll=Mathf.Atan2(up.x,up.y)*Mathf.Rad2Deg;float pitch=Mathf.Asin(Mathf.Clamp(vehicle.transform.forward.y,-1,1))*Mathf.Rad2Deg;attitude.SetAttitude(pitch,roll);
        speedText.text="GS\n"+knots.ToString("0")+" KT";altitudeText.text="AGL\n"+feet.ToString("0")+" FT";rotorText.text="NR\n"+nr.ToString("0")+"%";verticalText.text="V/S\n"+climb.ToString("+0;-0;0")+" FPM";
        int hdg=Mathf.RoundToInt(Mathf.Repeat(vehicle.transform.eulerAngles.y,360))%360;
        heading.text="HDG "+hdg.ToString("000")+"°   /   "+(motor.assistEnabled?"ASSIST":"MANUAL");
    }
}
