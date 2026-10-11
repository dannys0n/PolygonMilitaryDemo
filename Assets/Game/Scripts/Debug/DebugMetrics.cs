using TMPro;
using UnityEngine;
using UnityEngine.Profiling;

// Scene-level diagnostics: independent of characters, cameras and gameplay input.
public class DebugMetrics : MonoBehaviour
{
    public TMP_Text readout;
    [Min(.1f)] public float refreshInterval = .5f;
    int frames;
    float elapsed;

    void Update()
    {
        frames++;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < refreshInterval) return;
        float fps = frames / elapsed;
        float milliseconds = elapsed * 1000f / frames;
        float memory = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
        readout.text = $"<color=#FFAF00>DEBUG</color>  {fps:0} FPS  /  {milliseconds:0.0} ms  /  {memory:0} MB";
        frames = 0;
        elapsed = 0;
    }
}
