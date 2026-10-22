using UnityEngine;
using UnityEngine.UI;

// A small screen-space ring: hit origins are snapshots, never tracked attackers.
public sealed class ArenaDamageDirection : MaskableGraphic
{
    public ArenaHud hud;
    [Min(.1f)] public float duration = 1.2f;
    [Min(20f)] public float radius = 185f;
    [Range(10f,120f)] public float arcDegrees = 62f;
    [Min(1f)] public float thickness = 8f;
    struct Hit { public Vector3 origin; public float time, strength; public bool active; }
    readonly Hit[] hits = new Hit[4];
    SoldierHealth observed;
    ArenaAISpectator spectator;
    float cameraYaw;
    bool showing;

    protected override void Awake()
    {
        base.Awake(); raycastTarget = false;
        color = new Color(1f,.55f,.12f);
        spectator = FindAnyObjectByType<ArenaAISpectator>();
    }
    protected override void OnDisable()
    {
        if(observed)observed.DirectionalDamage -= OnDamage;
        observed = null; Clear(); base.OnDisable();
    }
    void Clear() { System.Array.Clear(hits,0,hits.Length); showing=false; SetVerticesDirty(); }
    void OnDamage(float amount, Vector3 origin)
    {
        int index=-1; float oldest=float.PositiveInfinity;
        for(int i=0;i<hits.Length;i++)
        {
            if(hits[i].active && (hits[i].origin-origin).sqrMagnitude<4f){index=i;break;}
            if(!hits[i].active){index=i;oldest=float.NegativeInfinity;}
            else if(hits[i].time<oldest){oldest=hits[i].time;index=i;}
        }
        hits[index]=new Hit {origin=origin,time=Time.unscaledTime,strength=Mathf.Clamp(.5f+amount/Mathf.Max(1,observed.maxHealth),.5f,1f),active=true};
        SetVerticesDirty();
    }
    void LateUpdate()
    {
        if(!hud){Destroy(gameObject);return;}
        var next=hud.player?hud.player.health:null;
        if(next!=observed)
        {
            if(observed)observed.DirectionalDamage-=OnDamage;
            observed=next; Clear();
            if(observed)observed.DirectionalDamage+=OnDamage;
        }
        var actor=hud.player;
        bool visible=hud.isActiveAndEnabled && actor && observed && !observed.IsDead && !actor.IsAwaitingSpawn
            && hud.match && hud.match.IsRunning && (!spectator || !spectator.IsSpectating || spectator.FollowedCharacter==actor);
        Camera view=null;
        if(visible)
        {
            var seat=actor.GetComponent<VehicleOccupant>();
            view=spectator && spectator.IsSpectating?spectator.PresentationCamera
                :seat && seat.IsMounted && seat.ActiveVehicleCamera?seat.ActiveVehicleCamera:actor.player.view.playerCamera;
        }
        bool active=false;
        for(int i=0;i<hits.Length;i++)
        {
            if(hits[i].active && Time.unscaledTime-hits[i].time>=duration)hits[i].active=false;
            active|=hits[i].active;
        }
        bool wasShowing=showing; showing=visible && view && active;
        if(showing)
        {
            var forward=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up);
            if(forward.sqrMagnitude<.0001f)forward=Vector3.Cross(view.transform.right,Vector3.up);
            cameraYaw=Mathf.Atan2(forward.x,forward.z);
        }
        if(showing || wasShowing)SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if(!showing || !hud || !hud.player)return;
        const int segments=32, bands=5;
        for(int h=0;h<hits.Length;h++)
        {
            var hit=hits[h];if(!hit.active)continue;
            var delta=hit.origin-hud.player.transform.position;
            if(delta.x*delta.x+delta.z*delta.z<.0001f)continue;
            float angle=Mathf.Atan2(delta.x,delta.z)-cameraYaw;
            float life=Mathf.Clamp01((Time.unscaledTime-hit.time-.1f)/Mathf.Max(.01f,duration-.1f));
            float fade=(1-life)*(1-life)*hit.strength;
            int start=vh.currentVertCount;
            for(int i=0;i<=segments;i++)
            {
                float t=(float)i/segments, taper=Mathf.Sin(t*Mathf.PI);
                float theta=angle+(t-.5f)*arcDegrees*Mathf.Deg2Rad;
                for(int b=0;b<bands;b++)
                {
                    float offset=(b-2)*thickness*.65f;
                    float alpha=b==0||b==4?0f:b==2?1f:.25f;
                    var tint=color;tint.a*=fade*alpha*Mathf.Pow(taper,.55f);
                    float r=radius+offset*Mathf.Max(.2f,taper);
                    vh.AddVert(new Vector3(Mathf.Sin(theta)*r,Mathf.Cos(theta)*r),tint,Vector2.zero);
                }
            }
            for(int i=0;i<segments;i++)for(int b=0;b<bands-1;b++)
            {int a=start+i*bands+b;vh.AddTriangle(a,a+bands,a+1);vh.AddTriangle(a+1,a+bands,a+bands+1);}
        }
    }
}
