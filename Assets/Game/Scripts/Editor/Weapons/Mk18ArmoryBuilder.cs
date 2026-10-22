using UnityEditor;
// MK18 uses the shared conversion pipeline, just like the other weapons.
public static class Mk18ArmoryBuilder
{
 [MenuItem("Tools/Game/Animation Tests/Build MK18 Polygon on UE4 proof")]public static void Rebuild()=>Main();
 public static object Main(){PolygonWeaponConversionBuilder.Build("legacy-mk18");PolygonWeaponConversionBuilder.BuildBody("legacy-mk18");return PolygonWeaponConversionBuilder.Integrate();}
}
