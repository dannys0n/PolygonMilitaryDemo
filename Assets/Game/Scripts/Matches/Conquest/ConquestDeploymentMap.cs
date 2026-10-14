using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Deployment presentation only: selection and spawn eligibility remain in the mode/menu.
[DefaultExecutionOrder(600)]
public class ConquestDeploymentMap : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
{
 public ConquestDeploymentMenu menu;
 public RawImage mapImage, previewImage;
 public RectTransform pinRoot;
 public UnityEngine.UI.Button pinTemplate;
 public TMP_Text previewTitle, mapTitle, matchSummary;
 public Material textMaterial;
 Camera mapCamera, previewCamera;
 RenderTexture mapTexture, previewTexture;
 readonly List<Pin> pins=new List<Pin>();
 readonly Dictionary<Canvas,bool> hiddenCanvases=new Dictionary<Canvas,bool>();
 ArenaParticipant[] roster;
 Vector3 center;
 float extent,height,nextPins,nextPreview;
 bool mapDirty=true;
 ArenaParticipant previousMember;
 ConquestCapturePoint previousPoint;
 class Pin {public UnityEngine.UI.Button button;public RectTransform rect;public TMP_Text label;public Image image;public ConquestCapturePoint point;public ArenaParticipant member;public bool teamBase;}
 static readonly Color Orange=new Color(1,.64f,.22f), White=new Color(.92f,.95f,.96f), Green=new Color(.6f,.84f,.42f);
 void OnEnable(){mapDirty=true;nextPins=nextPreview=0;}
 void Initialize()
 {
  var ownCanvas=GetComponentInParent<Canvas>();
  foreach(var hud in Object.FindObjectsByType<ArenaHud>(FindObjectsInactive.Include))
  {
   if(hud.gameObject.scene!=gameObject.scene)continue;
   var canvas=hud.GetComponentInParent<Canvas>();if(canvas==ownCanvas)continue;
   hiddenCanvases[canvas]=canvas.enabled;canvas.enabled=false;
  }
  // Two presentation cameras. Neither changes the observed character's camera or input.
  mapTexture=new RenderTexture(1024,768,24,RenderTextureFormat.ARGB32){name="Deployment tactical map"};mapTexture.Create();
  previewTexture=new RenderTexture(480,270,24,RenderTextureFormat.ARGB32){name="Deployment TPS preview"};previewTexture.Create();
  mapCamera=CreateCamera("Deployment map camera",mapTexture);mapCamera.orthographic=true;mapCamera.transform.rotation=Quaternion.Euler(90,0,0);mapCamera.aspect=mapTexture.width/(float)mapTexture.height;
  previewCamera=CreateCamera("Deployment preview camera",previewTexture);previewCamera.fieldOfView=60;previewCamera.aspect=previewTexture.width/(float)previewTexture.height;
  mapCamera.cullingMask &= ~(1<<9); // Characters are represented by live pins on the tactical map.
  mapImage.texture=mapTexture;previewImage.texture=previewTexture;
  var vertices=NavMesh.CalculateTriangulation().vertices;
  var bounds=new Bounds(menu.rules.points[0].transform.position,Vector3.one);
  foreach(var point in menu.rules.points)bounds.Encapsulate(point.transform.position);
  foreach(var t in menu.rules.redBaseSpawns)bounds.Encapsulate(t.position);
  foreach(var t in menu.rules.blueBaseSpawns)bounds.Encapsulate(t.position);
  foreach(var vertex in vertices)bounds.Encapsulate(vertex);
  center=bounds.center;extent=Mathf.Max(bounds.extents.z,bounds.extents.x/mapCamera.aspect)*1.08f;
  height=bounds.max.y+100;mapCamera.farClipPlane=Mathf.Max(250,height-bounds.min.y+100);
  mapCamera.transform.position=new Vector3(center.x,height,center.z);mapCamera.orthographicSize=extent;
  BuildPins();mapDirty=true;
 }
 Camera CreateCamera(string name,RenderTexture texture)
 {
  var go=new GameObject(name,typeof(Camera));var c=go.GetComponent<Camera>();c.enabled=false;c.targetTexture=texture;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.035f,.04f,.045f);c.nearClipPlane=.1f;c.farClipPlane=2000;c.cullingMask=~(1<<8);
  var data=c.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.renderShadows=true;
  return c;
 }
 Pin AddPin(string title)
 {
  var b=Instantiate(pinTemplate,pinRoot);b.name=title;b.gameObject.SetActive(true);
  var label=b.GetComponentInChildren<TMP_Text>();if(textMaterial)label.fontSharedMaterial=textMaterial;
  var p=new Pin{button=b,rect=(RectTransform)b.transform,label=label,image=b.GetComponent<Image>()};pins.Add(p);return p;
 }
 void BuildPins()
 {
  foreach(var p in pins)Destroy(p.button.gameObject);pins.Clear();roster=menu.participant.match.participants;
  var team=AddPin("Team base pin");team.teamBase=true;team.button.onClick.AddListener(menu.SelectBase);
  foreach(var point in menu.rules.points){var p=AddPin("Objective "+point.letter);p.point=point;p.button.onClick.AddListener(()=>menu.SelectFlag(point));}
  foreach(var actor in roster){if(actor.team!=menu.participant.team||actor==menu.participant)continue;var p=AddPin(actor.name+" position");p.member=actor;p.button.onClick.AddListener(()=>menu.SelectSquadmate(actor));}
  foreach(var p in pins)if(p.teamBase||p.point)p.rect.SetAsLastSibling();
 }
 void LateUpdate()
 {
  if(!menu.IsVisible)return;
  if(!mapCamera)Initialize();
  if(roster!=menu.participant.match.participants)BuildPins();
  if(mapDirty){mapCamera.Render();mapDirty=false;}
  if(Time.unscaledTime>=nextPins){nextPins=Time.unscaledTime+.15f;UpdatePins();}
  bool selectionChanged=previousPoint!=menu.SelectedPoint||previousMember!=menu.SelectedMember;
  if(selectionChanged){previousPoint=menu.SelectedPoint;previousMember=menu.SelectedMember;nextPreview=0;}
  if(Time.unscaledTime>=nextPreview){nextPreview=Time.unscaledTime+.1f;UpdatePreview();}
  if(Input.GetKeyDown(KeyCode.P)){ResetMap();}
 }
 Vector3 BasePosition(){var points=menu.participant.team==ArenaParticipant.Team.Red?menu.rules.redBaseSpawns:menu.rules.blueBaseSpawns;return points[0].position;}
 void UpdatePins()
 {
  int friendly=0;
  foreach(var p in pins)
  {
   Vector3 position;bool available=true,chosen=false,squad=false;Color color=White;
   if(p.teamBase){position=BasePosition();chosen=!menu.SelectedPoint&&!menu.SelectedMember;p.label.text="HQ";color=Orange;}
   else if(p.point){position=p.point.transform.position;available=menu.IsTacticalMap || menu.rules.CanDeploy(menu.participant,p.point);chosen=menu.SelectedPoint==p.point;p.label.text=p.point.letter+(p.point.IsContested?" !":"");color=p.point.Owner==menu.participant.team?Orange:p.point.Owner.HasValue?new Color(.9f,.4f,.32f):White;}
   else {var actor=p.member;bool alive=actor&&!actor.health.IsDead&&!actor.IsAwaitingSpawn;p.button.gameObject.SetActive(alive);if(!alive)continue;friendly++;position=actor.transform.position;squad=menu.rules.squads&&menu.rules.squads.SquadOf(actor)==menu.rules.squads.SquadOf(menu.participant);available=menu.IsTacticalMap || squad&&menu.rules.squads.CanDeploy(menu.participant,actor);chosen=menu.SelectedMember==actor;p.label.text=squad?"S":"";color=squad?Green:White;}
   var uv=mapCamera.WorldToViewportPoint(position);bool inView=uv.x>=0&&uv.x<=1&&uv.y>=0&&uv.y<=1;p.button.gameObject.SetActive(inView);if(!inView)continue;
   p.rect.anchorMin=p.rect.anchorMax=new Vector2(uv.x,uv.y);p.rect.anchoredPosition=Vector2.zero;p.rect.sizeDelta=p.member?Vector2.one*(squad?20:10):Vector2.one*(chosen?42:34);
   p.button.interactable=available;p.image.color=new Color(color.r,color.g,color.b,p.member ? .85f : chosen ? .95f : .7f);p.label.color=chosen?Color.white:color;
  }
  var match=menu.participant.match;
  if(matchSummary){string flags="";foreach(var point in menu.rules.points){string color=point.Owner==menu.participant.team?"FFAF00":point.Owner.HasValue?"DE7763":"C5CDD2";flags+="<color=#"+color+">"+point.letter+"</color>  ";}matchSummary.text="<color=#FFAF00>"+match.RedScore+"</color>  /  "+match.BlueScore+"  <size=17>"+Mathf.CeilToInt(match.TimeRemaining/60)+" MIN</size>\n<size=17>"+flags+"</size>";}
  mapTitle.text="TACTICAL MAP  /  "+friendly+" FRIENDLIES   /   WHEEL ZOOM / RMB DRAG / P RESET";
 }
 void UpdatePreview()
 {
  var member=menu.SelectedMember;
  if(member&&!member.health.IsDead&&!member.IsAwaitingSpawn)
  {
   var vehicle=member.GetComponent<VehicleOccupant>();Vector3 position;Quaternion rotation;
   if(vehicle&&vehicle.IsMounted)vehicle.GetSpectatorCameraPose(false,out position,out rotation);
   else member.player.view.GetThirdPersonCameraPose(out position,out rotation);
   previewCamera.transform.SetPositionAndRotation(position,rotation);previewTitle.text=member.name.ToUpperInvariant()+" / TPS";
  }
  else
  {
   Vector3 target=menu.SelectedPoint?menu.SelectedPoint.transform.position:BasePosition();
   Vector3 backward=menu.SelectedPoint?menu.SelectedPoint.transform.forward:menu.participant.team==ArenaParticipant.Team.Red?Vector3.forward:Vector3.back;
   Vector3 pivot=target+Vector3.up*2;Vector3 offset=Vector3.up*4-backward*10;
   if(Physics.SphereCast(pivot,.2f,offset.normalized,out var hit,offset.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))offset=offset.normalized*Mathf.Max(1,hit.distance-.25f);
   previewCamera.transform.position=pivot+offset;previewCamera.transform.rotation=Quaternion.LookRotation(pivot-previewCamera.transform.position,Vector3.up);
   previewTitle.text=menu.SelectedPoint?"OBJECTIVE "+menu.SelectedPoint.letter+" / "+menu.SelectedPoint.displayName.ToUpperInvariant():"TEAM BASE / LIVE FEED";
  }
  previewCamera.Render();
 }
 public void OnScroll(PointerEventData e){if(!mapCamera)return;mapCamera.orthographicSize=Mathf.Clamp(mapCamera.orthographicSize*Mathf.Pow(.88f,e.scrollDelta.y),extent*.25f,extent*1.4f);mapDirty=true;}
 public void OnBeginDrag(PointerEventData e){}
 public void OnDrag(PointerEventData e){if(e.button!=PointerEventData.InputButton.Right||!mapCamera)return;var rect=(RectTransform)mapImage.transform;float scale=mapCamera.orthographicSize*2/(rect.rect.height*GetComponentInParent<Canvas>().scaleFactor);mapCamera.transform.position+=new Vector3(-e.delta.x*scale,0,-e.delta.y*scale);mapDirty=true;}
 public void ResetMap(){if(!mapCamera)return;mapCamera.transform.position=new Vector3(center.x,height,center.z);mapCamera.orthographicSize=extent;mapDirty=true;}
 void OnDisable(){Release();}
 void OnDestroy(){Release();}
 void Release(){foreach(var pair in hiddenCanvases)if(pair.Key)pair.Key.enabled=pair.Value;hiddenCanvases.Clear();if(mapCamera)Destroy(mapCamera.gameObject);if(previewCamera)Destroy(previewCamera.gameObject);if(mapTexture){mapTexture.Release();Destroy(mapTexture);}if(previewTexture){previewTexture.Release();Destroy(previewTexture);}mapCamera=previewCamera=null;mapTexture=previewTexture=null;mapImage.texture=previewImage.texture=null;}
}
