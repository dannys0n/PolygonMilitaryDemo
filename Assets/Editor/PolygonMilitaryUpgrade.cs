using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PolygonMilitaryUpgrade
{
    [MenuItem("Tools/Polygon Military/Upgrade Saved Asset Formats")]
    public static void Run()
    {
        const string root = "Assets/PolygonMilitary";
        var paths = AssetDatabase.GetAllAssetPaths()
            .Where(path => path.StartsWith(root + "/", System.StringComparison.Ordinal)
                && File.Exists(path) && new FileInfo(path).Length > 0)
            .ToArray();
        Debug.Log($"PolygonMilitary upgrade: saving {paths.Length} assets and their metadata.");
        AssetDatabase.ForceReserializeAssets(paths,
            ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);
        Debug.Log("PolygonMilitary upgrade completed. Empty assets were skipped.");
    }
}
