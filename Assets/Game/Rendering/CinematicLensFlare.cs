using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
[System.Serializable, VolumeComponentMenu("Camera/Cinematic Lens Flare")]
public class CinematicLensFlare : VolumeComponent, IPostProcessComponent
{
 public ClampedFloatParameter intensity = new ClampedFloatParameter(0f,0f,2f);
 public MinFloatParameter threshold = new MinFloatParameter(1.2f,0f);
 public ClampedFloatParameter glare = new ClampedFloatParameter(.2f,0f,1f);
 public ClampedFloatParameter ghosts = new ClampedFloatParameter(.12f,0f,1f);
 public ClampedFloatParameter rays = new ClampedFloatParameter(.15f,0f,1f);
 public bool IsActive()=>active && intensity.value>0f;
}
