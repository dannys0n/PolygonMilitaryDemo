using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

// An optional cockpit display reuses the spectator TPS pose without changing vehicle controls.
[DefaultExecutionOrder(320)]
public class VehicleExternalViewMonitor : MonoBehaviour
{
    VehicleOccupant occupant;
    ArenaVehicleHud hud;
    RectTransform panel;
    RawImage display;
    Camera monitorCamera;
    RenderTexture texture;

    public void Build(ArenaVehicleHud owner, TMP_Text labelTemplate, Material displayMaterial)
    {
        hud = owner;
        panel = new GameObject("External vehicle monitor", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(transform, false);
        panel.SetAsFirstSibling();
        panel.anchorMin = new Vector2(.22f, .30f);
        panel.anchorMax = new Vector2(.78f, .75f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        var image = new GameObject("TPS camera feed", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
        image.transform.SetParent(panel, false);
        display = image.GetComponent<RawImage>();
        display.raycastTarget = false;
        display.material = displayMaterial;
        var fit = image.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 16f / 9f;
        var label = Instantiate(labelTemplate, panel);
        label.name = "External view label";
        label.text = "EXTERNAL VIEW";
        label.fontSize = 15;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.color = new Color(1, .55f, .12f);
        label.raycastTarget = false;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 1);
        label.rectTransform.pivot = new Vector2(0, 1);
        label.rectTransform.anchoredPosition = new Vector2(10, -8);
        label.rectTransform.sizeDelta = new Vector2(220, 25);
        label.gameObject.SetActive(true);
        panel.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        occupant = hud.player;
        bool show = occupant && occupant.IsLocal && occupant.IsMounted && occupant.CockpitView
            && occupant.MountedVehicle.cockpitExternalMonitor;
        panel.gameObject.SetActive(show);
        if (!show) { StopRendering(); return; }
        if (!monitorCamera)
        {
            monitorCamera = new GameObject("Cockpit external TPS camera").AddComponent<Camera>();
            monitorCamera.enabled = false;
        }
        if (!texture)
        {
            texture = new RenderTexture(960, 540, 24) { name = "Cockpit external view" };
            texture.Create();
            display.texture = texture;
        }
        var source = occupant.ActiveVehicleCamera;
        monitorCamera.CopyFrom(source);
        monitorCamera.GetUniversalAdditionalCameraData().renderPostProcessing = source.GetUniversalAdditionalCameraData().renderPostProcessing;
        monitorCamera.targetTexture = texture;
        monitorCamera.depth = source.depth - 1;
        monitorCamera.aspect = 16f / 9f;
        monitorCamera.nearClipPlane = .2f;
        occupant.GetSpectatorCameraPose(false, out var position, out var rotation);
        monitorCamera.transform.SetPositionAndRotation(position, rotation);
        monitorCamera.enabled = true;
    }

    void StopRendering()
    {
        if (monitorCamera) { monitorCamera.enabled = false; monitorCamera.targetTexture = null; }
        if (texture) { display.texture = null; texture.Release(); Destroy(texture); texture = null; }
    }
    void OnDisable()
    {
        StopRendering();
        if (panel) panel.gameObject.SetActive(false);
    }
    void OnDestroy()
    {
        StopRendering();
        if (monitorCamera) Destroy(monitorCamera.gameObject);
    }
}
