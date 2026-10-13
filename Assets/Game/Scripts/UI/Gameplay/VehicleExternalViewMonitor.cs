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
        panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
        panel.pivot = new Vector2(0, 1);
        panel.anchoredPosition = new Vector2(32, -48);
        panel.sizeDelta = new Vector2(420, 452);
        var image = new GameObject("TPS camera feed", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        image.transform.SetParent(panel, false);
        display = image.GetComponent<RawImage>();
        display.raycastTarget = false;
        display.material = displayMaterial;
        var imageRect = display.rectTransform;
        imageRect.anchorMin = new Vector2(0, 1);
        imageRect.anchorMax = new Vector2(1, 1);
        imageRect.pivot = new Vector2(.5f, 1);
        imageRect.anchoredPosition = Vector2.zero;
        imageRect.sizeDelta = new Vector2(0, 420);
        var label = Instantiate(labelTemplate, panel);
        label.name = "External view label";
        label.text = "THIRD PERSON";
        label.fontSize = 15;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.color = new Color(1, .55f, .12f);
        label.raycastTarget = false;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 1);
        label.rectTransform.pivot = new Vector2(0, 1);
        label.rectTransform.anchoredPosition = new Vector2(10, -426);
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
            texture = new RenderTexture(640, 640, 24) { name = "Cockpit external view" };
            texture.Create();
            display.texture = texture;
        }
        var source = occupant.ActiveVehicleCamera;
        monitorCamera.CopyFrom(source);
        monitorCamera.GetUniversalAdditionalCameraData().renderPostProcessing = source.GetUniversalAdditionalCameraData().renderPostProcessing;
        monitorCamera.targetTexture = texture;
        monitorCamera.depth = source.depth - 1;
        monitorCamera.aspect = 1f;
        monitorCamera.fieldOfView = 85f;
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
