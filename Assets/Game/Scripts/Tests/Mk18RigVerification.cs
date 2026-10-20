using UnityEngine;

// Isolated visual inspection fixture. It samples authored clips; no gameplay systems.
public sealed class Mk18RigVerification : MonoBehaviour
{
    public GameObject[] models;
    public AnimationClip idle, reload, bodyIdle, bodyReload, sourceIdle, sourceReload;
    public Camera observer;
    public int selected = 1;
    public bool playing = true, reloading = true;
    [Range(0, 1)] public float progress;
    public float speed = 1;
    public bool armoryToggle;
    bool inspecting;
    GameObject[] hiddenRoots;
    CursorLockMode savedLock;
    bool savedCursor;
    float yaw = 155, pitch = 8;
    float distance = 2.4f;
    string[] Labels => models.Length==2
        ? new[]{"FPS / fitted Polygon UE4", "TPS / scaled Polygon UE4"}
        : models.Length==6
        ? new[]{"FPS / original Polygon + evaluated IK", "FPS / UE4", "FPS / fitted Polygon", "FPS / original proportions", "TPS / UE4", "TPS / scaled Polygon"}
        : models.Length==5
        ? new[]{"FPS / UE4 baseline", "FPS / fitted Polygon", "FPS / original proportions", "TPS / UE4 baseline", "TPS / scaled Polygon"}
        : new[]{"FPS / UE4 baseline", "FPS / original proportions", "TPS / UE4 baseline", "TPS / scaled Polygon"};
    bool IsBody => models.Length==2 ? selected==1 : selected >= models.Length-2;

    public void ToggleInspection()
    {
        inspecting=!inspecting;
        if(inspecting)
        {
            var roots=gameObject.scene.GetRootGameObjects();
            hiddenRoots=System.Array.FindAll(roots,r=>r!=gameObject&&r.activeSelf);
            savedLock=Cursor.lockState;savedCursor=Cursor.visible;
            foreach(var root in hiddenRoots)root.SetActive(false);
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            foreach(Transform child in transform)child.gameObject.SetActive(true);
            selected=models.Length==6?2:models.Length-1;ApplyPose();
        }
        else
        {
            foreach(Transform child in transform)child.gameObject.SetActive(false);
            if(hiddenRoots!=null)foreach(var root in hiddenRoots)if(root)root.SetActive(true);
            Cursor.lockState=savedLock;Cursor.visible=savedCursor;
        }
    }
    void Update()
    {
        if(armoryToggle && Input.GetKeyDown(KeyCode.F8))ToggleInspection();
        if(armoryToggle&&!inspecting)return;
        for (int i=0;i<models.Length;i++) if(Input.GetKeyDown(KeyCode.Alpha1+i)) Select(i);
        if(Input.GetKeyDown(KeyCode.R)){reloading=true;progress=0;playing=true;}
        if(Input.GetKeyDown(KeyCode.Space)) playing=!playing;
        if(playing&&reloading)progress=Mathf.Repeat(progress+Time.deltaTime*speed/reload.length,1);
        if(IsBody)
        {
            if(Input.GetMouseButton(1)){yaw+=Input.GetAxisRaw("Mouse X")*3;pitch=Mathf.Clamp(pitch-Input.GetAxisRaw("Mouse Y")*3,-50,65);}
            distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*.15f,.5f,5);
        }
        ApplyPose();
    }
    public void Select(int index){selected=Mathf.Clamp(index,0,models.Length-1);ApplyPose();}
    public void ApplyPose()
    {
        for(int i=0;i<models.Length;i++)models[i].SetActive(i==selected);
        var model=models[selected];var clip=selected==0 && sourceIdle && sourceReload ? (reloading?sourceReload:sourceIdle) : IsBody && bodyIdle && bodyReload ? (reloading?bodyReload:bodyIdle) : (reloading?reload:idle);
        clip.SampleAnimation(model,reloading?progress*clip.length:0);
        if(!IsBody){observer.transform.SetPositionAndRotation(new Vector3(0,1.55f,0),Quaternion.identity);observer.fieldOfView=65;}
        else{var focus=new Vector3(0,1.05f,.08f);var rotation=Quaternion.Euler(pitch,yaw,0);observer.transform.SetPositionAndRotation(focus+rotation*new Vector3(0,0,-distance),rotation);observer.fieldOfView=50;}
    }
    void OnGUI()
    {
        if(armoryToggle&&!inspecting){GUI.Label(new Rect(16,Screen.height-35,500,25),"F8  MK18 / Polygon animation on UE4 rig / FPS + full body");return;}
        GUILayout.BeginArea(new Rect(16,16,500,300),GUI.skin.box);
        GUILayout.Label("MK18 / custom baked grip / original IK reference"+(armoryToggle?" | F8 return":""));
        for(int i=0;i<models.Length;i++)if(GUILayout.Toggle(selected==i,(i+1)+"  "+Labels[i],GUI.skin.button))Select(i);
        GUILayout.BeginHorizontal();playing=GUILayout.Toggle(playing,"Play");reloading=GUILayout.Toggle(reloading,"Reload");GUILayout.Label("Speed");speed=GUILayout.HorizontalSlider(speed,0,2,GUILayout.Width(120));GUILayout.EndHorizontal();
        float value=GUILayout.HorizontalSlider(progress,0,1);if(Mathf.Abs(value-progress)>.00001f){progress=value;playing=false;ApplyPose();}
        GUILayout.Label((progress*reload.length).ToString("F2")+" / "+reload.length.ToString("F2")+" s   |   R restart / Space pause");
        GUILayout.Label("TPS: hold RMB to orbit / scroll to zoom");
        GUILayout.EndArea();
    }
}

