using TMPro;
using UnityEngine;
using UnityEngine.UI;

// View-only seeker cues. Target eligibility and firing permission stay in VehicleLauncher.
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaVehicleLockHud")]
public sealed class VehicleLockHud : MonoBehaviour
{
    RectTransform panel, targetMarker;
    VehicleLockSymbolGraphic symbol;
    TMP_Text status, targetReadout;
    Canvas canvas;
    readonly Color accent=new Color(1,.55f,.12f);

    public void Build(TMP_Text template)
    {
        panel=new GameObject("Missile seeker HUD",typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(transform,false);panel.anchorMin=Vector2.zero;panel.anchorMax=Vector2.one;
        panel.offsetMin=panel.offsetMax=Vector2.zero;
        targetMarker=new GameObject("Tracked target diamond",typeof(RectTransform),typeof(CanvasRenderer),typeof(VehicleLockSymbolGraphic)).GetComponent<RectTransform>();
        targetMarker.SetParent(panel,false);targetMarker.anchorMin=targetMarker.anchorMax=new Vector2(.5f,.5f);
        targetMarker.sizeDelta=new Vector2(110,110);
        symbol=targetMarker.GetComponent<VehicleLockSymbolGraphic>();symbol.raycastTarget=false;
        status=Label(template,panel,"Seeker status",new Vector2(0,-110),new Vector2(340,48));
        targetReadout=Label(template,targetMarker,"Target range and lock",new Vector2(0,-70),new Vector2(250,42));
        canvas=GetComponentInParent<Canvas>();
        panel.gameObject.SetActive(false);
    }
    static TMP_Text Label(TMP_Text template,Transform parent,string name,Vector2 pos,Vector2 size)
    {
        var label=Instantiate(template,parent);label.name=name;
        var rect=label.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
        rect.anchoredPosition=pos;rect.sizeDelta=size;
        label.fontSize=16;label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
        label.text="";label.gameObject.SetActive(true);return label;
    }
    public void Present(VehicleOccupant viewer,Camera camera)
    {
        VehicleLauncher launcher=null;
        if(viewer && viewer.IsMounted)
        {
            var vehicle=viewer.MountedVehicle;var systems=vehicle.GetComponent<VehicleWeaponSystems>();
            var selected=systems?systems.Current(viewer):null;
            launcher=systems?selected?.launcher:vehicle.GetComponent<VehicleLauncher>();
            if(launcher && launcher.controlRole!=vehicle.seats[viewer.SeatIndex].role)launcher=null;
        }
        bool show=launcher && launcher.requiresLock && camera;
        panel.gameObject.SetActive(show);if(!show)return;
        bool lost=Time.time-launcher.LastLockLostTime<.8f;
        bool rejected=Time.time-launcher.LastRejectedFireTime<.65f;
        status.color=lost||rejected?accent:Color.white;
        status.text=launcher.Missiles==0?"MISSILES EMPTY  /  RELOAD "+launcher.RechargeRemaining.ToString("F1")+" S"
            :launcher.ReadyIn>0?"MISSILE READY IN "+launcher.ReadyIn.ToString("F1")+" S"
            :lost?"LOCK LOST":rejected&&!launcher.IsLocked?"LOCK REQUIRED"
            :launcher.LockTarget?"":"SEEKER SEARCH  /  AIM AT HOSTILE AIRCRAFT";
        var target=launcher.LockTarget;
        bool visible=target && !target.IsDestroyed;
        Vector3 screen=visible?camera.WorldToScreenPoint(VehicleMissileGuidance.TargetPoint(target)):Vector3.zero;
        visible &= screen.z>0 && camera.pixelRect.Contains(new Vector2(screen.x,screen.y));
        targetMarker.gameObject.SetActive(visible);if(!visible)return;
        var uiCamera=canvas && canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(panel,screen,uiCamera,out var local);
        targetMarker.anchoredPosition=local;
        bool locked=launcher.IsLocked;
        symbol.SetState(launcher.LockProgress,locked,locked?accent:Color.white);
        targetReadout.color=locked?accent:Color.white;
        float range=Vector3.Distance(launcher.RequestedAimRay.origin,VehicleMissileGuidance.TargetPoint(target));
        targetReadout.text=(locked?"LOCKED  /  [LMB] FIRE":"ACQUIRING  "+Mathf.RoundToInt(launcher.LockProgress*100)+"%")
            +"\n"+Mathf.RoundToInt(range)+" M";
    }
}

// Vector HUD geometry keeps the seeker diamond and progress arc sharp at every resolution.
public sealed class VehicleLockSymbolGraphic : MaskableGraphic
{
    float progress;
    bool locked;
    public void SetState(float fraction,bool ready,Color tint)
    {progress=Mathf.Clamp01(fraction);locked=ready;color=tint;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();float radius=Mathf.Lerp(42,30,Mathf.SmoothStep(0,1,progress));
        var corners=new[]{Vector2.up,Vector2.right,Vector2.down,Vector2.left};
        for(int i=0;i<4;i++)Line(mesh,corners[i]*radius,corners[(i+1)%4]*radius,2);
        if(locked)
            for(int i=0;i<4;i++)Line(mesh,corners[i]*(radius+5),corners[(i+1)%4]*(radius+5),.8f);
        else
        {
            int segments=Mathf.CeilToInt(progress*64);
            for(int i=0;i<segments;i++)
            {
                float a=(90-i*360f/64)*Mathf.Deg2Rad,b=(90-Mathf.Min((i+1)/64f,progress)*360)*Mathf.Deg2Rad;
                Line(mesh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*50,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*50,2);
            }
        }
    }
    void Line(VertexHelper mesh,Vector2 a,Vector2 b,float width)
    {
        Vector2 n=(b-a).normalized; n=new Vector2(-n.y,n.x)*(width*.5f);
        int first=mesh.currentVertCount;
        mesh.AddVert(a-n,color,Vector2.zero);mesh.AddVert(a+n,color,Vector2.zero);
        mesh.AddVert(b+n,color,Vector2.zero);mesh.AddVert(b-n,color,Vector2.zero);
        mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
    }
}
