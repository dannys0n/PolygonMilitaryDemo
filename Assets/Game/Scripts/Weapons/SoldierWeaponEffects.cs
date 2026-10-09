using UnityEngine;

// Local presentation of accepted shots. These effects do not affect weapon gameplay.
public class SoldierWeaponEffects : MonoBehaviour
{
    class MuzzleEffect
    {
        public ParticleSystem particles;
        public Light light;
        public float lightEnd;
    }

    SoldierActions actions;
    SoldierWeaponLoadout loadout;
    SoldierFirstPersonView view;
    SoldierController controller;
    MuzzleEffect[] firstPerson, thirdPerson;

    void Awake()
    {
        actions = GetComponent<SoldierActions>();
        loadout = GetComponent<SoldierWeaponLoadout>();
        view = GetComponent<SoldierFirstPersonView>();
        controller = GetComponent<SoldierController>();
        firstPerson = new MuzzleEffect[loadout.slots.Length];
        thirdPerson = new MuzzleEffect[loadout.slots.Length];
    }
    void OnEnable() => actions.FireStarted += OnShot;
    void OnDisable()
    {
        actions.FireStarted -= OnShot;
        ClearEffects();
    }

    bool IsLocalOwner => controller.inputSource is SoldierLocalInput local && local.isActiveAndEnabled;
    bool CanIlluminate => view.firstPerson && view.playerCamera.enabled && IsLocalOwner;

    void OnShot()
    {
        int index = loadout.selectedSlot;
        var slot = loadout.slots[index];
        var definition = slot.weapon.definition;
        // Casings are an owner-only cosmetic effect, including when the owner uses TPS.
        if (IsLocalOwner)
        {
            Transform muzzle = view.firstPerson && view.playerCamera.enabled ? slot.firstPersonMuzzle : slot.thirdPersonMuzzle;
            Instantiate(definition.shellCasing, muzzle.position + muzzle.rotation * definition.casingEjectionOffset, muzzle.rotation);
        }
        // World flash remains visible to TPS viewers. FPS flash uses the owner's view layer.
        Emit(ref thirdPerson[index], slot.thirdPersonMuzzle, definition, view.thirdPersonLayer, false);
        if (view.firstPerson && view.playerCamera.enabled)
            Emit(ref firstPerson[index], slot.firstPersonMuzzle, definition, view.firstPersonLayer, CanIlluminate);
    }

    void Emit(ref MuzzleEffect effect, Transform muzzle, SoldierWeaponDefinition definition, int layer, bool illuminate)
    {
        if (effect == null)
        {
            effect = new MuzzleEffect();
            effect.particles = Instantiate(definition.muzzleFlash, muzzle);
            // Mesh particles already face +Z after the source renderer's mesh alignment.
            // Imported weapon bones may inherit a scale of 100; effects use metre units.
            effect.particles.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Vector3 scale = muzzle.lossyScale;
            Vector3 authoredScale = definition.muzzleFlash.transform.localScale;
            effect.particles.transform.localScale = new Vector3(authoredScale.x / scale.x,
                authoredScale.y / scale.y, authoredScale.z / scale.z);
            foreach (Transform child in effect.particles.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            var main = effect.particles.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            effect.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        effect.particles.Emit(definition.flashParticlesCount);
        if (!illuminate) return;
        if (effect.light == null)
        {
            effect.light = Instantiate(definition.muzzleFlashLight, muzzle);
            effect.light.transform.SetPositionAndRotation(muzzle.position + muzzle.rotation * definition.flashLightOffset, muzzle.rotation);
            effect.light.cullingMask = 1 << view.firstPersonLayer;
            effect.light.shadows = LightShadows.None;
        }
        effect.light.enabled = true;
        effect.lightEnd = Time.time + definition.flashLightDuration;
    }

    void Update()
    {
        // Ownership/view changes and automatic fire cannot leave a light enabled.
        for (int i = 0; i < firstPerson.Length; i++)
        {
            var effect = firstPerson[i];
            if (effect?.light != null && (!CanIlluminate || i != loadout.selectedSlot || Time.time >= effect.lightEnd))
                effect.light.enabled = false;
        }
    }

    void ClearEffects()
    {
        foreach (var array in new[] { firstPerson, thirdPerson })
            foreach (var effect in array)
            {
                if (effect == null) continue;
                effect.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (effect.light != null) effect.light.enabled = false;
            }
    }
}
