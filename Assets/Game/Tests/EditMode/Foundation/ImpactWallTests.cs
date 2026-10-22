using NUnit.Framework;
using UnityEngine;

public class ImpactWallTests
{
    [Test]
    public void ImpactMarksReuseOneBoundedMesh()
    {
        var go=new GameObject("Collision wall test");go.SetActive(false);
        try
        {
            go.transform.localScale=new Vector3(.5f,5.6f,17f);
            var collider=go.AddComponent<BoxCollider>();var wall=go.AddComponent<ArmoryImpactWall>();
            wall.maximumMarks=4;wall.dotRadius=.025f;go.SetActive(true);
            for(int i=0;i<20;i++)SurfaceImpact.Report(collider,new Vector3(-.25f,i*.01f,0),Vector3.left);
            Assert.That(go.transform.childCount,Is.EqualTo(1));
            var mesh=go.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount,Is.EqualTo(16));
            var vertices=mesh.vertices;
            Assert.That(Vector3.Distance(go.transform.TransformPoint(vertices[0]),go.transform.TransformPoint(vertices[1])),Is.EqualTo(.05f).Within(.0001f));
            Assert.That(go.GetComponentInChildren<MeshRenderer>().enabled,Is.True);
        }
        finally{Object.DestroyImmediate(go);}
    }
    [Test]
    public void DotsHoldThenFadeSlowlyToInvisible()
    {
        var go=new GameObject("Fade test");go.SetActive(false);
        try
        {
            var wall=go.AddComponent<ArmoryImpactWall>();wall.holdDuration=2;wall.fadeDuration=20;
            Assert.That(wall.Opacity(1),Is.EqualTo(1));Assert.That(wall.Opacity(2),Is.EqualTo(1));
            Assert.That(wall.Opacity(12),Is.EqualTo(.5f));Assert.That(wall.Opacity(22),Is.Zero);Assert.That(wall.Opacity(100),Is.Zero);
        }
        finally{Object.DestroyImmediate(go);}
    }
    [Test]
    public void ShootingResetClearsEveryMarkAndReusesTheMesh()
    {
        var wallObject=new GameObject("Clear wall test");wallObject.SetActive(false);
        var resetObject=new GameObject("Reset plate test");
        try
        {
            var wall=wallObject.AddComponent<ArmoryImpactWall>();wall.maximumMarks=4;wallObject.SetActive(true);
            wall.ReceiveImpact(Vector3.zero,Vector3.forward);wall.ReceiveImpact(Vector3.right,Vector3.forward);
            var mesh=wallObject.GetComponentInChildren<MeshFilter>().sharedMesh;
            var receiver=resetObject.AddComponent<ArmoryImpactReset>();receiver.wall=wall;
            SurfaceImpact.Report(resetObject.AddComponent<BoxCollider>(),Vector3.zero,Vector3.forward);
            Assert.That(wallObject.GetComponentInChildren<MeshRenderer>().enabled,Is.False);
            foreach(var color in mesh.colors32)Assert.That(color.a,Is.Zero);
            wall.ReceiveImpact(Vector3.up,Vector3.forward);
            Assert.That(wallObject.GetComponentInChildren<MeshFilter>().sharedMesh,Is.SameAs(mesh));
            int visible=0;foreach(var color in mesh.colors32)if(color.a>0)visible++;
            Assert.That(visible,Is.EqualTo(4));
        }
        finally{Object.DestroyImmediate(resetObject);Object.DestroyImmediate(wallObject);}
    }
    [Test]
    public void OrdinarySurfacesDoNotNeedImpactReceivers()
    {
        var go=new GameObject("Ordinary surface");
        try{Assert.DoesNotThrow(()=>SurfaceImpact.Report(go.AddComponent<BoxCollider>(),Vector3.zero,Vector3.up));}
        finally{Object.DestroyImmediate(go);}
    }
}
