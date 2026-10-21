using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Authoring only: split complete disconnected iron-sight parts, retaining their skinning.
public static class ArenaWeaponIronParts
{
 public static void Configure(SoldierWeaponView view,string key,string folder)
 {
  if(key!="KXG12_UE4"&&key!="Striker-V_UE4"&&key!="polygon-buzzsub")return;
  var rear=view.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.name=="BuiltInRearIron");
  var front=view.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.name=="BuiltInFrontIron");
  if(!rear||!front)
  {
   var skin=view.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.sharedMesh.vertexCount>1000);
   var mesh=skin.sharedMesh;var baked=new Mesh();skin.BakeMesh(baked);
   var points=baked.vertices.Select(p=>view.opticMount.InverseTransformPoint(skin.transform.TransformPoint(p))).ToArray();Object.DestroyImmediate(baked);
   var groups=Islands(mesh);var labels=new int[mesh.vertexCount];
   float rearZ=view.opticMount.InverseTransformPoint(view.rearSight.position).z,frontZ=view.opticMount.InverseTransformPoint(view.frontSight.position).z;
   foreach(var group in groups)
   {
    var b=new Bounds(points[group[0]],Vector3.zero);foreach(int i in group)b.Encapsulate(points[i]);
    if(b.min.y<-.003f||b.max.y<.007f||b.size.z>.065f)continue;
    int label=Mathf.Abs(b.center.z-rearZ)<.05f?1:Mathf.Abs(b.center.z-frontZ)<.05f?2:0;
    foreach(int i in group)labels[i]=label;
   }
   if(!labels.Contains(1)||!labels.Contains(2))throw new InvalidOperationException(view.name+": could not isolate both iron sights.");
   var meshes=Enumerable.Range(0,3).Select(label=>Save(ExtractPart(mesh,labels,label),folder+"/"+key+(label==0?"_BodyWithoutIrons":label==1?"_RearIron":"_FrontIron")+".asset")).ToArray();
   Renderer Make(string name,Mesh part){var go=new GameObject(name);go.transform.SetParent(skin.transform,false);var r=go.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=part;r.sharedMaterials=skin.sharedMaterials;r.bones=skin.bones;r.rootBone=skin.rootBone;r.localBounds=skin.localBounds;r.quality=skin.quality;r.updateWhenOffscreen=skin.updateWhenOffscreen;r.shadowCastingMode=skin.shadowCastingMode;r.receiveShadows=skin.receiveShadows;return r;}
   rear=Make("BuiltInRearIron",meshes[1]);front=Make("BuiltInFrontIron",meshes[2]);skin.sharedMesh=meshes[0];
  }
  foreach(var mount in view.attachments)
  {if(mount.kind==WeaponAttachmentDefinition.MountKind.RearIron)mount.builtIn=new[]{rear};if(mount.kind==WeaponAttachmentDefinition.MountKind.FrontIron)mount.builtIn=new[]{front};}
  if(view.TryGetComponent<SoldierWeaponAttachments>(out var attachments))foreach(var mount in attachments.mounts)
  {var source=Array.Find(view.attachments,m=>m.kind==mount.kind);mount.builtInThirdPerson=source?.builtIn??Array.Empty<Renderer>();}
 }
 public static List<int[]> Islands(Mesh mesh)
 {
  var verts=mesh.vertices;var parent=Enumerable.Range(0,verts.Length).ToArray();int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}void Join(int a,int b){parent[Root(a)]=Root(b);}
  var weld=new Dictionary<Vector3Int,int>();for(int i=0;i<verts.Length;i++){var p=verts[i]*100000;var k=new Vector3Int(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.y),Mathf.RoundToInt(p.z));if(weld.TryGetValue(k,out int j))Join(i,j);else weld[k]=i;}
  var tri=mesh.triangles;for(int i=0;i<tri.Length;i+=3){Join(tri[i],tri[i+1]);Join(tri[i],tri[i+2]);}
  return Enumerable.Range(0,verts.Length).GroupBy(Root).Select(g=>g.ToArray()).ToList();
 }
 public static Mesh ExtractPart(Mesh source,int[] labels,int label)
 {
  if(source.blendShapeCount!=0)throw new InvalidOperationException("Weapon iron extraction does not support blend shapes.");
  var used=Enumerable.Range(0,labels.Length).Where(i=>labels[i]==label).ToArray();var map=new int[labels.Length];for(int i=0;i<used.Length;i++)map[used[i]]=i;
  var vertices=source.vertices;var result=new Mesh{indexFormat=used.Length>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16,vertices=used.Select(i=>vertices[i]).ToArray(),bindposes=source.bindposes};
  var normals=source.normals;if(normals.Length>0)result.normals=used.Select(i=>normals[i]).ToArray();var tangents=source.tangents;if(tangents.Length>0)result.tangents=used.Select(i=>tangents[i]).ToArray();var colors=source.colors32;if(colors.Length>0)result.colors32=used.Select(i=>colors[i]).ToArray();var weights=source.boneWeights;if(weights.Length>0)result.boneWeights=used.Select(i=>weights[i]).ToArray();
  for(int channel=0;channel<8;channel++){var uv=new List<Vector4>();source.GetUVs(channel,uv);if(uv.Count>0)result.SetUVs(channel,used.Select(i=>uv[i]).ToList());}
  result.subMeshCount=source.subMeshCount;for(int sub=0;sub<source.subMeshCount;sub++){var input=source.GetTriangles(sub);var output=new List<int>();for(int i=0;i<input.Length;i+=3)if(labels[input[i]]==label){output.Add(map[input[i]]);output.Add(map[input[i+1]]);output.Add(map[input[i+2]]);}result.SetTriangles(output,sub);}
  result.RecalculateBounds();return result;
 }
 static Mesh Save(Mesh mesh,string path){mesh.name=System.IO.Path.GetFileNameWithoutExtension(path);var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){existing.Clear(false);existing.indexFormat=mesh.indexFormat;existing.vertices=mesh.vertices;existing.normals=mesh.normals;existing.tangents=mesh.tangents;existing.colors32=mesh.colors32;existing.bindposes=mesh.bindposes;existing.boneWeights=mesh.boneWeights;for(int channel=0;channel<8;channel++){var uv=new List<Vector4>();mesh.GetUVs(channel,uv);if(uv.Count>0)existing.SetUVs(channel,uv);}existing.subMeshCount=mesh.subMeshCount;for(int sub=0;sub<mesh.subMeshCount;sub++)existing.SetTriangles(mesh.GetTriangles(sub),sub);existing.bounds=mesh.bounds;Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}AssetDatabase.CreateAsset(mesh,path);return mesh;}
}
