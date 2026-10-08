using UnityEngine;

public class SoldierHealth : MonoBehaviour
{
    [Min(1f)] public float maxHealth = 100f;
    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;
    public event System.Action<float, GameObject> Damaged;
    public event System.Action<GameObject> Died;

    void Awake() => ResetHealth();

    public void ResetHealth() => CurrentHealth = maxHealth;

    public bool ApplyDamage(float amount, GameObject source)
    {
        if (IsDead || amount <= 0f) return false;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        bool died = IsDead;
        Damaged?.Invoke(amount, source);
        if (died) Died?.Invoke(source);
        return true;
    }
}
