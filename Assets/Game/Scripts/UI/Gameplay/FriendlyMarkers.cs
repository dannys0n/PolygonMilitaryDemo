using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Screen-space team identification shares the gameplay HUD's observed player and glow.
[DefaultExecutionOrder(750)]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaFriendlyMarkers")]
public sealed class FriendlyMarkers : MonoBehaviour
{
    public GameplayHud hud;
    sealed class Marker
    {
        public MatchParticipant actor;
        public Transform head;
        public VehicleOccupant occupant;
        public RectTransform rect;
        public Image halo, core;
    }
    readonly List<Marker> markers = new();
    MatchParticipant[] roster;
    AISpectator spectator;
    MatchParticipant scopeViewer;
    SoldierScopeView scope;
    RectTransform root;
    Canvas canvas;
    Sprite sprite;
    Texture2D texture;
    Material glow;

    void Start()
    {
        if (!hud) hud = GetComponent<GameplayHud>();
        spectator = FindAnyObjectByType<AISpectator>();
        canvas = GetComponentInParent<Canvas>();
        root = new GameObject("Friendly Markers", typeof(RectTransform)).GetComponent<RectTransform>();
        // World pins belong to the screen plane, not the decorative tilted HUD
        // panel. RectTransform projection into an angled panel displaced markers,
        // most noticeably when ADS changed the camera projection.
        root.SetParent(canvas.transform, false);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        // One tiny antialiased sprite shared by every marker; no per-frame texture work.
        texture = new Texture2D(64,64,TextureFormat.RGBA32,false) { name="Friendly diamond", filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp };
        var pixels = new Color[64*64];
        for(int y=0;y<64;y++) for(int x=0;x<64;x++)
        {
            float px=(x+.5f)/64-.5f, py=(y+.5f)/64-.5f;
            float edge=Mathf.Abs(px)/.22f + (py>.04f?(py-.04f)/.14f:(.04f-py)/.32f)-1;
            pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(.5f-edge*12));
        }
        texture.SetPixels(pixels);texture.Apply(false,true);
        sprite=Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f),64,0,SpriteMeshType.FullRect);
        if(hud.selectedIconGlow)
        {
            glow=new Material(hud.selectedIconGlow) { name="Friendly marker incandescence" };
            if(glow.HasProperty("_Glow"))glow.SetFloat("_Glow",.8f);
        }
    }
    void RefreshRoster()
    {
        if(roster==hud.match.participants)return;
        roster=hud.match.participants;
        foreach(var marker in markers)if(marker.rect)Destroy(marker.rect.gameObject);
        markers.Clear();
        foreach(var actor in roster)
        {
            if(!actor || !actor.player)continue;
            var animator=actor.player.actions.animator;
            Transform head=animator && animator.isHuman?animator.GetBoneTransform(HumanBodyBones.Head):actor.transform;
            var image=MakeImage(actor.name,root,new Vector2(28,32));
            var core=MakeImage("White-hot core",image.transform,new Vector2(15,18));
            markers.Add(new Marker { actor=actor,head=head,occupant=actor.GetComponent<VehicleOccupant>(),rect=image.rectTransform,halo=image,core=core });
        }
    }
    Image MakeImage(string name,Transform parent,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
        go.transform.SetParent(parent,false);
        var image=go.GetComponent<Image>();image.sprite=sprite;image.material=glow;image.raycastTarget=false;
        image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(.5f,.5f);
        image.rectTransform.sizeDelta=size;
        return image;
    }
    public bool IsVisible(MatchParticipant actor)
    {
        foreach(var marker in markers)if(marker.actor==actor)return marker.rect.gameObject.activeInHierarchy;
        return false;
    }
    void LateUpdate()
    {
        if(!root || !hud || !hud.match)return;
        RefreshRoster();
        var viewer=hud.player;
        bool observing=spectator && spectator.IsSpectating;
        bool visible=viewer && !viewer.health.IsDead && !viewer.IsAwaitingSpawn
            && hud.match.IsRunning && (!observing || spectator.FollowedCharacter==viewer);
        root.gameObject.SetActive(visible);
        if(!visible)return;
        if(scopeViewer!=viewer)
        {
            scopeViewer=viewer;
            scope=viewer.GetComponent<SoldierScopeView>();
        }
        var seat=viewer.GetComponent<VehicleOccupant>();
        Camera camera=observing?spectator.PresentationCamera
            :seat && seat.IsMounted && seat.ActiveVehicleCamera?seat.ActiveVehicleCamera:viewer.player.view.playerCamera;
        if(!camera){root.gameObject.SetActive(false);return;}
        Rect lensRect=default;
        bool hasLens=scope && scope.TryGetLensScreenRect(camera,out lensRect);
        if(hasLens){lensRect.xMin-=14;lensRect.xMax+=14;lensRect.yMin-=18;lensRect.yMax+=18;}
        var uiCamera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        foreach(var marker in markers)
        {
            var actor=marker.actor;
            bool show=actor && actor!=viewer && actor.team==viewer.team && !actor.health.IsDead
                && !actor.IsAwaitingSpawn && !actor.IsSpectator && actor.isActiveAndEnabled && marker.head;
            if(show && seat && seat.IsMounted && marker.occupant && marker.occupant.IsMounted
                && marker.occupant.MountedVehicle==seat.MountedVehicle)show=false;
            Vector3 screen=Vector3.zero;
            if(show)
            {
                screen=camera.WorldToScreenPoint(marker.head.position+Vector3.up*.3f);
                var rect=camera.pixelRect;
                show=screen.z>0 && screen.x>rect.xMin+14 && screen.x<rect.xMax-14 && screen.y>rect.yMin+18 && screen.y<rect.yMax-18;
                if(show && hasLens && lensRect.Contains(screen))show=false;
            }
            marker.rect.gameObject.SetActive(show);
            if(!show)continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,uiCamera,out var point);
            marker.rect.anchoredPosition=point;
            float alpha=Mathf.Lerp(1,.55f,Mathf.InverseLerp(50,600,screen.z));
            marker.halo.color=new Color(hud.accent.r,hud.accent.g,hud.accent.b,alpha);
            marker.core.color=new Color(1,.94f,.8f,alpha);
        }
    }
    void OnDisable(){if(root)root.gameObject.SetActive(false);}
    void OnDestroy()
    {
        if(root)Destroy(root.gameObject);
        if(glow)Destroy(glow);
        if(sprite)Destroy(sprite);
        if(texture)Destroy(texture);
    }
}
