using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Restore attachment anchors in receiver units after the centimeter-to-meter mesh conversion.
public static class M249SocketRepair
{
 public static bool Repair(WeaponAnimationConversionProof proof)
 {
  if(!proof.converted.weapon.catalogId.Contains("polygon-machinegun"))return false;
  var current=proof.converted.firstPersonPrefab;
  if(current.opticMount&&current.transform.InverseTransformPoint(current.opticMount.position).magnitude<1f)return false;
  var original=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Weapons/Bases/M249_Base.prefab"));
  try
  {
   var baseCover=original.GetComponentsInChildren<Transform>(true).First(t=>t.name=="open");
   var baseMount=original.GetComponentsInChildren<Transform>(true).First(t=>t.name=="OpticMount");
   foreach(var view in new[]{proof.converted.firstPersonPrefab,proof.converted.thirdPersonPrefab})
   {
    string path=AssetDatabase.GetAssetPath(view);var root=PrefabUtility.LoadPrefabContents(path);
    try
    {
     var cover=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="open");
     float unitRatio=cover.localPosition.magnitude/baseCover.localPosition.magnitude;
     foreach(var socket in root.GetComponentsInChildren<Transform>(true).Where(t=>new[]{"OpticMount","SightRiserMount","FittedOpticMount","FittedSightRiserMount"}.Contains(t.name)).ToArray())
     {
      socket.SetParent(cover,false);socket.SetLocalPositionAndRotation(baseMount.localPosition*unitRatio,baseMount.localRotation);socket.localScale=Vector3.one;
     }
     PrefabUtility.SaveAsPrefabAsset(root,path);
    }finally{PrefabUtility.UnloadPrefabContents(root);}
   }
   // Socket poses belong to the prefab and its cover hinge, not the arm action.
   foreach(var clip in new[]{proof.idle,proof.reload,proof.fire,proof.aimFire}.Where(c=>c).Distinct())
   {
    foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path.Contains("FittedOpticMount")||b.path.Contains("FittedSightRiserMount")).ToArray())AnimationUtility.SetEditorCurve(clip,binding,null);
    EditorUtility.SetDirty(clip);
   }
   AssetDatabase.SaveAssets();return true;
  }finally{Object.DestroyImmediate(original);}
 }
}
