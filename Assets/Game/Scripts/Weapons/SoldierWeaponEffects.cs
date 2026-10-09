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

    bool CanIlluminate => view.firstPerson && view.playerCamera.enabled &&
        controller.inputSource is SoldierLocalInput local && local.isActiveAndEnabled;

    void OnShot()
    {
        int index = loadout.selectedSlot;
        var slot = loadout.slots[index];
        var definition = slot.weapon.definition;
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
            effect.particles.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));
            foreach (Transform child in effect.particles.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            effect.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        effect.particles.Emit(definition.flashParticlesCount);
        if (!illuminate) return;
        if (effect.light == null)
        {
            effect.light = Instantiate(definition.muzzleFlashLight, muzzle);
            effect.light.transform.SetLocalPositionAndRotation(definition.flashLightOffset, Quaternion.identity);
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
