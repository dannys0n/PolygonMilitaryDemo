using UnityEditor;
// MK18 uses the shared conversion pipeline, just like the other weapons.
public static class ArenaMk18ArmoryBuilder
{
 [MenuItem("Tools/Arena Shooter/Animation Tests/Build MK18 Polygon on UE4 proof")]public static void Rebuild()=>Main();
 public static object Main(){ArenaPolygonWeaponConversionBuilder.Build("legacy-mk18");ArenaPolygonWeaponConversionBuilder.BuildBody("legacy-mk18");return ArenaPolygonWeaponConversionBuilder.Integrate();}
}
