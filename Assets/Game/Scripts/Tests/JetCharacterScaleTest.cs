using System;
using UnityEngine;

// Isolated visual fixture: no vehicle motors, damage, infantry AI or movement scripts.
public sealed class JetCharacterScaleTest : MonoBehaviour
{
    [Serializable] public class Station
    {
        public string label;
        public Animator pilot, headlessPilot;
        public Transform anchor, leftGrip, rightGrip, authoredEye;
        public Renderer[] vehicleRenderers;
    }
    public Station[] stations;
    public BehaviorTestCamera observer;
    public Transform overview, standingView;
    public GameObject[] standingModels;
    public int selected;
    public bool cockpit, cutaway, headless = true, handIK = true, showHelmets = true;
    VehicleSeatPose[] poses;
    GUIStyle title;

    void Start()
    {
        poses = new VehicleSeatPose[stations.Length];
        for(int i=0;i<stations.Length;i++) Prepare(i);
        ShowStanding();
    }
    void Prepare(int index)
    {
        var station=stations[index];
        var animator = station.headlessPilot && cockpit && headless && index==selected ? station.headlessPilot : station.pilot;
        if(station.headlessPilot)station.headlessPilot.gameObject.SetActive(animator==station.headlessPilot);
        station.pilot.gameObject.SetActive(animator==station.pilot);
        if(poses[index])poses[index].End();
        var pose=animator.GetComponent<VehicleSeatPose>();
        if(!pose)pose=animator.gameObject.AddComponent<VehicleSeatPose>();
        poses[index]=pose;
        pose.Begin(animator,new Vehicle.Seat {anchor=station.anchor,role=Vehicle.SeatRole.Driver,pose=Vehicle.Seat.Pose.Driving},handIK?station.leftGrip:null,handIK?station.rightGrip:null);
    }
    public void SelectStation(int index, bool firstPerson)
    {
        selected=Mathf.Clamp(index,0,stations.Length-1);cockpit=firstPerson;
        for(int i=0;i<stations.Length;i++)Prepare(i);
        ApplyVisibility();
        var s=stations[selected];
        var animator=s.headlessPilot&&cockpit&&headless?s.headlessPilot:s.pilot;
        animator.Update(0);
        var head=animator.GetBoneTransform(HumanBodyBones.Head);
        if(cockpit) observer.SetPose(head.position + s.anchor.up*.055f+s.anchor.forward*.065f,s.anchor.rotation);
        else
        {
            var eye=s.anchor.position+s.anchor.up*.6f;
            var position=eye+s.anchor.right*2.6f+s.anchor.forward*1.6f+s.anchor.up*.6f;
            observer.SetPose(position,Quaternion.LookRotation(eye-position,Vector3.up));
        }
    }
    public void ShowOverview()
    {
        cockpit=false;
        for(int i=0;i<stations.Length;i++)Prepare(i);
        ApplyVisibility();observer.SetPose(overview.position,overview.rotation);
    }
    public void ShowStanding()
    {
        cockpit=false;for(int i=0;i<stations.Length;i++)Prepare(i);
        ApplyVisibility();observer.SetPose(standingView.position,standingView.rotation);
    }
    public void ApplyVisibility()
    {
        foreach(var s in stations)
        {
            foreach(var renderer in s.vehicleRenderers)if(renderer)renderer.enabled=!cutaway;
            foreach(var a in new[]{s.pilot,s.headlessPilot})
            {
                if(!a)continue;
                var head=a.GetBoneTransform(HumanBodyBones.Head);
                foreach(var r in a.GetComponentsInChildren<MeshRenderer>(true))
                    if(r.transform.IsChildOf(head))r.enabled=showHelmets && a!=s.headlessPilot;
            }
        }
        foreach(var model in standingModels)
        {
            var a=model.GetComponentInChildren<Animator>();var head=a.GetBoneTransform(HumanBodyBones.Head);
            foreach(var r in model.GetComponentsInChildren<MeshRenderer>(true))if(r.transform.IsChildOf(head))r.enabled=showHelmets;
        }
    }
    void LateUpdate()
    {
        if(!cockpit||Input.GetMouseButton(1))return;
        var s=stations[selected];var animator=s.headlessPilot&&headless?s.headlessPilot:s.pilot;
        var head=animator.GetBoneTransform(HumanBodyBones.Head);
        observer.SetPose(head.position+s.anchor.up*.055f+s.anchor.forward*.065f,s.anchor.rotation);
    }
    void OnGUI()
    {
        if(title==null)title=new GUIStyle(GUI.skin.label){fontSize=17,fontStyle=FontStyle.Bold};
        GUILayout.BeginArea(new Rect(12,12,480,335),GUI.skin.box);
        GUILayout.Label("JET 1 / CHARACTER SCALE COMPARISON",title);
        GUILayout.Label("Same production seat, Driving_Rigged pose and hand targets.");
        for(int i=0;i<stations.Length;i++)
        {
            GUILayout.BeginHorizontal();GUILayout.Label(stations[i].label,GUILayout.Width(250));
            int id=i;if(GUILayout.Button("Exterior"))SelectStation(id,false);
            if(GUILayout.Button("Head view"))SelectStation(id,true);GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();if(GUILayout.Button("Overview"))ShowOverview();if(GUILayout.Button("Standing comparison"))ShowStanding();GUILayout.EndHorizontal();
        bool c=GUILayout.Toggle(cutaway,"Hide aircraft shell / inspect seat and limbs");if(c!=cutaway){cutaway=c;ApplyVisibility();}
        bool h=GUILayout.Toggle(headless,"Headless scaled body in head view");if(h!=headless){headless=h;SelectStation(selected,cockpit);}
        bool ik=GUILayout.Toggle(handIK,"Production hand IK targets");if(ik!=handIK){handIK=ik;SelectStation(selected,cockpit);}
        bool helmets=GUILayout.Toggle(showHelmets,"Show helmets (disable for height comparison)");if(helmets!=showHelmets){showHelmets=helmets;ApplyVisibility();}
        GUILayout.Label("Hold RMB + WASD to fly camera. Q/E down/up; Shift fast.");
        GUILayout.Label("Polygon uses its humanoid rig; UE4 uses a humanoid test avatar.");
        GUILayout.EndArea();
    }
}
