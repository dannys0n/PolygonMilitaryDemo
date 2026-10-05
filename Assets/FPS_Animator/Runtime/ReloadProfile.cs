using UnityEngine;

namespace FPSAnimatorPreview
{
    [CreateAssetMenu(menuName = "FPS Animator/Reload Profile")]
    public class ReloadProfile : ScriptableObject
    {
        public AnimationClip sourceClip;
        public AnimationClip characterClip;
        public AnimationClip weaponClip;
        [Min(0.01f)] public float blendInTime = 0.15f;
        [Min(0f)] public float blendOutTime = 0.15f;
        [Min(0.01f)] public float rateScale = 1f;
        public bool isAdditive;
        public AnimationCurve handAttachment;
    }
}
