using UnityEngine;

public class SoldierHealth : MonoBehaviour
{
    [Min(1f)] public float maxHealth = 200f;
    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;
    // The accepted hit retains its firing system even after the shooter switches seats or weapons.
    public Sprite LastDamageIcon { get; private set; }
    public event System.Action<float, GameObject> Damaged;
    public event System.Action<float, Vector3> DirectionalDamage;
    public event System.Action<GameObject> Died;
    // Optional rules supplied by the owning game mode (teams, invulnerability, etc.).
    public System.Func<GameObject, bool> DamageFilter { get; set; }

    void Awake() => ResetHealth();

    public void ResetHealth() { CurrentHealth = maxHealth; LastDamageIcon = null; }

    public bool ApplyDamage(float amount, GameObject source, Sprite weaponIcon = null, Vector3? damageOrigin = null)
    {
        if (IsDead || amount <= 0f) return false;
        if (DamageFilter != null && !DamageFilter(source)) return false;
        LastDamageIcon = weaponIcon;
        float previous=CurrentHealth;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        bool died = IsDead;
        CombatEvents.ConfirmDamage(source,gameObject,previous-CurrentHealth,died);
        Damaged?.Invoke(amount, source);
        if(damageOrigin.HasValue) DirectionalDamage?.Invoke(amount, damageOrigin.Value);
        else if(source && source != gameObject && !source.transform.IsChildOf(transform))
            DirectionalDamage?.Invoke(amount, source.transform.position);
        if (died) Died?.Invoke(source);
        return true;
    }
}
