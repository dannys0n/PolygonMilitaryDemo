using UnityEngine;

// A small monitor reuses its last image between captures. Camera activation no
// longer doubles as ownership of the FPS animation/presentation graph.
[DefaultExecutionOrder(1200)]
public sealed class SpectatorInsetRenderer : MonoBehaviour
{
    public ArenaAISpectator spectator;
    Camera previous;
    SoldierScopeView scope;
    float nextCapture;
    void LateUpdate()
    {
        var camera = spectator ? spectator.InsetCamera : null;
        if (!camera || !camera.gameObject.activeInHierarchy || !camera.targetTexture) { previous = null; return; }
        if (camera != previous) { previous = camera; scope = camera.GetComponentInParent<SoldierScopeView>(); nextCapture = 0; }
        if (Time.unscaledTime < nextCapture) return;
        nextCapture = Time.unscaledTime + 1f / Mathf.Max(1, spectator.insetFramesPerSecond);
        // An FPS preview may depend on a PiP lens. Capture that dependency first.
        if (scope && scope.HasActiveLens) PreviewCameraRenderer.Render(scope.ScopeCamera);
        PreviewCameraRenderer.Render(camera);
    }
}
