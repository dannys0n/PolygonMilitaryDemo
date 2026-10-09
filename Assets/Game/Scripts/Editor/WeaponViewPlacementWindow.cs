using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Scene handles edit the definition, never a temporary transform or weapon hierarchy.
public class WeaponViewPlacementWindow : EditorWindow
{
    SoldierFirstPersonView player;
    SoldierLocalInput localInput;
    bool editing, ads = true, previousFirstPerson, previousInputBlocked, previousToolsHidden;
    CursorLockMode previousCursorLock;
    bool previousCursorVisible;
    int handleMode;
    RenderTexture preview;
    double nextRepaint;
    readonly HashSet<SoldierWeaponDefinition> changed = new HashSet<SoldierWeaponDefinition>();

    [MenuItem("Tools/Game/Weapon View Placement")]
    public static void Open() => GetWindow<WeaponViewPlacementWindow>("Weapon View Placement");

    void OnEnable()
    {
        minSize = new Vector2(480, 420);
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
        foreach (var candidate in Object.FindObjectsByType<SoldierFirstPersonView>(FindObjectsSortMode.None))
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
        SaveChanges();
    }

    void SaveChanges()
    {
        foreach (var definition in changed)
            if (definition != null) AssetDatabase.SaveAssetIfDirty(definition);
        changed.Clear();
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("Play Mode: choose Hip or ADS, then drag the handles in Scene view. The preview shows the actual player camera. Changes save to the weapon definition, including when Play Mode ends.", MessageType.Info);
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
        handleMode = GUILayout.Toolbar(handleMode, new[] { "Move weapon view", "Rotate weapon view", "Move pivot" });
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(editing ? "Finish editing" : "Start editing")) { if (editing) StopEditing(); else StartEditing(); }
        if (GUILayout.Button("Frame gun in Scene view")) FrameWeapon();
        if (GUILayout.Button("Save")) SaveChanges();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Position", (ads ? definition.aimOffset : definition.hipFireOffset).ToString("F4"));
        EditorGUILayout.LabelField("Rotation", (ads ? definition.aimRotationOffset : definition.hipFireRotationOffset).ToString("F2"));
        EditorGUILayout.LabelField("Pivot", definition.viewRotationPivot.ToString("F4"));
        if (editing) EditorGUILayout.LabelField("Moving the pivot preserves both Hip and ADS placement.", EditorStyles.wordWrappedLabel);
        DrawPreview();
    }

    void FrameWeapon()
    {
        if (player == null || SceneView.lastActiveSceneView == null) return;
        var loadout = player.GetComponent<SoldierWeaponLoadout>();
        var renderers = loadout.slots[loadout.selectedSlot].firstPersonWeapon.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var mesh in renderers) bounds.Encapsulate(mesh.bounds);
        SceneView.lastActiveSceneView.Frame(bounds, false);
    }

    void DrawPreview()
    {
        var camera = player.playerCamera;
        float aspect = Mathf.Max(.1f, camera.aspect);
        var rect = GUILayoutUtility.GetRect(100, 10000, 180, 10000, GUILayout.ExpandHeight(true));
        float width = Mathf.Min(rect.width, rect.height * aspect);
        rect = new Rect(rect.center.x - width / 2f, rect.center.y - width / aspect / 2f, width, width / aspect);
        int pixelsWide = Mathf.Clamp(Mathf.RoundToInt(width), 128, 1024);
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
        GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, false);
        EditorGUI.DrawRect(new Rect(rect.center.x - 8, rect.center.y, 16, 1), Color.white);
        EditorGUI.DrawRect(new Rect(rect.center.x, rect.center.y - 8, 1, 16), Color.white);
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
        if (Event.current.rawType == EventType.MouseUp) SaveChanges();
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
