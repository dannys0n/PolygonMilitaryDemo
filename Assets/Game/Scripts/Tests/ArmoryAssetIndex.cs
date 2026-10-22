using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

// A fresh, name-only snapshot for an editor test scene. No generated assets or rig references.
public sealed class ArmoryAssetIndex
{
    public sealed class Weapon
    {
        public string id, title, definitionPath, firstPersonPath, thirdPersonPath;
    }
    public Weapon[] weapons;
    public string[] sightPaths, attachmentPaths;

    public WeaponCatalog.Entry LoadWeapon(string id)
    {
#if UNITY_EDITOR
        var item = weapons.FirstOrDefault(w => w.id == id)
            ?? throw new ArgumentException("Unknown armory weapon: " + id);
        var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<SoldierWeaponDefinition>(item.definitionPath);
        var first = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(item.firstPersonPath);
        var third = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(item.thirdPersonPath);
        if (!definition || !first || !third) throw new InvalidOperationException("Incomplete armory weapon: " + item.title);
        return new WeaponCatalog.Entry { weapon = definition,
            firstPersonPrefab = first.GetComponent<SoldierWeaponView>(),
            thirdPersonPrefab = third.GetComponent<SoldierWeaponView>(), basePrefab = third };
#else
        throw new InvalidOperationException("Armory asset inspection requires the Unity Editor.");
#endif
    }

    public static ArmoryAssetIndex Read(string catalogPath, bool polishedOnly)
    {
#if UNITY_EDITOR
        // Resolve paths on the main thread. Workers only read small text assets, never Unity objects.
        string text = File.ReadAllText(catalogPath);
        var entries = Regex.Split(text, @"(?m)^  - weapon: ").Skip(1)
            .Where(row => !polishedOnly || Regex.IsMatch(row, @"(?m)^    polished: 1\r?$" )).ToArray();
        var rows = entries.Select(row => new Weapon {
                definitionPath = PathForGuid(Guid(row.Split('\n')[0])),
                firstPersonPath = PathForGuid(Reference(row, "firstPersonPrefab")),
                thirdPersonPath = PathForGuid(Reference(row, "thirdPersonPrefab"))
            }).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("No weapons in armory source: " + catalogPath);
        var sights = PathsInSection(string.Join("\n", entries), "sights");
        var attachments = PathsInSection(string.Join("\n", entries), "attachments");
        Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) }, row => {
            string definition = File.ReadAllText(row.definitionPath);
            row.id = Scalar(definition, "catalogId");
            row.title = Scalar(definition, "displayName");
            if (string.IsNullOrEmpty(row.id) || string.IsNullOrEmpty(row.title))
                throw new InvalidDataException("Missing weapon name/id: " + row.definitionPath);
        });
        if (rows.Select(r => r.id).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Duplicate weapon ids in " + catalogPath);
        return new ArmoryAssetIndex { weapons = rows.OrderBy(r => r.title, StringComparer.OrdinalIgnoreCase).ToArray(),
            sightPaths = sights, attachmentPaths = attachments };
#else
        throw new InvalidOperationException("Armory asset inspection requires the Unity Editor.");
#endif
    }
#if UNITY_EDITOR
    static string Guid(string line) => Regex.Match(line, @"guid: ([a-f0-9]{32})").Groups[1].Value;
    static string Reference(string row, string field) => Guid(Regex.Match(row, @"(?m)^    " + field + @": (.*)$").Groups[1].Value);
    static string PathForGuid(string guid)
    {
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) throw new InvalidDataException("Missing armory asset GUID: " + guid);
        return path;
    }
    static string[] PathsInSection(string text, string field) => Regex.Matches(text,
        @"(?m)^    " + field + @":\r?\n((?:    - .*\r?\n)*)")
        .Cast<Match>().SelectMany(m => Regex.Matches(m.Groups[1].Value, @"guid: ([a-f0-9]{32})").Cast<Match>())
        .Select(m => m.Groups[1].Value).Distinct().Select(PathForGuid).ToArray();
    static string Scalar(string text, string field)
    {
        string value = Regex.Match(text, @"(?m)^  " + field + @": (.*)$").Groups[1].Value.Trim();
        if (value.StartsWith("\"") && value.EndsWith("\"")) return Regex.Unescape(value.Substring(1, value.Length - 2));
        if (value.StartsWith("'") && value.EndsWith("'")) return value.Substring(1, value.Length - 2).Replace("''", "'");
        return value;
    }
#endif
}
