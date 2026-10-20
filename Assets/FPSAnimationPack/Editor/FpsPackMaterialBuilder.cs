using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class FpsPackMaterialBuilder
{
    [Serializable] public class Definition {public string source,file,baseTexture,normalTexture;public float[] color;public float metallic,smoothness;public bool invisible;}
    [Serializable] public class Mesh {public string source,file;public string[] materials,resolvedMaterials;}
    [Serializable] public class Layout {public Definition[] materials;public Mesh[] meshes;}
    public static void Apply(string path,GameObject model)
    {
        string metadata=FpsPackPreviewCatalog.Root+"material-layout.json";
        if(!File.Exists(metadata))return;
        var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(metadata));
        var mesh=layout.meshes.FirstOrDefault(m=>FpsPackPreviewCatalog.Root+m.file==path);
        if(mesh?.resolvedMaterials==null || mesh.resolvedMaterials.Length==0)return;
        int index=0;
        foreach(var renderer in model.GetComponentsInChildren<Renderer>())
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++,index++)
            {
                if(index>=mesh.resolvedMaterials.Length)throw new Exception("Material layout changed: "+path);
                string source=mesh.resolvedMaterials[index];if(string.IsNullOrEmpty(source))continue;
                var definition=layout.materials.Single(m=>m.source==source);
                materials[i]=AssetDatabase.LoadAssetAtPath<Material>(FpsPackPreviewCatalog.Root+definition.file);
            }
            renderer.sharedMaterials=materials;
        }
    }
    [MenuItem("Tools/FPS Animation Pack/Rebuild source materials")]
    public static string Build()
    {
        const string root=FpsPackPreviewCatalog.Root;
        var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(root+"material-layout.json"));
        var mapped=new Dictionary<string,Material>();int slots=0,textured=0;
        foreach(var definition in layout.materials)
        {
            string path=root+definition.file;Directory.CreateDirectory(Path.GetDirectoryName(path));
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.name=Path.GetFileNameWithoutExtension(path);material.shader=Shader.Find("Universal Render Pipeline/Lit");
            var color=definition.color;var linear=new Color(color[0],color[1],color[2],color[3]);
            var texture=string.IsNullOrEmpty(definition.baseTexture)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(root+definition.baseTexture);
            if(!string.IsNullOrEmpty(definition.baseTexture) && !texture)throw new Exception("Missing source texture: "+definition.baseTexture);
            material.SetColor("_BaseColor",texture?Color.white:linear.gamma);
            material.SetTexture("_BaseMap",texture);material.SetFloat("_Metallic",definition.metallic);material.SetFloat("_Smoothness",definition.smoothness);
            if(texture)textured++;
            if(!string.IsNullOrEmpty(definition.normalTexture))
            {
                string normalPath=root+definition.normalTexture;
                var importer=(TextureImporter)AssetImporter.GetAtPath(normalPath);
                if(importer.textureType!=TextureImporterType.NormalMap){importer.textureType=TextureImporterType.NormalMap;importer.SaveAndReimport();}
                material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));material.EnableKeyword("_NORMALMAP");
            }
            if(definition.invisible)
            {
                material.SetColor("_BaseColor",Color.clear);material.SetFloat("_Surface",1);material.SetFloat("_ZWrite",0);
                material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=(int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);mapped.Add(definition.source,material);
        }
        AssetDatabase.SaveAssets();
        foreach(var mesh in layout.meshes)
        {
            string path=root+mesh.file;var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!source)throw new Exception("Missing model: "+path);
            var materials=source.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).ToArray();
            var used=new HashSet<int>();
            mesh.resolvedMaterials=new string[materials.Length];
            for(int i=0;i<materials.Length;i++)
            {
                if(materials.Length==mesh.materials.Length){mesh.resolvedMaterials[i]=mesh.materials[i];continue;}
                string name=materials[i]?materials[i].name:"";
                int index=Enumerable.Range(0,mesh.materials.Length).Where(n=>!used.Contains(n) && !string.IsNullOrEmpty(mesh.materials[n]) && Path.GetFileName(mesh.materials[n])==name).DefaultIfEmpty(-1).First();
                if(index>=0){used.Add(index);mesh.resolvedMaterials[i]=mesh.materials[index];continue;}
                if(name.StartsWith("Fbx Default Material") || string.IsNullOrEmpty(name)){mesh.resolvedMaterials[i]=null;continue;}
                throw new Exception("Unresolved native material: "+mesh.file+" "+name);
            }
            slots+=mesh.resolvedMaterials.Count(m=>!string.IsNullOrEmpty(m));
        }
        File.WriteAllText(root+"material-layout.json",JsonUtility.ToJson(layout,true));
        AssetDatabase.ImportAsset(root+"material-layout.json",ImportAssetOptions.ForceSynchronousImport);
        foreach(var mesh in layout.meshes)
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath(root+mesh.file);
            foreach(var key in importer.GetExternalObjectMap().Keys.Where(k=>k.type==typeof(Material)).ToArray())importer.RemoveRemap(key);
            importer.SaveAndReimport();
        }
        var report="Source materials="+mapped.Count+"; textured materials="+textured+"; remapped slots="+slots;
        File.WriteAllText("MaterialConversionReport.txt",report);return report;
    }
}
