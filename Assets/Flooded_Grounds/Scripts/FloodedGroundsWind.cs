using UnityEngine;

// Preserve the Tree Creator wind controls while using the URP foliage shaders.
[ExecuteAlways]
[RequireComponent(typeof(WindZone))]
public class FloodedGroundsWind : MonoBehaviour
{
    WindZone wind;
    void OnEnable() => wind = GetComponent<WindZone>();
    void Update()
    {
        float pulse = 1f + Mathf.Sin(Time.time * wind.windPulseFrequency * Mathf.PI * 2f) * wind.windPulseMagnitude;
        Vector3 direction = transform.forward * wind.windMain * pulse;
        Shader.SetGlobalVector("_FGWind", new Vector4(direction.x, direction.y, direction.z, wind.windTurbulence));
    }
    void OnDisable() => Shader.SetGlobalVector("_FGWind", Vector4.zero);
}
