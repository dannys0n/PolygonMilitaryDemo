using System.Collections.Generic;
using UnityEngine;

// Local presentation of accepted shots. These effects do not affect weapon gameplay.
[DefaultExecutionOrder(200)]
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
    MuzzleEffect firstPerson, thirdPerson;
    readonly Queue<int> pendingShots = new Queue<int>();

    void Awake()
    {
        actions = GetComponent<SoldierActions>();
        loadout = GetComponent<SoldierWeaponLoadout>();
        view = GetComponent<SoldierFirstPersonView>();
        controller = GetComponent<SoldierController>();


    }
    void OnEnable() { actions.FireStarted += OnShot; loadout.PresentationChanging += ReleaseEffects; }
    void OnDisable()
    {
        actions.FireStarted -= OnShot;
        loadout.PresentationChanging -= ReleaseEffects;
        pendingShots.Clear();
        ClearEffects();
    }

    bool IsLocalOwner => controller.IsLocallyControlled && controller.inputSource && controller.inputSource.isActiveAndEnabled;
    bool CanIlluminate => loadout.HasFirstPersonPresentation && view.firstPerson && view.playerCamera.enabled && IsLocalOwner;

    void OnShot() => pendingShots.Enqueue(loadout.selectedSlot);

    void LateUpdate()
    {
        // Sample the final animated gun pose, after the view and grip rigs update.
        while (pendingShots.Count > 0) PresentShot(pendingShots.Dequeue());
    }

    void PresentShot(int index)
    {
        if(index!=loadout.selectedSlot)return;
        var definition = loadout.EquippedWeapon.definition;
        // Casings are an owner-only cosmetic effect, including when the owner uses TPS.
        if (IsLocalOwner && definition.shellCasing)
        {
            Transform port = (loadout.HasFirstPersonPresentation && view.firstPerson && view.playerCamera.enabled
                ? loadout.FirstPersonView.effects : loadout.ThirdPersonView.effects).shellEjection;
            var casing = Instantiate(definition.shellCasing, port.position, port.rotation);
            casing.Launch(port.rotation, controller.movement.controller.velocity);
        }
        // World flash remains visible to TPS viewers. FPS flash uses the owner's view layer.
        Emit(ref thirdPerson, loadout.ThirdPersonView.effects.muzzleFlash, definition, view.thirdPersonLayer, false);
        if (loadout.HasFirstPersonPresentation && view.firstPerson && view.playerCamera.enabled)
            Emit(ref firstPerson, loadout.FirstPersonView.effects.muzzleFlash, definition, view.firstPersonLayer, CanIlluminate);
    }

    void Emit(ref MuzzleEffect effect, Transform muzzle, SoldierWeaponDefinition definition, int layer, bool illuminate)
    {
        if(!definition.muzzleFlash)return;
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
            // A flash belongs to the barrel throughout its lifetime, including recoil.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            effect.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        effect.particles.Emit(definition.flashParticlesCount);
        if (!illuminate || !definition.muzzleFlashLight) return;
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
        if(firstPerson?.light!=null&&(!CanIlluminate||Time.time>=firstPerson.lightEnd))firstPerson.light.enabled=false;
    }
    void ClearEffects()
    {
        foreach(var effect in new[]{firstPerson,thirdPerson})
        {
            if(effect?.particles)effect.particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(effect?.light)effect.light.enabled=false;
        }
    }
    void ReleaseEffects()
    {
        pendingShots.Clear();ClearEffects();
        foreach(var effect in new[]{firstPerson,thirdPerson})
        {
            if(effect?.particles)Destroy(effect.particles.gameObject);
            if(effect?.light)Destroy(effect.light.gameObject);
        }
        firstPerson=thirdPerson=null;
    }
}
