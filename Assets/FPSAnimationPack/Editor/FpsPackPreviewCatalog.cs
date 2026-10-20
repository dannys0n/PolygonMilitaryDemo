using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Names and skeleton associations come from the native export manifest, not a second asset catalog.
public sealed class FpsPackPreviewCatalog : IDisposable
{
    public enum FilterMode { All, WeaponCompatible, RigCompatible, Both }
    public const string Root = "Assets/FPSAnimationPack/";
    // Consumer projects can supply compatible character models without editing the export manifest.
    public static event Func<IEnumerable<Entry>> AdditionalModels;
    [Serializable] public class Entry
    {
        public string file, source, type, skeleton, rigFile;
        [NonSerialized] public string previewSkeleton, rigLabel;
        public string EffectiveSkeleton => previewSkeleton??skeleton;
        public bool success;
        public string Path => file.StartsWith("Assets/", StringComparison.Ordinal) ? file.Replace('\\','/') : Root + file.Replace('\\','/');
        public string Name => System.IO.Path.GetFileNameWithoutExtension(file);
        public string Family
        {
            get
            {
                var parts=file.Replace('\\','/').Split('/');
                string value=(parts[0]=="Animations" || parts[0]=="Actions") && parts.Length>2 ? parts[2] : "";
                return value=="Val" ? "ASVal" : value=="Mk14" ? "Mk14EBR" : value;
            }
        }
        public bool Camera => file.Contains("/Camera/");
        public bool Character => file.Contains("/Character/") || (file.StartsWith("Animations/UE5/") || file.StartsWith("Actions/UE5/")) && !Camera;
        public bool Weapon => file.Contains("/Weapon/");
        public string Action
        {
            get
            {
                string name=Name.ToLowerInvariant().Replace("_","").Replace("-","");
                if(name.Contains("reload"))
                {
                    if(name.Contains("empty")) return "Reload / empty";
                    if(name.Contains("start")) return "Reload / start";
                    if(name.Contains("loop")) return "Reload / loop";
                    if(name.Contains("end")) return "Reload / end";
                    if(name.Contains("less")) return "Reload / tactical / less";
                    return "Reload / tactical";
                }
                if(name.Contains("pose") || name.Contains("idle")) return "Idle";
                if(name.Contains("fire") || name.Contains("firing")) return name.Contains("out") ? "Fire / empty" : "Fire";
                if(name.Contains("pump")) return name.Contains("empty") ? "Pump / empty" : "Pump";
                if(name.Contains("unequip")) return "Unequip";
                if(name.Contains("equip")) return "Equip";
                return Name;
            }
        }
    }
    [Serializable] class Manifest { public Entry[] assets; }
    [Serializable] public class PreviewIndex { public string[] playableFiles; }
    public class WeaponGroup
    {
        public string Family, Skeleton;
        public Entry[] Models;
        public override string ToString() => Family;
    }
    public Entry[] Entries;
    public WeaponGroup[] Weapons;
    readonly Dictionary<string,AnimationClip> clips = new Dictionary<string,AnimationClip>();
    HashSet<string> playable;
    public FpsPackSourceLayout Layout {get; private set;}
    public int LoadedClipCount => clips.Count;
    public int ClipLoadCount { get; private set; }
    public FpsPackPreviewCatalog()
    {
        string path=System.IO.Path.Combine(Application.dataPath,"FPSAnimationPack/export-manifest.json");
        Entries=JsonUtility.FromJson<Manifest>("{\"assets\":"+File.ReadAllText(path)+"}").assets.Where(e=>e.success).ToArray();
        Layout=FpsPackSourceLayout.Read();
        string actionManifest=Root+"action-manifest.json";
        if(File.Exists(actionManifest))Entries=Entries.Concat(JsonUtility.FromJson<Manifest>(File.ReadAllText(actionManifest)).assets).ToArray();
        string rigManifest=Root+"preview-rigs.json";
        if(File.Exists(rigManifest))foreach(var rig in JsonUtility.FromJson<FpsPackActionBuilder.Rigs>(File.ReadAllText(rigManifest)).rigs)
        {var model=Entries.FirstOrDefault(e=>e.file==rig.file);if(model!=null){model.previewSkeleton=rig.skeleton;model.rigLabel=rig.label;}}
        string indexPath=System.IO.Path.Combine(Application.dataPath,"FPSAnimationPack/preview-index.json");
        if(File.Exists(indexPath))playable=new HashSet<string>(JsonUtility.FromJson<PreviewIndex>(File.ReadAllText(indexPath)).playableFiles);
        if(AdditionalModels!=null) Entries=Entries.Concat(AdditionalModels.GetInvocationList()
            .Cast<Func<IEnumerable<Entry>>>().SelectMany(provider=>provider())).ToArray();
        Weapons=Entries.Where(e=>e.type=="AnimSequence" && e.Weapon && !string.IsNullOrEmpty(e.skeleton))
            .GroupBy(e=>e.skeleton).Select(group=>new WeaponGroup {
                Family=group.First().Family, Skeleton=group.Key,
                Models=Entries.Where(e=>e.type=="SkeletalMesh" && e.skeleton==group.Key)
                    .OrderBy(e=>DefaultMesh(group.First().Family)==e.source ? 0 : 1).ThenBy(e=>e.file.StartsWith("Meshes/Weapons/") ? 0 : 1).ThenBy(e=>e.Name).ToArray()
            }).Where(w=>w.Models.Length>0).OrderBy(w=>w.Family).ToArray();
    }
    public AnimationClip Clip(Entry entry)
    {
        if(entry==null) return null;
        if(!clips.TryGetValue(entry.Path,out var clip))
        {
            // Hold a small working set. Browsing metadata never calls this method.
            if(clips.Count>=12)clips.Remove(clips.Keys.First());
            clip=FpsPackPreview.LoadClip(entry.Path);
            ClipLoadCount++;
            clips.Add(entry.Path,clip);
        }
        return clip;
    }
    public void Dispose() {clips.Clear();}
    public bool HasAnimation(Entry entry) => entry.type=="AnimMontage" || (entry.type=="AnimSequence" && (playable==null || playable.Contains(entry.file)));
    public Entry[] Filter(WeaponGroup weapon,Entry character,FilterMode mode,bool includeShared)
    {
        bool RigMatches(Entry entry) => entry.skeleton==weapon.Skeleton ||
            (character!=null && entry.skeleton==character.EffectiveSkeleton);
        bool WeaponMatches(Entry entry) => entry.Family==weapon.Family ||
            (includeShared && new[]{"General","Jump","Grenade","Locomotion"}.Contains(entry.Family));
        return Entries.Where(HasAnimation).Where(entry=>mode==FilterMode.All ||
            (mode==FilterMode.WeaponCompatible && WeaponMatches(entry)) ||
            (mode==FilterMode.RigCompatible && RigMatches(entry)) ||
            (mode==FilterMode.Both && WeaponMatches(entry) && RigMatches(entry)))
            .OrderBy(e=>e.Action.StartsWith("Reload",StringComparison.OrdinalIgnoreCase)?0:1)
            .ThenBy(e=>e.type=="AnimMontage"?0:1).ThenBy(e=>e.file,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public Entry[] CharacterModels(WeaponGroup weapon) => Entries.Where(e=>e.type=="SkeletalMesh" &&
        Entries.Any(a=>a.type=="AnimSequence" && a.Character && a.Family==weapon.Family && a.skeleton==e.EffectiveSkeleton))
        .OrderBy(e=>e.Name=="SK_Arms_Mono" ? 0 : e.Name.Contains("Arms") ? 1 : 2).ThenBy(e=>e.Path).ToArray();
    public Entry[] Actions(WeaponGroup weapon, Entry character, bool includeShared)
    {
        return Entries.Where(e=>e.type=="AnimSequence" &&
            ((e.Family==weapon.Family && ((e.Character && character!=null && e.skeleton==character.EffectiveSkeleton) ||
                (e.Weapon && e.skeleton==weapon.Skeleton))) ||
             (includeShared && e.Character && character!=null && e.skeleton==character.EffectiveSkeleton &&
                new[]{"General","Jump","Grenade","Locomotion"}.Contains(e.Family))))
            .Where(HasAnimation).OrderBy(e=>e.Family==weapon.Family?0:1)
            .ThenBy(e=>e.Action=="Idle"?0:1).ThenBy(e=>e.Action).ThenBy(e=>e.Character?0:1).ThenBy(e=>e.Path)
            .GroupBy(e=>e.Family+"|"+e.Action).Select(g=>g.First()).ToArray();
    }
    public FpsPackSourceLayout.Action AuthoredAction(Entry raw)
    {
        if(raw==null || raw.type!="AnimSequence" || raw.Action=="Idle")return null;
        return Layout.actions.Where(a=>a.segments.Any(s=>s.source==raw.source))
            .OrderBy(a=>a.segments.Length).ThenBy(a=>a.source).FirstOrDefault();
    }
    public Entry Partner(WeaponGroup weapon, Entry action, string track)
    {
        if(track=="camera")
        {var native=Layout.actions.FirstOrDefault(a=>a.source==action.source);var camera=Entries.FirstOrDefault(e=>e.source==native?.camera);if(camera!=null)return camera;}
        var link=Layout.presets.Where(p=>p.family==weapon.Family).SelectMany(p=>p.variants).SelectMany(v=>v.links).FirstOrDefault(l=>l.character==action.source);
        if(track=="weapon" && link!=null)return Entries.FirstOrDefault(e=>e.source==link.weapon)??IdleWeapon(weapon);
        return Entries.FirstOrDefault(e=>e.type=="AnimSequence" && e.Family==(track=="camera"?action.Family:weapon.Family) && e.Action==action.Action &&
            (track=="weapon" ? e.Weapon && e.skeleton==weapon.Skeleton : e.Camera) && HasAnimation(e));
    }
    public Entry IdleWeapon(WeaponGroup weapon) => Entries.FirstOrDefault(e=>e.type=="AnimSequence" && e.Weapon && e.skeleton==weapon.Skeleton && e.Action=="Idle" && HasAnimation(e));
    public Entry MatchingCharacter(WeaponGroup weapon, Entry character, Entry action)
    {
        if(character==null)return null;
        if(action.Camera && action.type=="AnimMontage")
        {
            var paired=Layout.actions.FirstOrDefault(a=>a.camera==action.source && a.skeleton==character.EffectiveSkeleton);
            if(paired!=null)return Entries.FirstOrDefault(e=>e.source==paired.source);
        }
        var link=Layout.presets.Where(p=>p.family==weapon.Family).SelectMany(p=>p.variants).Where(v=>v.skeleton==character.EffectiveSkeleton).SelectMany(v=>v.links).FirstOrDefault(l=>l.weapon==action.source);
        if(link!=null)return Entries.FirstOrDefault(e=>e.source==link.character);
        return Entries.FirstOrDefault(e=>e.type=="AnimSequence" && e.Character && e.Family==weapon.Family && e.skeleton==character.EffectiveSkeleton && e.Action==action.Action && HasAnimation(e));
    }
    string DefaultMesh(string family)
    {
        var presets=Layout.presets.Where(p=>p.family==family).ToArray();
        return presets.FirstOrDefault(p=>p.viewmodel!=null && p.viewmodel.Split('/').Last().Substring(3)==p.id.Substring(3))?.mesh??presets.FirstOrDefault()?.mesh;
    }
    public Entry CharacterIdle(WeaponGroup weapon, Entry character)
    {
        if(character==null)return null;
        var variant=Layout.presets.Where(p=>p.family==weapon.Family).SelectMany(p=>p.variants).FirstOrDefault(v=>v.skeleton==character.EffectiveSkeleton);
        return Entries.FirstOrDefault(e=>e.source==variant?.pose)??Entries.FirstOrDefault(e=>e.type=="AnimSequence" && e.Character && e.Family==weapon.Family && e.skeleton==character.EffectiveSkeleton && e.Action=="Idle" && HasAnimation(e));
    }
}
