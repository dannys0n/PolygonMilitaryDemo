using UnityEngine;
using UnityEngine.UI;

// Presentation shared by infantry and vehicle HUDs. Menu effects stay independent.
public static class HudTheme
{
    public static readonly Color Accent = new Color(1f, .55f, .12f);
    public static readonly Color FullBackground = Color.black;
    public static readonly Color SectionBackground = new Color(.08f, .08f, .08f, .82f);

    public static Color SectionTint(float opacity)
    {
        var color=SectionBackground;color.a=opacity;return color;
    }

    public static void StyleBackgrounds(Transform root)
    {
        foreach (var image in root.GetComponentsInChildren<Image>(true))
            if (image.name == "Backdrop" || image.name == "Monitor Backdrop")
            {
                var color = SectionBackground;
                color.a = image.color.a;
                image.color = color;
            }
    }

    // Fixed-size corner strokes share the HUD filament, without scaling a marker.
    public static GameObject SelectionBrackets(RectTransform parent, Material filament)
    {
        var root = new GameObject("Selected brackets", typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        for (int corner = 0; corner < 4; corner++)
        {
            var anchor = new Vector2(corner % 2, corner / 2);
            for (int axis = 0; axis < 2; axis++)
            {
                var stroke = new GameObject(axis == 0 ? "Corner Horizontal" : "Corner Vertical",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                stroke.transform.SetParent(rect, false);
                stroke.material = filament; stroke.raycastTarget = false;
                stroke.color = new Color(1, 1, 1, .8f);
                var line = stroke.rectTransform;
                line.anchorMin = line.anchorMax = anchor;
                line.pivot = anchor;
                line.anchoredPosition = (anchor - Vector2.one * .5f) * 8f;
                // Filament needs a 12px halo. A short horizontal/vertical envelope
                // uses unequal lengths to select the intended orientation.
                line.sizeDelta = axis == 0 ? new Vector2(15, 12) : new Vector2(12, 15);
            }
        }
        root.SetActive(false);
        return root;
    }

    public static void StyleLines(Transform root, Material filament, Material accentGlow = null)
    {
        StyleBackgrounds(root);
        if (!filament) return;
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            string name = image.name;
            if (name == "SlimUI Edge Lines")
            {
                if (accentGlow) image.material = accentGlow;
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