using UnityEngine;
using UnityEngine.UI;

// Gameplay and the armory display the same cone that the shot resolver uses.
[DefaultExecutionOrder(150)]
public sealed class WeaponCrosshair : MonoBehaviour
{
    public SoldierLife actor;
    public Graphic[] normalSegments;
    public Image[] shotgunSegments;
    LauncherCrosshair launcher;
    ShotgunCrosshair shotgun;
    RectTransform root;
    Canvas canvas;
    float visibility = 1f;
    Camera aimCamera;
    SoldierLife cameraOwner;
    Vector2[] normalDirections;

    public static WeaponCrosshair Attach(RectTransform root, Graphic[] normal, Image[] shotgun)
    {
        var reticle = root.GetComponent<WeaponCrosshair>();
        if (!reticle) reticle = root.gameObject.AddComponent<WeaponCrosshair>();
        reticle.normalSegments = normal; reticle.shotgunSegments = shotgun;
        return reticle;
    }
    void Awake() { root = (RectTransform)transform; canvas = GetComponentInParent<Canvas>(); }
    void Start()
    {
        normalDirections = new Vector2[normalSegments.Length];
        for (int i = 0; i < normalDirections.Length; i++) normalDirections[i] = normalSegments[i].rectTransform.anchoredPosition.normalized;
        launcher = new GameObject("Launcher reticle", typeof(RectTransform), typeof(CanvasRenderer), typeof(LauncherCrosshair)).GetComponent<LauncherCrosshair>();
        launcher.transform.SetParent(transform, false);
        launcher.rectTransform.sizeDelta = new Vector2(100f, 100f);
        launcher.color = Color.white; launcher.raycastTarget = false;
        foreach(var segment in shotgunSegments)segment.enabled=false;
        shotgun=new GameObject("Shotgun reticle",typeof(RectTransform),typeof(CanvasRenderer),typeof(ShotgunCrosshair)).GetComponent<ShotgunCrosshair>();
        shotgun.transform.SetParent(transform,false);shotgun.color=Color.white;shotgun.raycastTarget=false;
    }
    void LateUpdate()
    {
        if (!actor || !actor.loadout || actor.loadout.slots.Length == 0 || !launcher) return;
        if (cameraOwner != actor) { cameraOwner = actor; aimCamera = actor.GetComponent<SoldierHitscan>().aimCamera; }
        var definition = actor.loadout.slots[actor.loadout.selectedSlot].definition;
        bool visible = !actor.health.IsDead && !actor.view.WantsToAim && !actor.movement.IsRunning && !actor.loadout.IsSwitching;
        visibility = Mathf.Lerp(visibility, visible ? 1f : 0f, 1f - Mathf.Exp(-Time.deltaTime * 8f));
        root.localScale = Vector3.one * visibility;
        bool show = visibility > .15f;
        bool isLauncher = definition.UsesLauncherCrosshair;
        foreach (var s in normalSegments) s.enabled = show && !isLauncher && !definition.UsesSpread;
        shotgun.enabled = show && !isLauncher && definition.UsesSpread;
        launcher.enabled = show && isLauncher;
        if (!show || !aimCamera) return;
        float pixels = Mathf.Tan(actor.actions.HipSpreadAt(Time.time) * Mathf.Deg2Rad)
            * aimCamera.pixelHeight * .5f / Mathf.Tan(aimCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var center = RectTransformUtility.WorldToScreenPoint(uiCamera, root.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, center, uiCamera, out var origin);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, center + Vector2.right * pixels, uiCamera, out var right);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, center + Vector2.up * pixels, uiCamera, out var up);
        float x = (right - origin).magnitude * visibility, y = (up - origin).magnitude * visibility;
        if (definition.UsesSpread)
        {
            shotgun.radius = pixels * visibility;
            float width = 1f;
            foreach(var segment in normalSegments)
                if(segment is CrosshairStroke stroke)
                {
                    var rect=segment.rectTransform;
                    var unit=RectTransformUtility.WorldToScreenPoint(uiCamera,rect.TransformPoint(rect.rect.center+Vector2.right));
                    var pivot=RectTransformUtility.WorldToScreenPoint(uiCamera,rect.TransformPoint(rect.rect.center));
                    width=stroke.thickness*Vector2.Distance(pivot,unit);
                    break;
                }
            shotgun.thickness = width;
        }
        else for (int i = 0; i < normalSegments.Length; i++)
        {
            var direction = normalDirections[i];
            var segment = normalSegments[i];
            var rect = segment.rectTransform;
            Vector2 halfSize = rect.rect.size * .5f;
            if (segment is CrosshairStroke stroke)
                halfSize = (stroke.horizontal ? new Vector2(stroke.length, stroke.thickness)
                    : new Vector2(stroke.thickness, stroke.length)) * .5f;
            // The empty gap represents the cone; the stroke begins at its boundary.
            rect.anchoredPosition = new Vector2(direction.x * (x + halfSize.x), direction.y * (y + halfSize.y));
        }
        launcher.radius = Mathf.Max(22f, Mathf.Max(x, y));
    }
}
