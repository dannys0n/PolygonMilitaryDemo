using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Entry = FpsPackPreviewCatalog.Entry;
using WeaponGroup = FpsPackPreviewCatalog.WeaponGroup;

// All sampled objects and materials live in PreviewRenderUtility's isolated scene.
public class FpsPackPreview : EditorWindow
{
    [SerializeField] string weaponFamily="AKX", weaponPath, characterPath, actionPath;
    [SerializeField] bool showWeapon=true, showCharacter=true, fps, cameraMotion=true, textureOnly=true, shared;
    [SerializeField] FpsPackPreviewCatalog.FilterMode filterMode=FpsPackPreviewCatalog.FilterMode.Both;
    [SerializeField] bool showEnvironment=true;
    [SerializeField] float fov=80, speed=1;
    [SerializeField] Vector3 eyeOffset;
    FpsPackPreviewCatalog catalog;
    WeaponGroup selectedWeapon;
    Entry weaponModel, characterModel, selectedAction, characterTrack, weaponTrack, cameraTrack;
    Entry[] characters=Array.Empty<Entry>(), actions=Array.Empty<Entry>();
    PreviewRenderUtility preview;
    GameObject character, weapon, cameraRig, environment;
    Transform grip, cameraBone;
    Pose cameraReference;
    BonePose[] characterRest, weaponRest, cameraRest;
    readonly List<Material> materials=new List<Material>();
    readonly Dictionary<string,int> dropdownSelections=new Dictionary<string,int>();
    float time, distance=2;
    Vector3 center;
    Vector2 orbit=new Vector2(150,5);
    bool playing;
    bool holdCharacterPose;
    FpsPackSourceLayout.Action pairedSourceAction;
    double lastTime;
    string failure;
    string loadedWeaponPath,loadedCharacterPath,loadedCameraSkeleton;
    bool loadedTextureOnly;
    string[] weaponLabels,modelLabels,characterLabels,actionLabels;
    static readonly string[] ViewLabels={"Orbit view","FPS camera"};
    static readonly string[] FilterLabels={"All animations","Weapon compatible","Rig compatible","Both (weapon + rig)"};

