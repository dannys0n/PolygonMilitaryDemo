using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Menus expose static dust with their filaments. Gameplay reuses the texture only on damage.
[RequireComponent(typeof(Image))]
public class ArenaMenuAtmosphere : MonoBehaviour
{
    public ArenaMenuPresentation menu;
    public Image[] localFilaments = System.Array.Empty<Image>();
    Image image;
    Material source, instance;
    Camera menuCamera;
    ArenaHud hud;
    ArenaVehicleHud vehicleHud;
    SoldierHealth observedHealth;
    bool damageOverlay;
    float damageUntil, damageAmount;
    const float DamageDuration = 1.6f;
    readonly List<Image> pageLines = new List<Image>();
    readonly Vector4[] filaments = new Vector4[6];
    readonly float[] strengths = new float[6];
    static readonly int Filaments = Shader.PropertyToID("_Filaments");
    static readonly int Strengths = Shader.PropertyToID("_FilamentStrengths");

    void OnEnable()
    {
        image = GetComponent<Image>();
        source = image.material;
        instance = new Material(source);
        image.material = instance;
        menuCamera = image.canvas.worldCamera;
        damageOverlay = source.HasProperty("_Mode") && Mathf.RoundToInt(source.GetFloat("_Mode")) == 5;
        if(damageOverlay)
        {
            hud = GetComponentInParent<ArenaHud>();
            vehicleHud = GetComponentInParent<ArenaVehicleHud>();
            instance.SetColor("_Warmth", ArenaHudTheme.Accent);
            image.raycastTarget = false;
            image.enabled = false;
            BindHealth();
        }
        pageLines.Clear();
        if (menu) foreach (var page in FindAnyObjectByType<ArenaMenuNavigation>().pages)
            foreach (var feedback in page.GetComponentsInChildren<ArenaMenuSelectionFeedback>(true))
                if (feedback.highlightLine) pageLines.Add(feedback.highlightLine);
    }

    void LateUpdate()
    {
        if(damageOverlay)
        {
            BindHealth();
            // The ordinary gameplay HUD owns damage feedback. Vehicle HUDs keep
            // their old dust graphic dark instead of drawing a duplicate overlay.
            if(!hud){image.enabled=false;return;}
            // Brief hold, then a smooth fade. Disable the graphic completely at
            // rest so this costs no full-screen transparency during normal play.
            float remaining = Mathf.Clamp01((damageUntil-Time.unscaledTime) / (DamageDuration-.15f));
            float alpha = Mathf.SmoothStep(0, 1, remaining) * damageAmount * .5f;
            var tint = Color.white; tint.a = alpha;
            image.color = tint;
            image.enabled = alpha > .001f;
            return;
        }
        // Reuse the same six light slots on whichever menu is visible.
        System.Array.Clear(strengths, 0, strengths.Length);
        int count = 0;
        if (menu) foreach (var button in menu.mainButtons)
        {
            var feedback = button.GetComponent<ArenaMenuButtonFeedback>();
            var marker = feedback.focusEdge;
            if (marker.gameObject.activeInHierarchy && marker.color.a > .001f)
                AddLight(marker, marker.color.a * Mathf.Clamp01(marker.rectTransform.rect.height / feedback.markerHeight), ref count);
        }
        foreach (var marker in pageLines)
            if (marker.gameObject.activeInHierarchy && marker.color.a > .001f)
                AddLight(marker, marker.color.a, ref count);
        foreach (var marker in localFilaments)
            if (marker.gameObject.activeInHierarchy && marker.color.a > .001f)
                AddLight(marker, marker.color.a, ref count);
        instance.SetVectorArray(Filaments, filaments);
        instance.SetFloatArray(Strengths, strengths);
    }

    void BindHealth()
    {
        var next = hud && hud.player ? hud.player.health
            : vehicleHud && vehicleHud.player ? vehicleHud.player.GetComponent<SoldierHealth>() : null;
        if(next==observedHealth)return;
        if(observedHealth)observedHealth.Damaged-=OnDamage;
        observedHealth=next;
        damageUntil=damageAmount=0;
        if(observedHealth)observedHealth.Damaged+=OnDamage;
    }

    void OnDamage(float amount, GameObject source)
    {
        if(Time.unscaledTime>=damageUntil)damageAmount=0;
        float strength = Mathf.Clamp01(amount / Mathf.Max(1, observedHealth.maxHealth) * 2f);
        damageAmount = Mathf.Clamp01(Mathf.Max(.25f, damageAmount * .5f) + strength);
        damageUntil = Time.unscaledTime + DamageDuration;
    }

    void AddLight(Image marker, float strength, ref int count)
    {
        if (count == filaments.Length) return;
        var rect = marker.rectTransform;
        bool horizontal = rect.rect.width > rect.rect.height;
        Vector3 start = ViewportPoint(rect.TransformPoint(horizontal
            ? new Vector3(rect.rect.xMin, rect.rect.center.y, 0f)
            : new Vector3(rect.rect.center.x, rect.rect.yMax, 0f)));
        Vector3 end = ViewportPoint(rect.TransformPoint(horizontal
            ? new Vector3(rect.rect.xMax, rect.rect.center.y, 0f)
            : new Vector3(rect.rect.center.x, rect.rect.yMin, 0f)));
        filaments[count] = new Vector4(start.x, start.y, end.x, end.y);
        strengths[count++] = strength;
    }

    Vector3 ViewportPoint(Vector3 world)
    {
        if (menuCamera) return menuCamera.WorldToViewportPoint(world);
        var screen = RectTransformUtility.WorldToScreenPoint(null, world);
        return new Vector3(screen.x / Screen.width, screen.y / Screen.height, 0f);
    }

    void OnDisable()
    {
        if(observedHealth)observedHealth.Damaged-=OnDamage;
        observedHealth=null;
        if(image){image.material = source;if(damageOverlay)image.enabled=false;}
        Destroy(instance);
    }
}
