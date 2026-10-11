using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Uses a scene rendering preset and restores the previous quality preset on exit.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class SceneRenderPipelineOverride : MonoBehaviour
{
    public RenderPipelineAsset pipeline;
    [Tooltip("Original quality preset to restore if Unity reloads while this scene's preset is already active.")]
    public RenderPipelineAsset fallbackPipeline;
    RenderPipelineAsset previousPipeline;
    bool applied;

    void OnEnable()
    {
        if (pipeline == null) return;
        // Quality settings can retain the override through a domain reload or an editor save.
        // Never record our own preset as the preset to restore.
        previousPipeline = QualitySettings.renderPipeline == pipeline
            ? fallbackPipeline : QualitySettings.renderPipeline;
        QualitySettings.renderPipeline = pipeline;
        applied = true;
    }

    void OnDisable()
    {
        if (applied && QualitySettings.renderPipeline == pipeline)
            QualitySettings.renderPipeline = previousPipeline;
        applied = false;
    }
}
