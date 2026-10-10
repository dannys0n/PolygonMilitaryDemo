using UnityEngine;

public class SoldierHealth : MonoBehaviour
{
    [Min(1f)] public float maxHealth = 100f;
    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;
    public event System.Action<float, GameObject> Damaged;
    public event System.Action<GameObject> Died;
    // Optional rules supplied by the owning game mode (teams, invulnerability, etc.).
    public System.Func<GameObject, bool> DamageFilter { get; set; }

    void Awake() => ResetHealth();

    public void ResetHealth() => CurrentHealth = maxHealth;

    public bool ApplyDamage(float amount, GameObject source)
    {
        if (IsDead || amount <= 0f) return false;
        if (DamageFilter != null && !DamageFilter(source)) return false;
        float previous=CurrentHealth;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        bool died = IsDead;
        CombatEvents.ConfirmDamage(source,gameObject,previous-CurrentHealth,died);
        Damaged?.Invoke(amount, source);
        if (died) Died?.Invoke(source);
        return true;
    }
}
