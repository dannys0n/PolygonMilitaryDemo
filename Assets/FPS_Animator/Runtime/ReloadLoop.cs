using UnityEngine;

namespace FPSAnimatorPreview
{
    // A stationary animation preview: original rig and paired source clips.
    public class ReloadLoop : MonoBehaviour
    {
        public GameObject character;
        public GameObject weapon;
        public AnimationClip idlePose;
        public AnimationClip characterReload;
        public AnimationClip weaponReload;
        public bool useProfile;
        public ReloadProfile profile;
        [Header("Stationary lower body")]
        public Transform pelvis;
        public Transform weaponMount;
        public Transform[] lowerBodyBones;
        [Min(0f)] public float pauseBetweenReloads = 0.75f;
        [Min(0.01f)] public float blendDuration = 0.15f;
        [Min(0.01f)] public float playbackSpeed = 1f;

        private Transform[] bones;
        private Vector3[] positions;
        private Quaternion[] rotations;
        private Vector3[] scales;
        private float clock;
        private int[] lowerBodyIndices;

        void Awake() => Initialize();

        public void Initialize()
        {
            if (useProfile)
            {
                characterReload = profile.characterClip;
                weaponReload = profile.weaponClip;
                blendDuration = profile.blendInTime;
                playbackSpeed = profile.rateScale;
            }
            bones = character.GetComponentsInChildren<Transform>(true);
            positions = new Vector3[bones.Length];
            rotations = new Quaternion[bones.Length];
            scales = new Vector3[bones.Length];
            idlePose.SampleAnimation(character, 0f);
            for (int i = 0; i < bones.Length; i++)
            {
                positions[i] = bones[i].localPosition;
                rotations[i] = bones[i].localRotation;
                scales[i] = bones[i].localScale;
            }
            lowerBodyIndices = new int[lowerBodyBones.Length];
            for (int i = 0; i < lowerBodyBones.Length; i++)
                lowerBodyIndices[i] = System.Array.IndexOf(bones, lowerBodyBones[i]);
        }

        void Update()
        {
            clock += Time.deltaTime * playbackSpeed;
            Evaluate(clock % (characterReload.length + pauseBetweenReloads));
        }

        public void Evaluate(float time)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = positions[i];
                bones[i].localRotation = rotations[i];
                bones[i].localScale = scales[i];
            }
            if (time >= characterReload.length)
                return;

            characterReload.SampleAnimation(character, time);
            Vector3 hipPosition = pelvis.position;
            Quaternion hipRotation = pelvis.rotation;
            Vector3 gunPosition = weaponMount.position;
            Quaternion gunRotation = weaponMount.rotation;
            foreach (int i in lowerBodyIndices)
            {
                bones[i].localPosition = positions[i];
                bones[i].localRotation = rotations[i];
                bones[i].localScale = scales[i];
            }
            // Gun targets outside the hip hierarchy need the same correction
            // that the arms inherit when the hips return to their idle pose.
            if (!weaponMount.IsChildOf(pelvis))
            {
                Quaternion correction = pelvis.rotation * Quaternion.Inverse(hipRotation);
                weaponMount.SetPositionAndRotation(pelvis.position + correction * (gunPosition - hipPosition),
                    correction * gunRotation);
            }
            weaponReload.SampleAnimation(weapon, Mathf.Min(time, weaponReload.length));

            // Briefly blend the whole authored pose into/out of the idle pose.
            float outTime = useProfile ? profile.blendOutTime : blendDuration;
            float exitBlend = outTime > 0f ? (characterReload.length - time) / outTime : 1f;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Min(time / blendDuration, exitBlend));
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = Vector3.Lerp(positions[i], bones[i].localPosition, blend);
                bones[i].localRotation = Quaternion.Slerp(rotations[i], bones[i].localRotation, blend);
                bones[i].localScale = Vector3.Lerp(scales[i], bones[i].localScale, blend);
            }
        }

        void OnDisable()
        {
            if (bones != null)
                Evaluate(characterReload.length);
            clock = 0f;
        }
    }
}
