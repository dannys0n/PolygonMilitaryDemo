using UnityEngine;
using UnityEngine.Rendering;

// Cached authored state prevents camera visibility, animation and sight fitting
// from leaking between different owners of the same pooled visual.
public sealed class PooledEquipmentVisual : MonoBehaviour
{
    public GameObject prefab;
    public bool parked;
    Transform[] nodes;
    Vector3[] positions, scales;
    Quaternion[] rotations;
    bool[] active;
    Renderer[] meshes;
    bool[] enabledMeshes, hiddenMeshes;
    ShadowCastingMode[] shadows;
    Material[][] materials;
    WeaponSightAttachment sight, sourceSight;
    Transform alternateRear,alternateFront;
    public void Initialize(GameObject source)
    {
        prefab = source;
        sight=GetComponent<WeaponSightAttachment>();sourceSight=source.GetComponent<WeaponSightAttachment>();
        if(sight){alternateRear=sight.alternateRearSight;alternateFront=sight.alternateFrontSight;}
        nodes = GetComponentsInChildren<Transform>(true);
        positions = new Vector3[nodes.Length]; scales = new Vector3[nodes.Length];
        rotations = new Quaternion[nodes.Length]; active = new bool[nodes.Length];
        for (int i=0;i<nodes.Length;i++)
        { positions[i]=nodes[i].localPosition; rotations[i]=nodes[i].localRotation; scales[i]=nodes[i].localScale; active[i]=nodes[i].gameObject.activeSelf; }
        meshes = GetComponentsInChildren<Renderer>(true);
        enabledMeshes=new bool[meshes.Length];hiddenMeshes=new bool[meshes.Length];
        shadows=new ShadowCastingMode[meshes.Length];materials=new Material[meshes.Length][];
        for(int i=0;i<meshes.Length;i++)
        { enabledMeshes[i]=meshes[i].enabled;hiddenMeshes[i]=meshes[i].forceRenderingOff;shadows[i]=meshes[i].shadowCastingMode;materials[i]=meshes[i].sharedMaterials; }
    }
    public void Restore()
    {
        parked=false;
        if(sight && sourceSight)
        {
            // Optional runtime sight tuning must not carry over to the next borrower.
            sight.alternateRearSight=alternateRear;sight.alternateFrontSight=alternateFront;
            sight.alternateEyeDistance=sourceSight.alternateEyeDistance;
            sight.alternateFieldOfView=sourceSight.alternateFieldOfView;
            sight.alternateSensitivityMultiplier=sourceSight.alternateSensitivityMultiplier;
        }
        for(int i=0;i<nodes.Length;i++) if(nodes[i])
        {
            nodes[i].SetLocalPositionAndRotation(positions[i],rotations[i]);nodes[i].localScale=scales[i];
            if(i>0)nodes[i].gameObject.SetActive(active[i]);
        }
        for(int i=0;i<meshes.Length;i++) if(meshes[i])
        {
            var mesh=meshes[i];mesh.enabled=enabledMeshes[i];mesh.forceRenderingOff=hiddenMeshes[i];
            mesh.shadowCastingMode=shadows[i];mesh.sharedMaterials=materials[i];
            // Runtime equipment blocks belong to the previous viewer (scope/laser/GPU fallback).
            if(mesh.HasPropertyBlock())mesh.SetPropertyBlock(null);
        }
    }
}
