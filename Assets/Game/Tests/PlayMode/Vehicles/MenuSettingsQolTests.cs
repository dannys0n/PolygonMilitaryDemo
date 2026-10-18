#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MenuSettingsQolTests
{
    const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    readonly List<GameObject> roots = new List<GameObject>();
    Type preference;
    float previous;
    bool hadPreference;
    const string Key = "Arena.MouseSensitivity";
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    object Value { get => preference.GetProperty("Value", Public).GetValue(null); set => preference.GetProperty("Value", Public).SetValue(null, value); }

    [SetUp] public void Setup()
    {
        preference = T("ArenaMouseSensitivity");
        hadPreference = PlayerPrefs.HasKey(Key);
        previous = (float)Value;
    }
    [TearDown] public void Cleanup()
    {
        foreach (var root in roots) if (root) UnityEngine.Object.DestroyImmediate(root);
        roots.Clear();
        Value = previous;
        if (!hadPreference) PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    Component Binding(string name)
    {
        var root = new GameObject(name, typeof(RectTransform)); roots.Add(root); root.SetActive(false);
        var slider = root.AddComponent(T("UnityEngine.UI.Slider"));
        var binding = root.AddComponent(T("ArenaSensitivitySlider"));
        binding.GetType().GetField("slider").SetValue(binding, slider);
        root.SetActive(true);
        return slider;
    }

    [UnityTest] public IEnumerator BothMenusShareLiveSensitivityWithoutPollingAndPreservePrefabTuning()
    {
        Value = 1f;
        var main = Binding("Main settings sensitivity");
        var pause = Binding("Pause sensitivity");
        yield return null;
        main.GetType().GetProperty("value").SetValue(main, 1.75f);
        Assert.AreEqual(1.75f, (float)Value, .001f);
        Assert.AreEqual(1.75f, (float)pause.GetType().GetProperty("value").GetValue(pause), .001f);
        Assert.AreEqual(1.75f, PlayerPrefs.GetFloat(Key), .001f);
        var actor = new GameObject("Input preference test"); roots.Add(actor); actor.SetActive(false);
        var input = actor.AddComponent(T("SoldierLocalInput"));
        input.GetType().GetField("sensitivity").SetValue(input, 2f);
        Assert.AreEqual(3.5f, input.GetType().GetProperty("EffectiveSensitivity").GetValue(input));
        pause.gameObject.SetActive(false);
        Value = .5f;
        pause.gameObject.SetActive(true);
        Assert.AreEqual(.5f, pause.GetType().GetProperty("value").GetValue(pause));
        pause.GetType().GetProperty("value").SetValue(pause, 2.25f);
        Assert.AreEqual(2.25f, main.GetType().GetProperty("value").GetValue(main));
        Value = 100f; Assert.AreEqual(4f, Value);
        Value = 0f; Assert.AreEqual(.1f, Value);
        Assert.IsNull(T("ArenaSensitivitySlider").GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.IsTrue(((string)T("ArenaLobbyMenu").GetField("SandboxScenePath").GetRawConstantValue()).EndsWith("MilitaryBase_Sandbox.unity"));
    }
}
#endif
