using System;
using UnityEngine;

// A user preference multiplier; prefab sensitivity and optic multipliers remain authored tuning.
public static class ArenaMouseSensitivity
{
    public const string PreferenceKey = "Arena.MouseSensitivity";
    public const float Minimum = .1f, Maximum = 4f;
    static float value = float.NaN;
    public static event Action<float> Changed;

    public static float Value
    {
        get
        {
            if (float.IsNaN(value)) value = Mathf.Clamp(PlayerPrefs.GetFloat(PreferenceKey, 1f), Minimum, Maximum);
            return value;
        }
        set
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            float next = Mathf.Clamp(value, Minimum, Maximum);
            if (Mathf.Approximately(Value, next)) return;
            ArenaMouseSensitivity.value = next;
            PlayerPrefs.SetFloat(PreferenceKey, next);
            Changed?.Invoke(next);
        }
    }

    public static void Save() => PlayerPrefs.Save();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { value = float.NaN; Changed = null; }
}
