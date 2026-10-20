using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class FpsPackValidation
{
    [Serializable] class Entry { public string source, type, file, skeleton; public bool success; public float duration; }
    [Serializable] class Manifest { public Entry[] assets; }
    [Serializable] class Report { public int files, animations, staticPoses, meshes, textures; public List<string> failures = new(); }
    public static void Run()
    {
        const string prefix = "Assets/FPSAnimationPack/";
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var report = new Report();
        var manifest = JsonUtility.FromJson<Manifest>("{\"assets\":" + File.ReadAllText(prefix + "export-manifest.json") + "}");
        foreach (var row in manifest.assets.Where(e => e.success))
        {
            string path = prefix + row.file.Replace('\\','/');
            report.files++;
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (row.type == "AnimSequence")
            {
                var clips = assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                if (clips.Length == 0)
                {
                    // This source sequence has no keyed tracks. FBX preserves its reference hierarchy only.
                    var hierarchy = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    bool referencePose = row.source == "/FPSAnimationPack/Animations/UE4/Kar98K/Weapon/A_W_Kar98K_Pose"
                        && hierarchy && hierarchy.GetComponentsInChildren<Transform>().Length > 1
                        && !System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)).Contains("KeyTime");
                    if (referencePose) report.staticPoses++;
                    else report.failures.Add(path + ": no animation clip");
                }
                foreach (var clip in clips)
                {
                    report.animations++;
                    if (AnimationUtility.GetCurveBindings(clip).Length == 0) report.failures.Add(path + ": no bone curves");
                    if (row.duration > 0 && Mathf.Abs(clip.length-row.duration) > .05f) report.failures.Add(path + ": duration mismatch " + clip.length + " vs " + row.duration);
                }
            }
            else if (row.type == "SkeletalMesh" || row.type == "StaticMesh")
            {
                if (!assets.OfType<Mesh>().Any(m => m.vertexCount > 0)) report.failures.Add(path + ": no mesh vertices");
                else report.meshes++;
            }
            else if (row.type == "Texture2D")
            {
                if (!assets.OfType<Texture2D>().Any(t => t.width > 0)) report.failures.Add(path + ": no texture");
                else report.textures++;
            }
        }
        File.WriteAllText(Path.Combine(Application.dataPath, "..", "ImportValidation.json"), JsonUtility.ToJson(report,true));
        if (report.files == 0 || report.failures.Count > 0) throw new Exception("FPS pack import incomplete; see ImportValidation.json");
        Debug.Log("FPS_PACK_VALIDATED " + JsonUtility.ToJson(report));
    }
}

