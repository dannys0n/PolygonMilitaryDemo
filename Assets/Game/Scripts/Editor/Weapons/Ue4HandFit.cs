using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Editor-only surface/weight transfer. Polygon keeps its fused three-finger block.
// Keep Polygon topology/UVs; use the source glove as a fitting cage, not a runtime rig.
public static class Ue4HandFit
{
    public static int Refine(Mesh mesh, Transform[] bones, SkinnedMeshRenderer native)
    {
        var baked = new Mesh();
        try
        {
            native.BakeMesh(baked);
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var sourceVertices = baked.vertices.Select(native.transform.TransformPoint).ToArray();
            var sourceWeights = native.sharedMesh.boneWeights;
            var sourceTriangles = baked.triangles;
            var index = bones.Select((b, i) => (b.name, i)).GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.First().i);
            var pairs = bones.Select(b => index.TryGetValue(b.name.EndsWith("_l") ? b.name.Substring(0, b.name.Length - 2) + "_r" : b.name, out int i) ? i : -1).ToArray();
            int count = vertices.Length / 2, fitted = 0;
            IEnumerable<(int bone, float weight)> Influences(BoneWeight w)
            { yield return (w.boneIndex0, w.weight0); yield return (w.boneIndex1, w.weight1); yield return (w.boneIndex2, w.weight2); yield return (w.boneIndex3, w.weight3); }
            bool Matches(string name, string family) => name.EndsWith("_l") && (family == "fingers_" ? name.StartsWith("ring_") || name.StartsWith("middle_") || name.StartsWith("pinky_") : name.StartsWith(family));
            float Membership(BoneWeight w, Transform[] skeleton, string family) => Influences(w).Where(p => p.weight > 0 && Matches(skeleton[p.bone].name, family)).Sum(p => p.weight);
            foreach (string family in new[] { "thumb_", "index_", "fingers_", "hand_" })
            {
                var candidates = new List<int>();
                for (int t = 0; t < sourceTriangles.Length; t += 3)
                    if ((Membership(sourceWeights[sourceTriangles[t]], native.bones, family) + Membership(sourceWeights[sourceTriangles[t+1]], native.bones, family) + Membership(sourceWeights[sourceTriangles[t+2]], native.bones, family)) / 3 > .55f) candidates.Add(t);
                if (candidates.Count == 0) throw new InvalidOperationException("No source finger surface: " + family);
                for (int i = 0; i < count; i++)
                {
                    float membership = Membership(weights[i], bones, family);
                    if (membership < .65f) continue;
                    float distance = float.PositiveInfinity; int triangle = -1; Vector3 best = vertices[i], bary = default;
                    foreach (int t in candidates)
                    {
                        var a = sourceVertices[sourceTriangles[t]]; var b = sourceVertices[sourceTriangles[t+1]]; var c = sourceVertices[sourceTriangles[t+2]];
                        var point = Closest(vertices[i], a, b, c, out var uvw);
                        float d = (point - vertices[i]).sqrMagnitude;
                        if (d < distance) { distance = d; triangle = t; best = point; bary = uvw; }
                    }
                    if (triangle < 0 || distance > .08f * .08f) continue;
                    var influences = new Dictionary<int, float>();
                    void Add(BoneWeight w, float factor)
                    {
                        foreach (var p in Influences(w))
                            if (p.weight > 0 && index.TryGetValue(native.bones[p.bone].name.Replace("middle_", "ring_").Replace("pinky_", "ring_"), out int target))
                                influences[target] = influences.GetValueOrDefault(target) + p.weight * factor;
                    }
                    Add(sourceWeights[sourceTriangles[triangle]], bary.x);
                    Add(sourceWeights[sourceTriangles[triangle+1]], bary.y);
                    Add(sourceWeights[sourceTriangles[triangle+2]], bary.z);
                    var sorted = influences.OrderByDescending(p => p.Value).Take(4).ToArray();
                    if (sorted.Length == 0) continue;
                    var w = default(BoneWeight); float sum = sorted.Sum(p => p.Value);
                    for (int j = 0; j < sorted.Length; j++) Set(ref w, j, sorted[j].Key, sorted[j].Value / sum);
                    // Restore the fuller Polygon silhouette across each bone, without
                    // lengthening fingers or moving their joints/contact timing.
                    var fullness = Vector3.zero;
                    float thickness = family == "hand_" ? .12f : .20f;
                    float width = family == "fingers_" ? .10f : thickness;
                    foreach (var p in Influences(w))
                    {
                        if (p.weight <= 0) continue;
                        var frame = Frame(bones[p.bone], index, bones);
                        var local = frame.inverse.MultiplyPoint3x4(best);
                        float tip = TipFullness(bones[p.bone], local);
                        float tipWidth = family == "fingers_" ? tip * .5f : tip;
                        fullness += frame.MultiplyVector(new Vector3(local.x * (width + tipWidth), local.y * (thickness + tip), 0)) * p.weight;
                    }
                    best += fullness;
                    vertices[i] = best; weights[i] = w;
                    var right = Vector3.zero; var rw = default(BoneWeight);
                    int slot = 0;
                    foreach (var p in Influences(w))
                    {
                        if (p.weight <= 0) continue;
                        int paired = pairs[p.bone];
                        if (paired < 0) throw new InvalidOperationException("Missing mirrored finger bone");
                        var frame = Frame(bones[p.bone], index, bones);
                        var other = Frame(bones[paired], index, bones);
                        right += (other * Matrix4x4.Scale(new Vector3(1,-1,1)) * frame.inverse).MultiplyPoint3x4(best) * p.weight;
                        Set(ref rw, slot++, paired, p.weight);
                    }
                    vertices[count+i] = right; weights[count+i] = rw; fitted++;
                }
            }
            mesh.vertices = vertices; mesh.boneWeights = weights;
            return fitted;
        }
        finally { UnityEngine.Object.DestroyImmediate(baked); }
    }
    static float TipFullness(Transform bone, Vector3 local)
    {
        if (!(bone.name.StartsWith("thumb_") || bone.name.StartsWith("index_") || bone.name.StartsWith("ring_"))) return 0;
        int segment = bone.name.Contains("_03_") ? 2 : bone.name.Contains("_02_") ? 1 : 0;
        if (segment == 0) return 0;
        var next = bone.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.parent == bone);
        float length = next ? Vector3.Distance(next.position, bone.position) : Vector3.Distance(bone.position, bone.parent.position) * .8f;
        float progress = Mathf.Clamp01(local.z / Mathf.Max(.001f, length));
        // Smoothly restore volume over the last two segments, strongest at the pad.
        return .35f * Mathf.SmoothStep(0, 1, (segment + progress - 1) * .5f);
    }
    static Matrix4x4 Frame(Transform b, Dictionary<string,int> indices, Transform[] bones)
    {
        string side = b.name.EndsWith("_r") ? "r" : "l";
        var hand = bones[indices["hand_"+side]];
        var normal = Vector3.Cross(bones[indices["index_01_"+side]].position-hand.position, bones[indices["ring_01_"+side]].position-hand.position).normalized;
        var next = b.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.parent == b && !t.name.Contains("twist"));
        var forward = b == hand ? bones[indices["index_01_"+side]].position-b.position : next ? next.position-b.position : b.position-b.parent.position;
        return Matrix4x4.TRS(b.position, Quaternion.LookRotation(forward,normal), Vector3.one);
    }
    static void Set(ref BoneWeight w, int slot, int bone, float weight)
    {
        switch (slot) { case 0: w.boneIndex0=bone; w.weight0=weight; break; case 1:w.boneIndex1=bone;w.weight1=weight;break;case 2:w.boneIndex2=bone;w.weight2=weight;break;case 3:w.boneIndex3=bone;w.weight3=weight;break; }
    }
    static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0&&d2<=0){bary=new Vector3(1,0,0);return a;}
        var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
        if(d3>=0&&d4<=d3){bary=new Vector3(0,1,0);return b;}
        float vc=d1*d4-d3*d2;
        if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);bary=new Vector3(1-v,v,0);return a+v*ab;}
        var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
        if(d6>=0&&d5<=d6){bary=new Vector3(0,0,1);return c;}
        float vb=d5*d2-d1*d6;
        if(vb<=0&&d2>=0&&d6<=0){float v=d2/(d2-d6);bary=new Vector3(1-v,0,v);return a+v*ac;}
        float va=d3*d6-d5*d4;
        if(va<=0&&d4-d3>=0&&d5-d6>=0){float v=(d4-d3)/((d4-d3)+(d5-d6));bary=new Vector3(0,1-v,v);return b+v*(c-b);}
        float inv=1/(va+vb+vc);float y=vb*inv,z=vc*inv;bary=new Vector3(1-y-z,y,z);return a+ab*y+ac*z;
    }
}

