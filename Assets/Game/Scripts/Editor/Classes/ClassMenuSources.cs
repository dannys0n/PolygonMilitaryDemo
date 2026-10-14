using System;
using UnityEngine;

// Editor-only authoring inputs. Nothing in the runtime menu references this asset.
public class ClassMenuSources : ScriptableObject
{
    [Serializable] public class OptionIcon {public UnityEngine.Object asset;public Sprite icon;}
    public ArenaWeaponCatalog catalog;
    public OptionIcon[] icons=Array.Empty<OptionIcon>();
}
