using UnityEngine;
[CreateAssetMenu(menuName="Soldier/Grenade")]
public class GrenadeDefinition : ScriptableObject
{
    public enum GrenadeKind { Frag, Smoke, Flashbang }
    public GrenadeKind kind;
    public string displayName;
    public GrenadeProjectile projectile;
    public ParticleSystem effect;
    public Vector3 effectRotation=new Vector3(-90,0,0);
    public Sprite icon;
    public int startingCount=1;
    public float fuse=2.5f, throwSpeed=14f, upwardSpeed=2.5f;
    public float radius=6f, infantryDamage=120f, vehicleDamage=180f, effectScale=.35f;
    public float flashDuration=4f;
}
