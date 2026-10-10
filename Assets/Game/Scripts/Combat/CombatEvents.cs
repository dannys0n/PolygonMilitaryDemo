using System;
using UnityEngine;

// Damage recipients publish accepted damage independently of the weapon or game HUD.
public static class CombatEvents
{
    public readonly struct ConfirmedDamage
    {
        public readonly GameObject attacker, target;
        public readonly float amount;
        public readonly bool killed;
        public ConfirmedDamage(GameObject attacker,GameObject target,float amount,bool killed)
        {
            this.attacker=attacker;this.target=target;this.amount=amount;this.killed=killed;
        }
    }
    public static event Action<ConfirmedDamage> DamageConfirmed;
    public static void ConfirmDamage(GameObject attacker,GameObject target,float amount,bool killed) =>
        DamageConfirmed?.Invoke(new ConfirmedDamage(attacker,target,amount,killed));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => DamageConfirmed=null;
}