using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FpsPackPackaging
{
    [MenuItem("Tools/FPS Animation Pack/Rebuild animation index")]
    public static void BuildIndex()
    {
        var catalog=new FpsPackPreviewCatalog();
        var files=catalog.Entries.Where(e=>e.type=="AnimSequence" && FpsPackPreview.LoadClip(e.Path)!=null).Select(e=>e.file).ToArray();
        string path=FpsPackPreviewCatalog.Root+"preview-index.json";
        File.WriteAllText(Path.Combine(Application.dataPath,"..",path),JsonUtility.ToJson(new FpsPackPreviewCatalog.PreviewIndex {playableFiles=files},true));
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("FPS_PREVIEW_INDEX_BUILT "+files.Length+" playable animations");
    }

    [MenuItem("Tools/FPS Animation Pack/Export import package")]
    public static void Export()
    {
        BuildIndex();
        string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Exports"));
        Directory.CreateDirectory(directory);
        string output=Path.Combine(directory,"FPSAnimationPack-Unity-URP.unitypackage");
        string temporary=Path.Combine(directory,"FPSAnimationPack-Unity-URP.building.unitypackage");
        // Recurse includes only the converted pack and its editor tool, never project settings/packages.
        UnityEditor.AssetPackage.Package.Export(new UnityEditor.AssetPackage.ExportPackageParameters(
            FpsPackPreviewCatalog.Root.TrimEnd('/'),temporary,null,ExportPackageOptions.Recurse));
        if(File.Exists(output))File.Replace(temporary,output,null);else File.Move(temporary,output);
        File.WriteAllText(Path.Combine(directory,"ExportComplete.json"),JsonUtility.ToJson(new ExportResult {file=output,bytes=new FileInfo(output).Length},true));
        Debug.Log("FPS_PACKAGE_EXPORTED "+output);
    }
    [Serializable] class ExportResult {public string file;public long bytes;}
}
