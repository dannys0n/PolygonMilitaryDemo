using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

// Preserve source bones and curves for inspection before any gameplay retargeting.
public class FpsPackImportSettings : AssetPostprocessor
{
    [Serializable] public class Row { public string file, type, source; }
    [Serializable] public class Manifest { public Row[] assets; }
    public override uint GetVersion() => 2;
    public override int GetPostprocessOrder() => 1000;
    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        OnPostprocessMaterial(material);
    }
    void OnPostprocessMaterial(Material material)
    {
        if (!assetPath.StartsWith("Assets/FPSAnimationPack/")) return;
        var texture = material.mainTexture;
        var color = material.color;
        // FBX cannot carry Unreal's procedural material graph; keep untextured previews legible.
        if (color.maxColorComponent < .02f) color = new Color(.55f,.55f,.55f,1);
        material.shader = Shader.Find("Universal Render Pipeline/Lit");
        string family = material.name.Contains("Glove") ? "Glove" : material.name.Contains("Cloth") ? "Cloth" : material.name.Contains("Hand") ? "Arm" : null;
        if (family != null)
        {
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/FPSAnimationPack/Textures/Characters/FPS_Arms_UE4/Textures/T_"+family+"01_B.png");
            if (texture) color = Color.white;
        }
        material.SetColor("_BaseColor", color);
        if (texture) material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Smoothness", .3f);
    }
    void OnPostprocessModel(GameObject model) {if(assetPath.StartsWith("Assets/FPSAnimationPack/Meshes/"))FpsPackMaterialBuilder.Apply(assetPath,model);}
    void OnPostprocessAnimation(GameObject root,AnimationClip clip)
    {
        const string prefix="Assets/FPSAnimationPack/";
        if(!assetPath.StartsWith(prefix+"Animations/"))return;
        var manifest=JsonUtility.FromJson<Manifest>("{\"assets\":"+File.ReadAllText(prefix+"export-manifest.json")+"}");
        var row=manifest.assets.FirstOrDefault(r=>r.file.Replace('\\','/')==assetPath.Substring(prefix.Length));
        if(row==null)return;
        var corrected=FpsPackTranslationRepair.Apply(clip,row.source);
        if(corrected!=clip)
        {
            var flags=clip.hideFlags;
            EditorUtility.CopySerialized(corrected,clip);
            clip.hideFlags=flags;
            UnityEngine.Object.DestroyImmediate(corrected);
        }
    }
    void OnPreprocessModel()
    {
        const string prefix = "Assets/FPSAnimationPack/";
        if (!assetPath.StartsWith(prefix)) return;
        var model = (ModelImporter)assetImporter;
        string type = null;
        string manifestPath = prefix + "export-manifest.json";
        if (File.Exists(manifestPath))
        {
            var manifest = JsonUtility.FromJson<Manifest>("{\"assets\":" + File.ReadAllText(manifestPath) + "}");
            type = manifest.assets.FirstOrDefault(r => r.file.Replace('\\','/') == assetPath.Substring(prefix.Length))?.type;
        }
        bool animation = assetPath.StartsWith(prefix + "Animations/");
        bool skeletal = animation || type == "SkeletalMesh";
        model.bakeAxisConversion = true;
        model.importCameras = false;
        model.importLights = false;
        model.importConstraints = false;
        model.optimizeGameObjects = false;
        model.isReadable = true;
        model.animationCompression = ModelImporterAnimationCompression.Off;
        model.animationType = skeletal ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        model.importAnimation = animation;
        if (skeletal) model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    }
}
