using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Asset stress test: all population work is triggered by controls, never by an Update scan.
public sealed class VehiclePerformanceControl : MonoBehaviour
{
    public BehaviorTestCamera observer;
    public Vehicle[] prefabs = Array.Empty<Vehicle>();
    [Range(1,256)] public int maximumVehicles = 256;
    [Min(30)] public float baySpacing = 100;
    public bool frozen, showControls = true;
    List<Instance>[] fleets;
    bool[] occupied;
    string[] labels, rowLabels;
    string countText = "0", totalLabel = "Total vehicles: 0";
    int selected, total;
    Vector2 scroll;
    sealed class Instance
    {
        public Vehicle vehicle;
        public int bay;
        public MonoBehaviour[] scripts;
        public NavMeshObstacle[] obstacles;
        public Rigidbody[] bodies;
        public bool[] scriptsEnabled, obstaclesEnabled, kinematic, collisions;
    }
    public int TotalCount => total;
    public int Count(int type) => fleets[type].Count;
    void Awake()
    {
        fleets = new List<Instance>[prefabs.Length]; labels = new string[prefabs.Length]; rowLabels = new string[prefabs.Length];
        occupied = new bool[maximumVehicles];
        for(int i=0;i<prefabs.Length;i++) { fleets[i] = new List<Instance>(); labels[i] = prefabs[i].name; rowLabels[i] = labels[i]+" [0]"; }
    }
    public void SetCount(int type, int count)
    {
        if(type<0 || type>=fleets.Length) throw new ArgumentOutOfRangeException(nameof(type));
        var fleet = fleets[type];
        count = Mathf.Clamp(count, 0, fleet.Count + maximumVehicles - total);
        while(fleet.Count>count)
        {
            int last=fleet.Count-1;var item=fleet[last];fleet.RemoveAt(last);
            occupied[item.bay]=false;total--;
            item.vehicle.gameObject.SetActive(false);Destroy(item.vehicle.gameObject);
        }
        while(fleet.Count<count)
        {
            int bay=Array.IndexOf(occupied,false);
            var cell=Bay(bay);
            Vector3 position=new Vector3(cell.x*baySpacing,2,cell.y*baySpacing);
            var vehicle=Instantiate(prefabs[type],position,Quaternion.identity,transform);
            vehicle.name=labels[type]+" #"+bay;
            vehicle.DamageFilter=IgnoreDamage;
            // Align the lowest solid collider with the floor instead of guessing each model's pivot height.
            float bottom=float.PositiveInfinity;
            foreach(var collider in vehicle.GetComponentsInChildren<Collider>())
                if(collider.enabled && !collider.isTrigger && !(collider is WheelCollider)) bottom=Mathf.Min(bottom,collider.bounds.min.y);
            if(!float.IsPositiveInfinity(bottom))vehicle.transform.position+=Vector3.up*(.08f-bottom);
            var item=new Instance { vehicle=vehicle,bay=bay,scripts=vehicle.GetComponentsInChildren<MonoBehaviour>(true),
                obstacles=vehicle.GetComponentsInChildren<NavMeshObstacle>(true),bodies=vehicle.GetComponentsInChildren<Rigidbody>(true) };
            item.scriptsEnabled=Array.ConvertAll(item.scripts,s=>s.enabled);
            item.obstaclesEnabled=Array.ConvertAll(item.obstacles,o=>o.enabled);
            item.kinematic=Array.ConvertAll(item.bodies,b=>b.isKinematic);
            item.collisions=Array.ConvertAll(item.bodies,b=>b.detectCollisions);
            fleet.Add(item);occupied[bay]=true;total++;
            if(frozen)ApplyFrozen(item,true);
        }
        rowLabels[type]=labels[type]+" ["+fleet.Count+"]";
        totalLabel="Total vehicles: "+total+" / "+maximumVehicles;
        countText=fleets[selected].Count.ToString();
    }
    // Square spiral keeps small fleets compact without moving existing physics bodies as counts change.
    static Vector2Int Bay(int index)
    {
        if(index==0)return Vector2Int.zero;
        int ring=Mathf.CeilToInt((Mathf.Sqrt(index+1)-1)*.5f), side=ring*2;
        int offset=(side+1)*(side+1)-1-index;
        if(offset<side)return new Vector2Int(ring-offset,-ring);
        if(offset<side*2)return new Vector2Int(-ring,-ring+offset-side);
        if(offset<side*3)return new Vector2Int(-ring+offset-side*2,ring);
        return new Vector2Int(ring,ring-offset+side*3);
    }
    static bool IgnoreDamage(GameObject source)=>false;
    public void Clear()
    {
        for(int i=0;i<fleets.Length;i++)SetCount(i,0);
    }
    public void SetFrozen(bool value)
    {
        if(frozen==value)return;
        frozen=value;
        foreach(var fleet in fleets)foreach(var item in fleet)ApplyFrozen(item,value);
    }
    static void ApplyFrozen(Instance item,bool value)
    {
        for(int i=0;i<item.scripts.Length;i++)if(item.scripts[i])item.scripts[i].enabled=!value && item.scriptsEnabled[i];
        for(int i=0;i<item.obstacles.Length;i++)if(item.obstacles[i])item.obstacles[i].enabled=!value && item.obstaclesEnabled[i];
        for(int i=0;i<item.bodies.Length;i++)
        {
            var body=item.bodies[i];if(!body)continue;
            if(value && !body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            body.isKinematic=value || item.kinematic[i];
            body.detectCollisions=!value && item.collisions[i];
        }
    }
    public void FrameFleet()
    {
        if(total==0 || !observer)return;
        bool found=false;Bounds bounds=default;
        foreach(var fleet in fleets)foreach(var item in fleet)
        {
            var local=item.vehicle.NavigationBounds;
            for(int corner=0;corner<8;corner++)
            {
                var point=item.vehicle.transform.TransformPoint(local.center+Vector3.Scale(local.extents,
                    new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                if(found)bounds.Encapsulate(point);else {bounds=new Bounds(point,Vector3.zero);found=true;}
            }
        }
        var camera=observer.GetComponent<Camera>();
        float fov=Mathf.Min(camera.fieldOfView,2*Mathf.Atan(Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*camera.aspect)*Mathf.Rad2Deg);
        float distance=Mathf.Max(20,bounds.extents.magnitude/Mathf.Sin(fov*Mathf.Deg2Rad*.5f)*1.1f);
        Vector3 direction=new Vector3(0,1,-1).normalized;
        observer.SetPose(bounds.center+direction*distance,Quaternion.LookRotation(-direction));
    }
    void OnGUI()
    {
        if(Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.F1){showControls=!showControls;Event.current.Use();}
        if(!showControls)return;
        GUILayout.BeginArea(new Rect(12,48,370,Mathf.Min(Screen.height-60,740)),GUI.skin.box);
        GUILayout.Label("VEHICLE ASSET STRESS TEST");GUILayout.Label(totalLabel);
        GUILayout.Label("No crews / AI. Counts are per selected vehicle type.");
        scroll=GUILayout.BeginScrollView(scroll,GUILayout.Height(Mathf.Min(330,Screen.height*.38f)));
        for(int i=0;i<labels.Length;i++)
            if(GUILayout.Toggle(selected==i,rowLabels[i],GUI.skin.button) && selected!=i){selected=i;countText=fleets[i].Count.ToString();}
        GUILayout.EndScrollView();
        if(prefabs.Length>0)
        {
            GUILayout.Label(labels[selected]);GUILayout.BeginHorizontal();
            countText=GUILayout.TextField(countText,3,GUILayout.Width(60));
            if(GUILayout.Button("Apply") && int.TryParse(countText,out int count))SetCount(selected,count);
            if(GUILayout.Button("-10"))SetCount(selected,fleets[selected].Count-10);
            if(GUILayout.Button("+10"))SetCount(selected,fleets[selected].Count+10);
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Only this type (keep selected count)"))for(int i=0;i<fleets.Length;i++)if(i!=selected)SetCount(i,0);
        }
        bool freeze=GUILayout.Toggle(frozen,"Frozen: vehicle scripts and physics off");if(freeze!=frozen)SetFrozen(freeze);
        GUILayout.Label("Frozen retains renderers, animators and particles.\nNormal retains the prefab's standard scripts and physics.");
        if(GUILayout.Button("Frame entire fleet"))FrameFleet();
        if(GUILayout.Button("Clear all vehicles"))Clear();
        if(GUILayout.Button("Reload scene / reset runtime caches"))SceneManager.LoadScene(SceneManager.GetActiveScene().path);
        GUILayout.Label("RMB + WASD / Q E: fly | Shift: faster | F1: hide\n100 m bays avoid aircraft overlap. Move camera as needed.\nUse zero instances as baseline: catalog assets are already loaded.");
        GUILayout.EndArea();
    }
}
