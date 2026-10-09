using System;
using UnityEditor;
using UnityEngine;

// Flooded Grounds' original WavingGrass shader used red as its painted bend mask.
// URP Terrain grass expects the same mask in alpha. Keep the source FBX unchanged.
public class FloodedGrassWindImporter : AssetPostprocessor
{
    const string GrassFolder = "Assets/Flooded_Grounds/Content/Meshes/mNature/mGrass/";

    public override uint GetVersion() => 1;

    void OnPostprocessModel(GameObject model)
    {
        if (!assetPath.StartsWith(GrassFolder, StringComparison.Ordinal) ||
            !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;

        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            var colors = mesh.colors;
            for (int i = 0; i < colors.Length; i++) colors[i].a = colors[i].r;
            mesh.colors = colors;
        }
    }
}
