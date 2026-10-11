using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Hip placement edits definitions; ADS edits the reusable weapon/sight reference transforms.
public class WeaponViewPlacementWindow : EditorWindow
{
    SoldierFirstPersonView player;
    SoldierLocalInput localInput;
    bool editing, ads = true, previousFirstPerson, previousInputBlocked, previousToolsHidden;
    CursorLockMode previousCursorLock;
    bool previousCursorVisible;
    int handleMode;
    int sightPointIndex;
    RenderTexture preview;
    [SerializeField] float previewZoom = 1f;
    [SerializeField] Vector2 previewCenter = new Vector2(.5f, .5f);
    double nextRepaint;
    readonly HashSet<SoldierWeaponDefinition> changed = new HashSet<SoldierWeaponDefinition>();
    readonly HashSet<WeaponSightDefinition> changedSights = new HashSet<WeaponSightDefinition>();
    readonly Dictionary<string, Dictionary<string, Pose>> sightPoints = new Dictionary<string, Dictionary<string, Pose>>();

    [MenuItem("Tools/Game/Weapon View Placement")]
    public static void Open() => GetWindow<WeaponViewPlacementWindow>("Weapon View Placement");

    void OnEnable()
    {
        minSize = new Vector2(480, 520);
        SceneView.duringSceneGui += DrawHandles;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Undo.undoRedoPerformed += Refresh;
    }

    void OnDisable()
    {
        StopEditing();
        ReleasePreview();
        SceneView.duringSceneGui -= DrawHandles;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        Undo.undoRedoPerformed -= Refresh;
    }