    struct BonePose
    {
        public Transform bone;
        public Vector3 position, scale;
        public Quaternion rotation;
        public void Restore() { if(bone) {bone.localPosition=position;bone.localRotation=rotation;bone.localScale=scale;} }
    }
    [MenuItem("Tools/FPS Animation Pack/Animation Preview")]
    public static void Open() { var window=GetWindow<FpsPackPreview>("FPS Animation Preview"); window.minSize=new Vector2(560,550); }
    public static void OpenCharacter(string assetPath)
    {
        var window=GetWindow<FpsPackPreview>("FPS Animation Preview");
        window.minSize=new Vector2(560,550); window.characterPath=assetPath; window.shared=true;
        window.ReloadCatalog(); window.Focus();
    }
    public static void OpenWeapon(string family, string modelPath)
    {
        var window=GetWindow<FpsPackPreview>("FPS Animation Preview");
        window.weaponFamily=family; window.weaponPath=null; window.characterPath=modelPath;
        window.actionPath=null; window.shared=true; window.ReloadCatalog();
        var reload=window.actions.FirstOrDefault(a=>a.Character && a.Action.StartsWith("Reload"));
        if(reload!=null) {window.selectedAction=reload;window.SelectAction();}
        window.fps=true;window.Frame();window.Focus();
    }
    void OnEnable()
    {
        lastTime=EditorApplication.timeSinceStartup;
        EditorApplication.update+=Tick;
        ReloadCatalog();
    }
    void OnDisable() { EditorApplication.update-=Tick; Cleanup();catalog?.Dispose();catalog=null; }
    void Tick()
    {
        double now=EditorApplication.timeSinceStartup;
        if(playing && Duration>0) {time=(time+(float)(now-lastTime)*speed)%Duration;Repaint();}
        lastTime=now;
    }
    float Duration => pairedSourceAction!=null?(catalog?.Clip(selectedAction)?.length??0):new[]{holdCharacterPose?null:catalog?.Clip(characterTrack),catalog?.Clip(weaponTrack),catalog?.Clip(cameraTrack)}.Where(c=>c).Select(c=>c.length).DefaultIfEmpty(0).Max();
    void ReloadCatalog()
    {
        try
        {
            failure=null;catalog?.Dispose();catalog=new FpsPackPreviewCatalog();
            weaponLabels=catalog.Weapons.Select(w=>"Weapon: "+w.Family).ToArray();
            selectedWeapon=catalog.Weapons.FirstOrDefault(w=>w.Family==weaponFamily)??catalog.Weapons.FirstOrDefault();
            SelectWeapon();
        }
        catch(Exception e) {failure=e.Message;Cleanup();Debug.LogException(e);}
    }
    void SelectWeapon()
    {
        if(selectedWeapon==null) return;
        weaponFamily=selectedWeapon.Family;
        weaponModel=selectedWeapon.Models.FirstOrDefault(m=>m.Path==weaponPath)??selectedWeapon.Models[0];
        weaponPath=weaponModel.Path;
        modelLabels=selectedWeapon.Models.Select(m=>"Model: "+m.Name).ToArray();
        characters=catalog.CharacterModels(selectedWeapon);
        characterLabels=characters.Select(m=>m.Name+" ("+(m.rigLabel??(m.EffectiveSkeleton?.Contains("UE5")==true?"UE5":"UE4"))+")").ToArray();
        characterModel=characters.FirstOrDefault(m=>m.Path==characterPath)??characters.FirstOrDefault();
        characterPath=characterModel?.Path;
        FilterActions();
    }
    void FilterActions()
    {
        actions=catalog.Filter(selectedWeapon,characterModel,filterMode,shared);
        actionLabels=actions.Select(a=>a.file.Replace("Animations/","").Replace("/"," · ").Replace("\\"," · ")).ToArray();
        selectedAction=actions.FirstOrDefault(a=>a.Path==actionPath)??actions.FirstOrDefault();
        SelectAction();
    }
    void SelectAction()
    {
        actionPath=selectedAction?.Path;time=0;
        pairedSourceAction=catalog.AuthoredAction(selectedAction);
        var pairingAction=pairedSourceAction==null?selectedAction:catalog.Entries.First(e=>e.source==pairedSourceAction.source);
        characterTrack=selectedAction==null?null:!selectedAction.Weapon && !selectedAction.Camera?selectedAction:
            catalog.MatchingCharacter(selectedWeapon,characterModel,pairingAction)??catalog.CharacterIdle(selectedWeapon,characterModel);
        holdCharacterPose=selectedAction!=null && selectedAction!=characterTrack && characterTrack==catalog.CharacterIdle(selectedWeapon,characterModel);
        weaponTrack=selectedAction==null?null:selectedAction.Weapon?selectedAction:
            (selectedAction.Family==selectedWeapon.Family?catalog.Partner(selectedWeapon,selectedAction.Camera?(characterTrack??pairingAction):pairingAction,"weapon"):null)??catalog.IdleWeapon(selectedWeapon);
        cameraTrack=selectedAction==null?null:selectedAction.Camera?selectedAction:catalog.Partner(selectedWeapon,holdCharacterPose?pairingAction:(characterTrack==selectedAction?pairingAction:(characterTrack??pairingAction)),"camera");
        if(preview==null || loadedWeaponPath!=weaponModel?.Path || loadedCharacterPath!=characterModel?.Path || loadedTextureOnly!=textureOnly)Rebuild();
        else {UpdateCameraRig();Sample();Frame();}
    }
    void Cleanup()
    {
        preview?.Cleanup();preview=null;character=null;weapon=null;cameraRig=null;environment=null;grip=null;cameraBone=null;loadedCameraSkeleton=null;
        foreach(var material in materials) if(material) DestroyImmediate(material);
        materials.Clear();
    }
    static BonePose[] Capture(GameObject instance) => instance ? instance.GetComponentsInChildren<Transform>().Select(t=>
        new BonePose {bone=t,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray() : Array.Empty<BonePose>();
    GameObject Create(Entry entry)
    {
        if(entry==null) return null;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(entry.rigFile!=null?FpsPackPreviewCatalog.Root+entry.rigFile:entry.Path);
        if(!source) return null;
        var instance=Instantiate(source);
        instance.hideFlags=HideFlags.HideAndDontSave;
        foreach(var animator in instance.GetComponentsInChildren<Animator>()) animator.enabled=false;
        preview.AddSingleGO(instance);
        foreach(var renderer in instance.GetComponentsInChildren<Renderer>())
        {
            if(renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen=true;
            if(!textureOnly) continue;
            renderer.sharedMaterials=renderer.sharedMaterials.Select(sourceMaterial=>
            {
                var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")) {hideFlags=HideFlags.HideAndDontSave};
                material.SetColor("_BaseColor",sourceMaterial?sourceMaterial.color:Color.gray);
                if(sourceMaterial && sourceMaterial.mainTexture) material.SetTexture("_BaseMap",sourceMaterial.mainTexture);
                materials.Add(material);return material;
            }).ToArray();
        }
        return instance;
    }
    static Transform Find(GameObject instance,string name) => instance?instance.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==name):null;
    void BuildEnvironment()
    {
        environment=new GameObject("Stationary preview reference") {hideFlags=HideFlags.HideAndDontSave};
        preview.AddSingleGO(environment);
        Material Surface(Color color)
        {
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")) {hideFlags=HideFlags.HideAndDontSave};
            material.SetColor("_BaseColor",color);materials.Add(material);return material;
        }
        void Box(string name,Vector3 position,Vector3 size,Material material)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name=name;box.hideFlags=HideFlags.HideAndDontSave;
            box.transform.SetParent(environment.transform,false);
            box.transform.localPosition=position;box.transform.localScale=size;
            DestroyImmediate(box.GetComponent<Collider>());
            box.GetComponent<Renderer>().sharedMaterial=material;
        }
        var wall=Surface(new Color(.32f,.32f,.32f));
        var floor=Surface(new Color(.24f,.24f,.24f));
        var grid=Surface(new Color(.43f,.43f,.43f));
        Box("Floor",new Vector3(0,-.06f,0),new Vector3(20,.1f,20),floor);
        Box("Reference wall",new Vector3(0,3,-6),new Vector3(20,6,.1f),wall);
        for(int i=-10;i<=10;i++)
        {
            Box("Floor grid X",new Vector3(i,0,0),new Vector3(.015f,.005f,20),grid);
            Box("Floor grid Z",new Vector3(0,0,i),new Vector3(20,.005f,.015f),grid);
            Box("Wall grid vertical",new Vector3(i,3,-5.94f),new Vector3(.018f,6,.01f),grid);
        }
        for(int i=0;i<=6;i++)
            Box("Wall grid horizontal",new Vector3(0,i,-5.94f),new Vector3(20,.018f,.01f),grid);
        Box("Near left marker",new Vector3(-1.7f,1.5f,-2.5f),new Vector3(.16f,3,.16f),Surface(new Color(.48f,.31f,.19f)));
        Box("Far right marker",new Vector3(2.5f,1.5f,-4.5f),new Vector3(.16f,3,.16f),Surface(new Color(.23f,.37f,.47f)));
    }
    void Rebuild()
    {
        Cleanup();
        if(catalog==null || selectedWeapon==null) return;
        preview=new PreviewRenderUtility();
        preview.camera.nearClipPlane=.005f;preview.camera.farClipPlane=100;
        preview.camera.clearFlags=CameraClearFlags.Color;preview.camera.backgroundColor=new Color(.32f,.32f,.32f);
        var cameraData=preview.camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing=false;cameraData.volumeLayerMask=0;
        preview.lights[0].intensity=3;preview.lights[0].transform.rotation=Quaternion.Euler(35,30,0);
        preview.lights[1].intensity=2;preview.lights[1].transform.rotation=Quaternion.Euler(320,220,0);
        preview.ambientColor=new Color(.5f,.5f,.5f);
        BuildEnvironment();
        character=Create(characterModel);weapon=Create(weaponModel);
        loadedWeaponPath=weaponModel?.Path;loadedCharacterPath=characterModel?.Path;loadedTextureOnly=textureOnly;
        characterRest=Capture(character);weaponRest=Capture(weapon);
        grip=Find(character,"ik_hand_gun")??Find(character,"hand_r");
        UpdateCameraRig();Sample();Frame();
    }
    void UpdateCameraRig()
    {
        // Sample the exported camera hierarchy directly, avoiding an invented Unity camera-bone mapping.
        if(loadedCameraSkeleton!=cameraTrack?.skeleton || (!cameraRig && cameraTrack!=null))
        {
            if(cameraRig)DestroyImmediate(cameraRig);
            cameraRig=Create(cameraTrack);cameraRest=Capture(cameraRig);loadedCameraSkeleton=cameraTrack?.skeleton;
        }
        cameraBone=null;
        if(cameraRig)
        {
            foreach(var renderer in cameraRig.GetComponentsInChildren<Renderer>()) renderer.enabled=false;
            Restore(cameraRest);
            catalog.Clip(cameraTrack)?.SampleAnimation(cameraRig,0);
            cameraBone=Find(cameraRig,"camera_socket")??Find(cameraRig,"root");
            if(cameraBone) cameraReference=new Pose(cameraBone.position,cameraBone.rotation);
        }
    }
    static void Restore(BonePose[] bones) {if(bones!=null) foreach(var pose in bones) pose.Restore();}
    float TrackTime(Entry track)
    {
        if(track==null)return 0;
        float sampleTime=time;
        if(pairedSourceAction!=null && track!=selectedAction)
        {
            var segment=pairedSourceAction.segments.First(s=>s.source==selectedAction.source);
            sampleTime=segment.start+(time-segment.@from)/segment.rate;
        }
        return Mathf.Clamp(sampleTime,0,catalog.Clip(track)?.length??0);
    }
    void Sample()
    {
        if(environment)environment.SetActive(showEnvironment);
        Restore(characterRest);Restore(weaponRest);Restore(cameraRest);
        if(character) catalog.Clip(characterTrack)?.SampleAnimation(character,holdCharacterPose?0:TrackTime(characterTrack));
        if(weapon)
        {
            catalog.Clip(weaponTrack)?.SampleAnimation(weapon,TrackTime(weaponTrack));
            // Keep the FBX wrapper separate from animated weapon Root; weapon-part tracks stay local.
            if(grip) weapon.transform.SetPositionAndRotation(grip.position,grip.rotation);
        }
        if(cameraRig) catalog.Clip(cameraTrack)?.SampleAnimation(cameraRig,TrackTime(cameraTrack));
        SetVisible(character,showCharacter);SetVisible(weapon,showWeapon);
    }
    static void SetVisible(GameObject instance,bool enabled) {if(instance) foreach(var renderer in instance.GetComponentsInChildren<Renderer>()) renderer.enabled=enabled;}
    void Frame()
    {
        var renderers=new[]{character,weapon}.Where(o=>o).SelectMany(o=>o.GetComponentsInChildren<Renderer>()).Where(r=>r.enabled).ToArray();
        bool initialized=false;Bounds bounds=default;
        foreach(var renderer in renderers)
        {
            if(renderer is SkinnedMeshRenderer skin)
            {
                var baked=new Mesh();skin.BakeMesh(baked);
                foreach(var vertex in baked.vertices)
                {
                    var point=skin.transform.TransformPoint(vertex);
                    if(!initialized){bounds=new Bounds(point,Vector3.zero);initialized=true;}else bounds.Encapsulate(point);
                }
                DestroyImmediate(baked);
            }
            else if(!initialized){bounds=renderer.bounds;initialized=true;}else bounds.Encapsulate(renderer.bounds);
        }
        if(!initialized) return;
        center=bounds.center;distance=Mathf.Max(.15f,bounds.extents.magnitude/Mathf.Tan(35*Mathf.Deg2Rad*.5f)*1.2f);
    }
    void PositionCamera()
    {
        if(!fps)
        {
            preview.camera.fieldOfView=35;
            var rotation=Quaternion.Euler(orbit.y,orbit.x,0);
            preview.camera.transform.SetPositionAndRotation(center-rotation*Vector3.forward*distance,rotation);return;
        }
        // Exported animation coordinate frame faces -Z. Imported camera bone axes are not Unity camera axes.
        Vector3 eye=new Vector3(0,1.65f,0);Quaternion orientation=Quaternion.Euler(0,180,0);
        if(cameraMotion && cameraBone)
        {
            eye=cameraBone.position;
            orientation=cameraBone.rotation*Quaternion.Inverse(cameraReference.rotation)*orientation;
        }
        preview.camera.fieldOfView=fov;
        preview.camera.transform.SetPositionAndRotation(eye+orientation*eyeOffset,orientation);
    }
    static int Index(Entry[] entries,Entry selected) => Mathf.Max(0,Array.IndexOf(entries,selected));
    int Dropdown(string id,Rect rect,int selected,string[] options)
    {
        if(dropdownSelections.TryGetValue(id,out int next)){selected=Mathf.Clamp(next,0,options.Length-1);dropdownSelections.Remove(id);}
        if(EditorGUI.DropdownButton(rect,new GUIContent(options[selected]),FocusType.Keyboard,EditorStyles.popup))
            PopupWindow.Show(rect,new FpsPreviewDropdown(options,selected,Mathf.Max(rect.width,560),index=>{dropdownSelections[id]=index;Repaint();}));
        return selected;
    }
    void DrawToolbar()
    {
        const int count=12;
        const float gap=4;
        int columns=Mathf.Clamp(Mathf.FloorToInt((position.width-gap)/200),1,count);
        int rows=Mathf.CeilToInt((float)count/columns);
        float height=EditorGUIUtility.singleLineHeight;
        Rect area=GUILayoutUtility.GetRect(0,rows*(height+gap),GUILayout.ExpandWidth(true));
        float width=(area.width-gap*(columns-1))/columns;
        Rect Cell(int index) => new Rect(area.x+(index%columns)*(width+gap),area.y+(index/columns)*(height+gap),width,height);

        int group=Array.IndexOf(catalog.Weapons,selectedWeapon);
        int next=Dropdown("weapon",Cell(0),Mathf.Max(0,group),weaponLabels);
        if(next!=group){selectedWeapon=catalog.Weapons[next];weaponPath=null;actionPath=null;SelectWeapon();}
        int mesh=Index(selectedWeapon.Models,weaponModel);
        next=Dropdown("weaponModel",Cell(1),mesh,modelLabels);
        if(next!=mesh){weaponModel=selectedWeapon.Models[next];weaponPath=weaponModel.Path;Rebuild();}
        if(characters.Length>0)
        {
            int index=Index(characters,characterModel);
            next=Dropdown("character",Cell(2),index,characterLabels);
            if(next!=index){characterModel=characters[next];characterPath=characterModel.Path;actionPath=null;FilterActions();}
        }
        else GUI.Label(Cell(2),"No compatible character");
        if(actions.Length>0)
        {
            int index=Index(actions,selectedAction);
            // Unity treats '/' in popup labels as submenu separators. Keep context as plain text.
            next=Dropdown("animation",Cell(3),index,actionLabels);
            if(next!=index){selectedAction=actions[next];SelectAction();}
        }
        else GUI.Label(Cell(3),"No playable clips");
        fps=Dropdown("view",Cell(4),fps?1:0,ViewLabels)==1;
        int nextFilter=Dropdown("filter",Cell(5),(int)filterMode,FilterLabels);
        if(nextFilter!=(int)filterMode){filterMode=(FpsPackPreviewCatalog.FilterMode)nextFilter;FilterActions();}
        showCharacter=GUI.Toggle(Cell(6),showCharacter,"Show character","Button");
        showWeapon=GUI.Toggle(Cell(7),showWeapon,"Show weapon","Button");
        EditorGUI.BeginChangeCheck();
        textureOnly=GUI.Toggle(Cell(8),textureOnly,new GUIContent("Texture only","Texture-only inspection"),"Button");
        if(EditorGUI.EndChangeCheck())Rebuild();
        using(new EditorGUI.DisabledScope(!fps))
            cameraMotion=GUI.Toggle(Cell(9),cameraMotion,new GUIContent("Camera motion","Use source camera animation in FPS view"),"Button");
        using(new EditorGUI.DisabledScope(filterMode!=FpsPackPreviewCatalog.FilterMode.WeaponCompatible && filterMode!=FpsPackPreviewCatalog.FilterMode.Both))
        {
            EditorGUI.BeginChangeCheck();
            shared=GUI.Toggle(Cell(10),shared,new GUIContent("Shared actions","Include compatible shared actions"),"Button");
            if(EditorGUI.EndChangeCheck())FilterActions();
        }
        showEnvironment=GUI.Toggle(Cell(11),showEnvironment,new GUIContent("Reference environment","Stationary floor/wall grid and depth markers reveal camera motion"),"Button");
    }
    void OnGUI()
    {
        if(catalog==null || failure!=null){EditorGUILayout.HelpBox(failure??"Loading catalog",MessageType.Warning);if(GUILayout.Button("Reload catalog"))ReloadCatalog();return;}
        DrawToolbar();
        EditorGUILayout.LabelField(FilterLabels[(int)filterMode]+": "+actions.Length+" clips. Rig compatibility checks the selected character and weapon skeletons.",EditorStyles.miniLabel);
        if(fps)
        {
            using(new EditorGUILayout.HorizontalScope())
            {
            fov=EditorGUILayout.Slider("Vertical field of view",fov,40,110);
            eyeOffset=EditorGUILayout.Vector3Field("Eye adjustment (view space)",eyeOffset);
            }
        }
        EditorGUILayout.LabelField("Character",characterTrack?.Name??"No matching track",EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Weapon",weaponTrack?.Name??"Reference pose (no matching track)",EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Camera",cameraTrack?.Name??"Static FPS camera (no matching track)",EditorStyles.miniLabel);
        using(new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Speed",GUILayout.Width(42));
            speed=EditorGUILayout.Slider(speed,0,2);
            if(GUILayout.Button("1x",GUILayout.Width(35)))speed=1;
        }
        using(new EditorGUILayout.HorizontalScope())
        {
            playing=GUILayout.Toggle(playing,"Play / loop","Button",GUILayout.Width(85));
            if(GUILayout.Button("Restart",GUILayout.Width(65)))time=0;
            if(GUILayout.Button("Frame",GUILayout.Width(60))){Sample();Frame();}
            time=GUILayout.HorizontalSlider(time,0,Mathf.Max(.001f,Duration));
            GUILayout.Label($"{time:F2} / {Duration:F2}s",GUILayout.Width(85));
        }
        Rect rect=GUILayoutUtility.GetRect(100,100,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
        if(preview==null)return;
        var e=Event.current;
        if(rect.Contains(e.mousePosition) && !fps)
        {
            if(e.type==EventType.MouseDrag){orbit+=e.delta*.5f;e.Use();Repaint();}
            if(e.type==EventType.ScrollWheel){distance=Mathf.Clamp(distance*Mathf.Exp(e.delta.y*.04f),.03f,50);e.Use();Repaint();}
        }
        if(e.type==EventType.Repaint)
        {
            Sample();preview.BeginPreview(rect,GUIStyle.none);PositionCamera();preview.Render(true);
            GUI.DrawTexture(rect,preview.EndPreview(),ScaleMode.StretchToFill,false);
        }
    }
    public static AnimationClip LoadClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__"));

    public static void RenderVerification() => Verify();
    public static void Verify()
    {
        var tool=CreateInstance<FpsPackPreview>();
        var report=new List<string>();
        try
        {
            foreach(var group in tool.catalog.Weapons)
            {
                tool.selectedWeapon=group;tool.weaponPath=null;tool.characterPath=null;tool.actionPath=null;tool.SelectWeapon();
                if(tool.actions.Length==0)throw new Exception(group.Family+": empty filtered animation list");
                foreach(var action in tool.actions)
                {
                    if(action.Weapon && action.skeleton!=group.Skeleton)throw new Exception("Unrelated weapon track in filter");
                    if(action.Character && action.skeleton!=tool.characterModel.skeleton)throw new Exception("Unrelated arms track in filter");
                }
                foreach(var model in tool.characters)
                {
                    var instance=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(model.Path));
                    try
                    {
                        foreach(var action in tool.catalog.Actions(group,model,false).Where(a=>a.Character))
                        {
                            var bindings=AnimationUtility.GetCurveBindings(tool.catalog.Clip(action));
                            if(model.previewSkeleton==null && bindings.Any(b=>b.path.Length>0 && !instance.transform.Find(b.path)))
                                throw new Exception(group.Family+": incompatible filtered clip for "+model.Name+": "+action.Name);
                        }
                    }
                    finally {DestroyImmediate(instance);}
                }
                tool.selectedAction=tool.actions.FirstOrDefault(a=>a.Character && a.Action=="Reload / empty")??tool.actions.First();tool.SelectAction();
                tool.time=tool.Duration*.5f;tool.Sample();
                foreach(var pair in new[]{(tool.character,tool.characterTrack),(tool.weapon,tool.weaponTrack),(tool.cameraRig,tool.cameraTrack)})
                {
                    var clip=tool.catalog.Clip(pair.Item2);if(!clip || !pair.Item1)continue;
                    var bindings=AnimationUtility.GetCurveBindings(clip);
                    int missing=bindings.Count(b=>b.path.Length>0 && !pair.Item1.transform.Find(b.path));
                    if(missing>0 && !(pair.Item1==tool.character && tool.characterModel.previewSkeleton!=null))throw new Exception(group.Family+": "+missing+" unresolved bindings on "+pair.Item2.Name);
                }
                if(tool.grip && Vector3.Distance(tool.weapon.transform.position,tool.grip.position)>.00001f)throw new Exception("Weapon detached from grip");
                report.Add(group.Family+": "+tool.actions.Length+" filtered clips; paired="+tool.weaponTrack?.Name+"; camera="+tool.cameraTrack?.Name);
            }
            tool.weaponFamily="AKX";tool.weaponPath=null;tool.characterPath=null;tool.actionPath=null;tool.ReloadCatalog();
            tool.selectedAction=tool.actions.First(a=>a.Character && a.Action=="Reload / empty");tool.SelectAction();
            foreach(bool firstPerson in new[]{false,true})
            {
                tool.fps=firstPerson;tool.time=tool.Duration*.5f;tool.Sample();tool.Frame();
                tool.preview.BeginPreview(new Rect(0,0,1280,900),GUIStyle.none);tool.PositionCamera();tool.preview.Render(true);
                report.Add("View="+(firstPerson?"FPS":"Orbit")+" center="+tool.center.ToString("F3")+" camera="+tool.preview.camera.transform.position.ToString("F3")+" forward="+tool.preview.camera.transform.forward.ToString("F3")+" centerViewport="+tool.preview.camera.WorldToViewportPoint(tool.center).ToString("F3"));
                var texture=tool.preview.EndPreview();var previous=RenderTexture.active;
                try
                {
                    RenderTexture.active=(RenderTexture)texture;
                    var png=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false,true);
                    png.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);png.Apply();
                    // PNG has no linear-color flag; encode display colors for an honest inspection image.
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear && !RenderTexture.active.sRGB)
                    {var pixels=png.GetPixels();for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;png.SetPixels(pixels);png.Apply();}
                    File.WriteAllBytes(Path.Combine(Application.dataPath,"..",firstPerson?"PreviewFPS.png":"PreviewOrbit.png"),png.EncodeToPNG());DestroyImmediate(png);
                }
                finally {RenderTexture.active=previous;}
            }
            File.WriteAllLines(Path.Combine(Application.dataPath,"..","PreviewVerification.txt"),report);
            Debug.Log("FPS_PREVIEW_VERIFIED "+tool.catalog.Weapons.Length+" weapon families");
        }
        finally {DestroyImmediate(tool);}
    }
}
