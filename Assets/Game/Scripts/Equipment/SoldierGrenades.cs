using UnityEngine;
[DefaultExecutionOrder(-25)]
public class SoldierGrenades : MonoBehaviour
{
    public GrenadeDefinition[] types;
    public SoldierLife life;
    public Transform firstPersonHand,thirdPersonHand;
    public Vector3 handPosition=new Vector3(.085f,.04f,0),handRotation=new Vector3(90,0,0);
    public float throwDuration=3.2f,releaseTime=1.816667f;
    public int Selected { get; private set; }
    public bool IsThrowing { get; private set; }
    public float FlashAlpha=>flashStrength*Mathf.Clamp01((flashUntil-Time.time)/Mathf.Max(.1f,flashDuration));
    int[] counts;float started,flashUntil,flashStrength,flashDuration;
    bool released;GrenadeDefinition throwing;
    GameObject firstHeld,thirdHeld;
    void Awake(){counts=new int[types.Length];ResetEquipment();}
    void OnEnable()=>life.Respawned+=ResetEquipment;
    void OnDisable(){life.Respawned-=ResetEquipment;CancelThrow();}
    public int Count(int type)=>counts[type];
    public void Cycle(){if(!IsThrowing)Selected=(Selected+1)%types.Length;}
    void ResetEquipment(){for(int i=0;i<types.Length;i++)counts[i]=types[i].startingCount;flashUntil=flashStrength=0;}
    public bool TryThrow()
    {
        if(!life.actions.enabled||life.health.IsDead||life.actions.IsReloading||life.loadout.IsSwitching||IsThrowing||counts[Selected]==0)return false;
        throwing=types[Selected];counts[Selected]--;started=Time.time;released=false;IsThrowing=true;
        life.view.CancelAim();
        life.loadout.SetEquippedVisible(false);
        firstHeld=Held(firstPersonHand,life.view.firstPersonLayer);thirdHeld=Held(thirdPersonHand,life.view.thirdPersonLayer);
        PlayThrow(life.actions.animator);
        PlayThrow(life.view.armsAnimator);
        return true;
    }
    static void PlayThrow(Animator animator)
    {
        int layer=animator.GetLayerIndex("Grenade");
        animator.SetLayerWeight(layer,1);
        animator.Play("Grenade.Throw",layer,0);
    }
    static void ClearThrow(Animator animator)
    {
        int layer=animator.GetLayerIndex("Grenade");
        animator.Play("Grenade.Empty",layer,0);
        animator.SetLayerWeight(layer,0);
    }
    GameObject Held(Transform hand,int layer)
    {
        // Held grenades contain only art; the released prefab owns physics and the fuse.
        var go=new GameObject("Held "+throwing.displayName);
        go.transform.SetParent(hand,false);
        foreach(Transform visual in throwing.projectile.transform)
            Instantiate(visual.gameObject,go.transform,false);
        go.transform.SetLocalPositionAndRotation(handPosition,Quaternion.Euler(handRotation));
        foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
        return go;
    }
    void Update()
    {
        // Change to Equip before Animator evaluates, so the restored gun is
        // never rendered for a frame in the final grenade pose.
        if(IsThrowing && Time.time-started>=throwDuration)
        {
            CancelThrow();
            life.loadout.EquipCurrentWeapon();
        }
    }
    void LateUpdate()
    {
        if(!IsThrowing)return;
        foreach(var renderer in firstHeld.GetComponentsInChildren<Renderer>(true))renderer.enabled=life.view.firstPerson;
        if(!released&&Time.time-started>=releaseTime)
        {
            released=true;firstHeld.SetActive(false);thirdHeld.SetActive(false);
            var camera=life.look.aimCamera;Vector3 direction=camera.transform.forward;
            Transform held=life.view.firstPerson?firstHeld.transform:thirdHeld.transform;
            Vector3 origin=held.position;
            var projectile=Instantiate(throwing.projectile,origin,held.rotation);
            projectile.Launch(throwing,gameObject,direction*throwing.throwSpeed+Vector3.up*throwing.upwardSpeed+life.controller.velocity*.5f);
        }

    }
    public void CancelThrow()
    {
        if(!IsThrowing)return;
        if(!released)counts[System.Array.IndexOf(types,throwing)]++;
        IsThrowing=false;
        Destroy(firstHeld);Destroy(thirdHeld);
        ClearThrow(life.actions.animator);
        ClearThrow(life.view.armsAnimator);
        life.loadout.SetEquippedVisible(true);
    }
    public void ApplyFlash(float strength,float duration)
    {
        if(Time.time>=flashUntil)flashStrength=0;
        flashStrength=Mathf.Max(flashStrength,strength);flashDuration=duration;flashUntil=Mathf.Max(flashUntil,Time.time+duration*strength);
    }
}
