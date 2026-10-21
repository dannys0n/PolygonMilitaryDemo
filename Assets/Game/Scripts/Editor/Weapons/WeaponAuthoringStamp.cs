using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Incremental offline animation conversion only. Never used to populate the armory.
public static class WeaponAuthoringStamp
{
    public static string Stamp(IEnumerable<string> paths,Dictionary<string,string> memo)
    {
        var text=new System.Text.StringBuilder();
        foreach(string path in paths)
        {
            if(!memo.TryGetValue(path,out string value))
            {
                var file=new FileInfo(path);var meta=new FileInfo(path+".meta");
                value=(file.Exists?file.Length+":"+file.LastWriteTimeUtc.Ticks:"missing")+"|"+(meta.Exists?meta.LastWriteTimeUtc.Ticks.ToString():"missing");memo[path]=value;
            }
            text.Append(path).Append('|').Append(value).Append(';');
        }
        return Hash128.Compute(text.ToString()).ToString();
    }
}
