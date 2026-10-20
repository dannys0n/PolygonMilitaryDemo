using System.Linq;
using UnityEngine;

// Isolated, lazily spawned FPS/TPS animation inspection alongside normal armory gameplay.
public sealed class WeaponConversionPreview : MonoBehaviour
{
    public WeaponAnimationConversionProof[] proofs;
    public string[] proofPaths, proofLabels;
    WeaponAnimationConversionProof activeProof;
    public int Count=>proofPaths!=null&&proofPaths.Length>0?proofPaths.Length:proofs?.Length??0;
    WeaponAnimationConversionProof LoadProof(int index)
    {
#if UNITY_EDITOR
        if(proofPaths!=null&&proofPaths.Length>0)return UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponAnimationConversionProof>(proofPaths[index]);
#endif
        return proofs[index];
    }
    public Camera observer;
    public int selected;
    public bool thirdPerson, playing=true, reloading=true;
    public bool openOnStart;
    [Range(0,1)] public float progress;
    [Range(0,2)] public float speed=1;
    public bool Inspecting { get; private set; }
    public GameObject Model { get; private set; }
    GameObject[] hidden;
    CursorLockMode savedLock;
    bool savedCursor;
    string[] names;
    Vector2 selectionScroll;
    float yaw=155,pitch=8,distance=2.4f;
    AnimationClip Clip=>thirdPerson?(reloading?activeProof.bodyReload:activeProof.bodyIdle):(reloading?activeProof.reload:activeProof.idle);
    void Awake()=>names=proofLabels!=null&&proofLabels.Length>0?proofLabels:proofs.Select(p=>p.converted.weapon.displayName).ToArray();
    void Start(){if(openOnStart)Toggle();}
    public void Toggle()
    {
        Inspecting=!Inspecting;
        if(Inspecting)
        {
            savedLock=Cursor.lockState;savedCursor=Cursor.visible;
            hidden=gameObject.scene.GetRootGameObjects().Where(r=>r!=gameObject&&r.activeSelf).ToArray();foreach(var root in hidden)root.SetActive(false);
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            foreach(Transform child in transform)child.gameObject.SetActive(true);
            Select(selected,thirdPerson);
        }
        else
        {
            if(Model)Destroy(Model);Model=null;
            foreach(Transform child in transform)child.gameObject.SetActive(false);
            if(hidden!=null)foreach(var root in hidden)if(root)root.SetActive(true);
            Cursor.lockState=savedLock;Cursor.visible=savedCursor;
        }
    }
    public void Select(int index,bool body)
    {
        selected=Mathf.Clamp(index,0,Count-1);thirdPerson=body;
        if(Model){Model.SetActive(false);Destroy(Model);}
        var proof=activeProof=LoadProof(selected);
        if(thirdPerson&&proof.sharedBodyPrefab&&proof.sharedBodyWeaponPrefab)
        {
            Model=Instantiate(proof.sharedBodyPrefab,transform);
            var view=proof.converted.firstPersonPrefab;
            var source=proof.sharedBodyWeaponPrefab;
            var gun=Instantiate(source,Model.transform).transform;gun.name="SourceWeapon";
            gun.SetLocalPositionAndRotation(proof.bodyWeaponPosition,Quaternion.Euler(proof.bodyWeaponEuler));gun.localScale=proof.bodyWeaponScale;
            AddDefaultSight(proof,gun);
        }
        else if(!thirdPerson&&proof.sharedFirstPersonRig)
        {
            Model=Instantiate(proof.sharedFirstPersonRig,transform);
            var rig=Model.GetComponent<SoldierSharedFirstPersonRig>();
            var source=proof.converted.firstPersonPrefab;
            var mount=string.IsNullOrEmpty(source.sharedWeaponMountPath)?rig.weaponMount:Model.transform.Find(source.sharedWeaponMountPath);
            if(!mount)throw new System.InvalidOperationException("Missing shared weapon mount: "+source.sharedWeaponMountPath);
            var gun=Instantiate(source,mount);gun.name="SourceWeapon";
            gun.transform.SetLocalPositionAndRotation(source.mountPosition,source.mountRotation);gun.transform.localScale=source.mountScale;
            AddDefaultSight(proof,gun.transform);
        }
        else Model=Instantiate(thirdPerson?proof.thirdPersonProof:proof.converted.firstPersonPrefab.gameObject,transform);
        foreach(var animator in Model.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        foreach(var script in Model.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
        foreach(var collider in Model.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var bodyComponent in Model.GetComponentsInChildren<Rigidbody>(true)){bodyComponent.isKinematic=true;bodyComponent.detectCollisions=false;}
        foreach(var node in Model.GetComponentsInChildren<Transform>(true))node.gameObject.layer=0;
        if(!thirdPerson){var d=proof.converted.weapon;Model.transform.localPosition=d.viewOffset+d.hipFireOffset;Model.transform.localRotation=Quaternion.Euler(d.viewRotationOffset+d.hipFireRotationOffset);}
        ApplyPose();
    }
    static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
    static void AddDefaultSight(WeaponAnimationConversionProof proof,Transform gun)
    {
        var sight=proof.converted.defaultSight;
        if(!sight||!sight.prefab||Find(gun,"DefaultSightPreview"))return;
        var config=proof.converted.firstPersonPrefab;
        var source=config.sharedFirstPersonRig?config.transform:Find(config.transform,"SourceWeapon");
        // The stripped FPS prefab is itself SourceWeapon; TPS remains a thin equipment wrapper.
        var sourceMount=config.opticMount;
        if(!source||!sourceMount||!sourceMount.IsChildOf(source))return;
        var path=RelativePath(sourceMount,source);
        var mount=string.IsNullOrEmpty(path)?gun:gun.Find(path);
        if(!mount)return;
        var optic=Instantiate(sight.prefab.gameObject,mount);optic.name="DefaultSightPreview";
        optic.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);optic.transform.localScale=sight.prefab.transform.localScale;
        if(mount.TryGetComponent<WeaponAttachmentSocket>(out var socket))socket.Place(sight,optic.transform);
    }
    static string RelativePath(Transform node,Transform root)
    {
        string path=node.name;
        while(node.parent&&node.parent!=root){node=node.parent;path=node.name+"/"+path;}
        return node==root?string.Empty:path;
    }
    public void ApplyPose()
    {
        if(!Model)return;
        Clip.SampleAnimation(Model,reloading?progress*Clip.length:0);
        if(!thirdPerson){observer.transform.SetPositionAndRotation(new Vector3(0,1.55f,0),Quaternion.identity);observer.fieldOfView=65;}
        else {var rotation=Quaternion.Euler(pitch,yaw,0);observer.transform.SetPositionAndRotation(new Vector3(0,1.05f,.08f)+rotation*new Vector3(0,0,-distance),rotation);observer.fieldOfView=50;}
    }
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.F9))Toggle();
        if(!Inspecting)return;
        if(Input.GetKeyDown(KeyCode.R)){reloading=true;progress=0;playing=true;}
        if(Input.GetKeyDown(KeyCode.Space))playing=!playing;
        if(playing&&reloading)progress=Mathf.Repeat(progress+Time.deltaTime*speed/Clip.length,1);
        if(thirdPerson){if(Input.GetMouseButton(1)){yaw+=Input.GetAxisRaw("Mouse X")*3;pitch=Mathf.Clamp(pitch-Input.GetAxisRaw("Mouse Y")*3,-50,65);}distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*.15f,.5f,5);}
        ApplyPose();
    }
    void OnGUI()
    {
        if(!Inspecting){GUI.Label(new Rect(16,Screen.height-60,760,25),"F9  Weapon animation conversions / FPS + TPS inspection");return;}
        GUILayout.BeginArea(new Rect(16,16,640,320),GUI.skin.box);
        GUILayout.Label("Current weapon animations on Polygon UE4 rig | F9 return to armory");
        selectionScroll=GUILayout.BeginScrollView(selectionScroll,GUILayout.Height(132));int choice=GUILayout.SelectionGrid(selected,names,3);GUILayout.EndScrollView();bool body=GUILayout.Toolbar(thirdPerson?1:0,new[]{"FPS / selected proportions","TPS / scaled Polygon"})==1;
        if(choice!=selected||body!=thirdPerson)Select(choice,body);
        GUILayout.BeginHorizontal();playing=GUILayout.Toggle(playing,"Play");reloading=GUILayout.Toggle(reloading,"Reload");GUILayout.Label("Speed");speed=GUILayout.HorizontalSlider(speed,0,2,GUILayout.Width(120));GUILayout.EndHorizontal();
        float value=GUILayout.HorizontalSlider(progress,0,1);if(Mathf.Abs(value-progress)>.00001f){progress=value;playing=false;ApplyPose();}
        GUILayout.Label((progress*Clip.length).ToString("F2")+" / "+Clip.length.ToString("F2")+" s | R restart / Space pause");
        GUILayout.Label("TPS: RMB orbit / scroll zoom. Armory: compare (UE4) and (Original Proportions).");
        GUILayout.EndArea();
    }
}