    void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) StopEditing();
        if (state == PlayModeStateChange.EnteredPlayMode) FindPlayer();
        Repaint();
    }

    void FindPlayer()
    {
        var selected = Selection.activeGameObject;
        player = selected != null ? selected.GetComponentInParent<SoldierFirstPersonView>() : null;
        if (player != null && !EditorUtility.IsPersistent(player)) return;
        player = null;
        foreach (var candidate in Object.FindObjectsByType<SoldierFirstPersonView>())
            if (candidate.GetComponent<SoldierLocalInput>() != null) { player = candidate; break; }
    }

    void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (player == null) { StopEditing(); FindPlayer(); }
        if (editing && player != null) player.EditorPlacementAim = ads;
        if (EditorApplication.timeSinceStartup < nextRepaint) return;
        nextRepaint = EditorApplication.timeSinceStartup + 1.0 / 20.0;
        Repaint();
        if (editing) SceneView.RepaintAll();
    }

    void Refresh() { Repaint(); SceneView.RepaintAll(); }

    void StartEditing()
    {
        editing = true;
        previousFirstPerson = player.firstPerson;
        localInput = player.GetComponent<SoldierLocalInput>();
        if (localInput != null) { previousInputBlocked = localInput.InputBlocked; localInput.InputBlocked = true; }
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        previousToolsHidden = Tools.hidden;
        Tools.hidden = true;
        player.SetFirstPerson(true);
        player.EditorPlacementAim = ads;
        FrameWeapon();
    }

    void StopEditing()
    {
        if (editing)
        {
            if (player != null && EditorApplication.isPlaying)
            {
                player.EditorPlacementAim = null;
                player.SetFirstPerson(previousFirstPerson);
            }
            if (localInput != null) localInput.InputBlocked = previousInputBlocked;
            Tools.hidden = previousToolsHidden;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
        editing = false;
        SavePlacement();
    }

    void SavePlacement()
    {
        foreach (var definition in changed)
            if (definition != null) AssetDatabase.SaveAssetIfDirty(definition);
        changed.Clear();
        foreach (var entry in sightPoints)
        {
            string path=entry.Key;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var value in entry.Value)
                {
                    var point = root.transform.Find(value.Key);
                    point.SetLocalPositionAndRotation(value.Value.position, value.Value.rotation);
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        sightPoints.Clear();
        foreach (var sight in changedSights) if (sight) AssetDatabase.SaveAssetIfDirty(sight);
        changedSights.Clear();
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("Play Mode: choose Hip or ADS, then drag the handles in Scene view. The preview shows the actual player camera. Hip changes save to the weapon definition; sight-based ADS changes save to the active sight, including when Play Mode ends.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        var chosen = (SoldierFirstPersonView)EditorGUILayout.ObjectField("Live player", player, typeof(SoldierFirstPersonView), true);
        if (EditorGUI.EndChangeCheck()) { StopEditing(); player = chosen; }
        if (!EditorApplication.isPlaying || player == null || EditorUtility.IsPersistent(player))
        {
            EditorGUILayout.HelpBox("Start Play Mode with a local player in the scene.", MessageType.None);
            return;
        }
        var definition = player.GetComponent<SoldierWeaponPresentation>().weapon.definition;
        EditorGUILayout.ObjectField("Editing definition", definition, typeof(SoldierWeaponDefinition), false);
        ads = GUILayout.Toolbar(ads ? 1 : 0, new[] { "Hip", "ADS" }) == 1;
        var sights=player.GetComponent<SoldierWeaponLoadout>().EquippedSights;
        bool sightAds=ads && sights && sights.SelectedSight;
        if (!sightAds) handleMode = GUILayout.Toolbar(handleMode, new[] { "Move weapon view", "Rotate weapon view", "Move pivot" });
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(editing ? "Finish editing" : "Start editing")) { if (editing) StopEditing(); else StartEditing(); }
        if (GUILayout.Button("Frame gun in Scene view")) FrameWeapon();
        if (GUILayout.Button("Save")) SavePlacement();
        EditorGUILayout.EndHorizontal();
        if (sightAds) DrawSightCalibration(sights);
        else EditorGUILayout.LabelField("Position", (ads ? definition.aimOffset : definition.hipFireOffset).ToString("F4"));
        if (!sightAds) EditorGUILayout.LabelField("Rotation", (ads ? definition.aimRotationOffset : definition.hipFireRotationOffset).ToString("F2"));
        if (!sightAds) EditorGUILayout.LabelField("Pivot", definition.viewRotationPivot.ToString("F4"));
        if (editing && !sightAds) EditorGUILayout.LabelField("Moving the pivot preserves both Hip and ADS placement.", EditorStyles.wordWrappedLabel);
        DrawPreview();
    }

    void DrawSightCalibration(SoldierWeaponSights sights)
    {
        var sight=sights.SelectedSight;
        var rear=sights.ActiveAimPoint;
        var front=sights.ActiveFrontAimPoint;
        EditorGUILayout.ObjectField("Active sight",sight,typeof(WeaponSightDefinition),false);
        EditorGUILayout.HelpBox("Align the rear-to-front line. Rear rotation controls roll; eye distance controls camera spacing. Save writes these points to their reusable weapon or attachment prefab.",MessageType.Info);
        sightPointIndex = GUILayout.Toolbar(sightPointIndex, new[] { "Rear handle", "Front handle" });
        EditorGUILayout.ObjectField("Rear reference",rear,typeof(Transform),true);
        EditorGUILayout.ObjectField("Front reference",front,typeof(Transform),true);
        EditorGUI.BeginChangeCheck();
        float distance=EditorGUILayout.FloatField("Eye distance",sight.eyeDistance);
        float fov=EditorGUILayout.Slider("ADS field of view",sight.fieldOfView,20f,90f);
        Vector3 rearPosition=EditorGUILayout.Vector3Field("Rear position",rear.localPosition);
        Vector3 frontPosition=EditorGUILayout.Vector3Field("Front position",front.localPosition);
        Vector3 rotation=EditorGUILayout.Vector3Field("Rear up / roll",rear.localEulerAngles);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(sight,"Calibrate weapon sight");
            Undo.RecordObjects(new Object[]{rear,front},"Calibrate sight references");
            sight.eyeDistance=Mathf.Max(.03f,distance);sight.fieldOfView=fov;
            rear.SetLocalPositionAndRotation(rearPosition,Quaternion.Euler(rotation));
            front.localPosition=frontPosition;
            EditorUtility.SetDirty(sight);changedSights.Add(sight);
            QueueSightPoint(rear);
            QueueSightPoint(front);
        }
    }

    void QueueSightPoint(Transform point)
    {
        Transform source;
        var attachment = point.GetComponentInParent<WeaponSightAttachment>();
        if (attachment && attachment.SourcePrefab)
            source = point == attachment.rearSight ? attachment.SourcePrefab.rearSight : attachment.SourcePrefab.frontSight;
        else source = EditorUtility.IsPersistent(point) ? point : PrefabUtility.GetCorrespondingObjectFromOriginalSource(point);
        if (!source && player)
        {
            var sights = player.GetComponent<SoldierWeaponLoadout>().EquippedSights;
            if (sights && sights.authoringPrefab && (point == sights.rearSight || point == sights.frontSight))
            {
                var group = System.Array.Find(sights.authoringPrefab.GetComponentsInChildren<Transform>(true),
                    t => t.name == "SightReferences");
                source = group.Find(point == sights.rearSight ? "RearSight" : "FrontSight");
            }
        }
        string path = source ? AssetDatabase.GetAssetPath(source) : "";
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("No authoring prefab found for " + point.name + ". Edit this reference directly on its prefab.");
            return;
        }
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        string relative = AnimationUtility.CalculateTransformPath(source, root.transform);
        if (!sightPoints.TryGetValue(path, out var points))
            sightPoints[path] = points = new Dictionary<string, Pose>();
        points[relative] = new Pose(point.localPosition, point.localRotation);
    }

    void FrameWeapon()
    {
        if (player == null || SceneView.lastActiveSceneView == null) return;
        var loadout = player.GetComponent<SoldierWeaponLoadout>();
        var renderers = loadout.FirstPersonView.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var mesh in renderers) bounds.Encapsulate(mesh.bounds);
        SceneView.lastActiveSceneView.Frame(bounds, false);
    }

    void DrawPreview()
    {
        EditorGUILayout.BeginHorizontal();
        previewZoom = EditorGUILayout.Slider("Preview zoom", previewZoom, 1f, 8f);
        if (GUILayout.Button("Reset", GUILayout.Width(55)))
        {
            previewZoom = 1f;
            previewCenter = new Vector2(.5f, .5f);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Scroll over the preview to zoom; middle-drag to pan.", EditorStyles.miniLabel);
        var camera = player.playerCamera;
        float aspect = Mathf.Max(.1f, camera.aspect);
        var rect = GUILayoutUtility.GetRect(100, 10000, 180, 10000, GUILayout.ExpandHeight(true));
        float width = Mathf.Min(rect.width, rect.height * aspect);
        rect = new Rect(rect.center.x - width / 2f, rect.center.y - width / aspect / 2f, width, width / aspect);
        var input = Event.current;
        if (rect.Contains(input.mousePosition))
        {
            if (input.type == EventType.ScrollWheel)
            {
                Vector2 cursor = new Vector2((input.mousePosition.x - rect.x) / rect.width,
                    1f - (input.mousePosition.y - rect.y) / rect.height);
                Vector2 focus = previewCenter + (cursor - Vector2.one * .5f) / previewZoom;
                previewZoom = Mathf.Clamp(previewZoom * Mathf.Exp(-input.delta.y * .08f), 1f, 8f);
                previewCenter = focus - (cursor - Vector2.one * .5f) / previewZoom;
                input.Use();
                Repaint();
            }
            else if (input.type == EventType.MouseDrag && input.button == 2)
            {
                previewCenter -= new Vector2(input.delta.x / rect.width, -input.delta.y / rect.height) / previewZoom;
                input.Use();
                Repaint();
            }
        }
        float half = .5f / previewZoom;
        previewCenter = new Vector2(Mathf.Clamp(previewCenter.x, half, 1f - half),
            Mathf.Clamp(previewCenter.y, half, 1f - half));
        int pixelsWide = Mathf.Clamp(Mathf.RoundToInt(width * previewZoom), 128, 2048);
        int pixelsHigh = Mathf.Max(1, Mathf.RoundToInt(pixelsWide / aspect));
        if (preview == null || preview.width != pixelsWide || preview.height != pixelsHigh)
        {
            ReleasePreview();
            preview = new RenderTexture(pixelsWide, pixelsHigh, 24) { hideFlags = HideFlags.HideAndDontSave, name = "Weapon placement preview" };
            preview.Create();
        }
        if (Event.current.type == EventType.Repaint)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = preview };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
        }
        var uv = new Rect(previewCenter.x - half, previewCenter.y - half, half * 2f, half * 2f);
        GUI.DrawTextureWithTexCoords(rect, preview, uv, false);
        // Keep the aiming cross at the real screen center when inspecting off-center details.
        var cross = new Vector2(rect.x + (.5f - uv.x) / uv.width * rect.width,
            rect.y + (1f - (.5f - uv.y) / uv.height) * rect.height);
        if (rect.Contains(cross))
        {
            EditorGUI.DrawRect(new Rect(cross.x - 8, cross.y, 16, 1), Color.white);
            EditorGUI.DrawRect(new Rect(cross.x, cross.y - 8, 1, 16), Color.white);
        }
    }

    void ReleasePreview()
    {
        if (preview == null) return;
        preview.Release();
        DestroyImmediate(preview);
        preview = null;
    }

    void DrawHandles(SceneView scene)
    {
        if (!editing || player == null || !EditorApplication.isPlaying) return;
        float desiredBlend = ads ? 1f : 0f;
        if (Mathf.Abs(player.AimBlend - desiredBlend) > .001f) return;
        var sights=player.GetComponent<SoldierWeaponLoadout>().EquippedSights;
        if (ads && sights && sights.SelectedSight)
        {
            var rear=sights.ActiveAimPoint;
            var front=sights.ActiveFrontAimPoint;
            var point=sightPointIndex==0?rear:front;
            Handles.color=Color.cyan;
            Handles.DrawLine(rear.position,front.position);
            Handles.Label(point.position,sightPointIndex==0?"Rear sight reference":"Front sight reference");
            EditorGUI.BeginChangeCheck();
            Vector3 position=Handles.PositionHandle(point.position,point.rotation);
            if(EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(point,"Move sight reference");point.position=position;
                QueueSightPoint(point);Repaint();
            }
            if(Event.current.rawType==EventType.MouseUp)SavePlacement();
            return;
        }
        var definition = player.GetComponent<SoldierWeaponPresentation>().weapon.definition;
        var root = player.firstPersonRoot;
        Vector3 pivot = root.TransformPoint(definition.viewRotationPivot);
        Handles.color = Color.yellow;
        Handles.SphereHandleCap(0, pivot, Quaternion.identity, HandleUtility.GetHandleSize(pivot) * .035f, EventType.Repaint);
        Handles.Label(pivot, definition.name + (ads ? " ADS" : " Hip") + " pivot");
        EditorGUI.BeginChangeCheck();
        var axis = Tools.pivotRotation == PivotRotation.Local ? root.rotation : Quaternion.identity;
        Vector3 movedPivot = handleMode == 1 ? pivot : Handles.PositionHandle(pivot, axis);
        Quaternion rotated = handleMode == 1 ? Handles.RotationHandle(root.rotation, pivot) : root.rotation;
        if (EditorGUI.EndChangeCheck())
        {
            if (handleMode == 2) SetPivot(player, root.InverseTransformPoint(movedPivot));
            else SetPose(player, ads, movedPivot, rotated);
            changed.Add(definition);
            Repaint();
        }
        if (Event.current.rawType == EventType.MouseUp) SavePlacement();
    }

    public static void SetPose(SoldierFirstPersonView view, bool aim, Vector3 worldPivot, Quaternion worldRotation)
    {
        var definition = view.GetComponent<SoldierWeaponPresentation>().weapon.definition;
        var root = view.firstPersonRoot;
        Quaternion localRotation = Quaternion.Inverse(root.parent.rotation) * worldRotation;
        Vector3 worldPosition = worldPivot - worldRotation * Vector3.Scale(definition.viewRotationPivot, root.lossyScale);
        Vector3 localPosition = root.parent.InverseTransformPoint(worldPosition);
        Vector3 pivot = Vector3.Scale(definition.viewRotationPivot, root.localScale);
        Vector3 offset = localPosition - definition.viewOffset - view.EditorPlacementRestRotation * pivot + localRotation * pivot;
        Vector3 angles = (Quaternion.Inverse(view.EditorPlacementRestRotation * Quaternion.Euler(definition.viewRotationOffset)) * localRotation).eulerAngles;
        angles = new Vector3(Mathf.DeltaAngle(0, angles.x), Mathf.DeltaAngle(0, angles.y), Mathf.DeltaAngle(0, angles.z));
        Undo.RecordObject(definition, "Adjust weapon view");
        if (aim) { definition.aimOffset = offset; definition.aimRotationOffset = angles; }
        else { definition.hipFireOffset = offset; definition.hipFireRotationOffset = angles; }
        EditorUtility.SetDirty(definition);
    }

    public static void SetPivot(SoldierFirstPersonView view, Vector3 pivot)
    {
        var definition = view.GetComponent<SoldierWeaponPresentation>().weapon.definition;
        Vector3 delta = Vector3.Scale(pivot - definition.viewRotationPivot, view.firstPersonRoot.localScale);
        Quaternion rest = view.EditorPlacementRestRotation;
        Quaternion basis = rest * Quaternion.Euler(definition.viewRotationOffset);
        Undo.RecordObject(definition, "Move weapon view pivot");
        definition.hipFireOffset += basis * Quaternion.Euler(definition.hipFireRotationOffset) * delta - rest * delta;
        definition.aimOffset += basis * Quaternion.Euler(definition.aimRotationOffset) * delta - rest * delta;
        definition.viewRotationPivot = pivot;
        EditorUtility.SetDirty(definition);
    }
}
