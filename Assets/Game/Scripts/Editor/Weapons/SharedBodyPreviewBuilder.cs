using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// One body hierarchy for the armory. Weapon geometry and its placement remain equipment data.
public static class SharedBodyPreviewBuilder
{
    public const string SharedPrefabPath="Assets/Game/AnimationTests/UE4RigProof/SharedScaledBodyPreview.prefab";
    static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);

    public static object Configure(IEnumerable<WeaponAnimationConversionProof> input)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Configure shared previews in Edit Mode.");
        var proofs=input.Where(p=>p&&p.converted?.thirdPersonPrefab&&(p.thirdPersonProof||(p.sharedBodyPrefab&&p.sharedBodyWeaponPrefab))).ToArray();
        if(proofs.Length==0)throw new InvalidOperationException("No complete TPS proofs supplied.");
        GameObject shared=AssetDatabase.LoadAssetAtPath<GameObject>(SharedPrefabPath);
        if(!shared)
        {
        var templateSource=proofs.FirstOrDefault(p=>p.thirdPersonProof);
        if(!templateSource)throw new InvalidOperationException("No complete body remains to author the shared preview.");
        var template=Object.Instantiate(templateSource.thirdPersonProof);
        try
        {
            if(PrefabUtility.IsPartOfPrefabInstance(template))PrefabUtility.UnpackPrefabInstance(template,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            template.name="SharedScaledBodyPreview";
            var gun=Find(template.transform,"SourceWeapon");
            if(!gun)throw new InvalidOperationException("Body proof is missing SourceWeapon.");
            Object.DestroyImmediate(gun.gameObject);
            foreach(var animator in template.GetComponentsInChildren<Animator>(true))Object.DestroyImmediate(animator);
            shared=PrefabUtility.SaveAsPrefabAsset(template,SharedPrefabPath);
        }
        finally{Object.DestroyImmediate(template);}
        }
        foreach(var proof in proofs)
        {
            if(proof.sharedBodyPrefab==shared && proof.sharedBodyWeaponPrefab)continue;
            if(!proof.thirdPersonProof)throw new InvalidOperationException(AssetDatabase.GetAssetPath(proof)+" needs a body rebake.");
            var gun=Find(proof.thirdPersonProof.transform,"SourceWeapon");
            var view=proof.converted.firstPersonPrefab;
            var weapon=view.sharedFirstPersonRig?view.transform:Find(view.transform,"SourceWeapon");
            if(!gun||!weapon)throw new InvalidOperationException(AssetDatabase.GetAssetPath(proof)+" is missing its weapon geometry.");
            string weaponPath=Path.ChangeExtension(AssetDatabase.GetAssetPath(proof.thirdPersonProof),null)+"_Weapon.prefab";
            var weaponPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
            {
                var extracted=Object.Instantiate(gun.gameObject);
                try
                {
                    if(PrefabUtility.IsPartOfPrefabInstance(extracted))PrefabUtility.UnpackPrefabInstance(extracted,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                    weaponPrefab=PrefabUtility.SaveAsPrefabAsset(extracted,weaponPath);
                }
                finally{Object.DestroyImmediate(extracted);}
            }
            proof.sharedBodyWeaponPrefab=weaponPrefab;
            // Old previews implicitly depended on unanimated rest transforms.
            // Store those defaults in the clips so the common body is independent
            // of which weapon originally supplied its neutral pose.
            var sourceArms=Find(proof.thirdPersonProof.transform,"SourceArms");
            foreach(var clip in new[]{proof.bodyIdle,proof.bodyReload}.Where(c=>c))
            {
                var bindings=AnimationUtility.GetCurveBindings(clip).ToHashSet();
                var additions=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
                foreach(var bone in sourceArms.GetComponentsInChildren<Transform>(true))
                {
                    string path=AnimationUtility.CalculateTransformPath(bone,proof.thirdPersonProof.transform);
                    for(int axis=0;axis<10;axis++)
                    {
                        string property=axis<3?"m_LocalPosition."+"xyz"[axis]:axis<7?"m_LocalRotation."+"xyzw"[axis-3]:"m_LocalScale."+"xyz"[axis-7];
                        var binding=EditorCurveBinding.FloatCurve(path,typeof(Transform),property);
                        if(bindings.Contains(binding))continue;
                        float value=axis<3?bone.localPosition[axis]:axis<7?bone.localRotation[axis-3]:bone.localScale[axis-7];
                        additions.Add(binding);curves.Add(AnimationCurve.Constant(0,clip.length,value));
                    }
                }
                if(additions.Count>0){AnimationUtility.SetEditorCurves(clip,additions.ToArray(),curves.ToArray());EditorUtility.SetDirty(clip);}
            }
            proof.sharedBodyPrefab=shared;
            proof.bodyWeaponPosition=gun.localPosition;
            proof.bodyWeaponEuler=gun.localEulerAngles;
            proof.bodyWeaponScale=gun.localScale;
            EditorUtility.SetDirty(proof);
        }
        AssetDatabase.SaveAssets();
        return new{sharedPrefab=SharedPrefabPath,configured=proofs.Length};
    }
}
