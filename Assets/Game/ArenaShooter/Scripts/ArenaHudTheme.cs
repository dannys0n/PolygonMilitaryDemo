using UnityEngine;
using UnityEngine.UI;

// Presentation shared by infantry and vehicle HUDs. Menu effects stay independent.
public static class ArenaHudTheme
{
    public static void StyleLines(Transform root, Material filament, Material glow = null)
    {
        if (!filament) return;
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            string name = image.name;
            if (name == "SlimUI Edge Lines")
            {
                if (glow) image.material = glow;
                continue;
            }
            if (name != "Corner Horizontal" && name != "Corner Vertical"
                && name != "Filament" && name != "Top Filament" && name != "Capture Filament") continue;
            var rect = image.rectTransform;
            bool horizontal = rect.rect.width >= rect.rect.height;
            // Make room for the halo; shader core stays a constant screen-pixel width.
            rect.SetSizeWithCurrentAnchors(horizontal ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal, 12f);
            image.material = filament;
            image.raycastTarget = false;
        }
    }
}