using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Compact spectator presentation, separate from camera/input controls.
public class ArenaSpectatorHud : MonoBehaviour
{
    public ArenaAISpectator spectator;
    public CanvasGroup visibility;
    public GameObject scorePanel;
    public TMP_Text title, controls, redScore, blueScore, timer, mode;
    public UnityEngine.UI.Image redProgress, blueProgress;
    public Color accent = new Color(1,.686f,0);
    [Header("Alternate perspective monitor")]
    public GameObject monitor;
    public RawImage monitorImage;
    public TMP_Text monitorLabel;
    [Min(256)] public int monitorWidth = 640;
    RenderTexture monitorTexture;
    float nextRefresh;
    string accentHex;
    UnityEngine.UI.Button[] filterButtons;
    TMP_Text[] filterLabels;
    RectTransform caption,filterBar;
    Vector2 captionPosition,captionSize,captionMin,captionMax,captionPivot,titleSize,controlsSize;
    bool vehicleCaption;
    static readonly string[] FilterNames = { "ALL", "INFANTRY", "GROUND VEHICLES", "AIRBORNE VEHICLES" };
    void Awake()
    {
        accentHex = ColorUtility.ToHtmlStringRGB(accent);
        caption=(RectTransform)title.transform.parent;
        captionPosition=caption.anchoredPosition;captionSize=caption.sizeDelta;captionMin=caption.anchorMin;captionMax=caption.anchorMax;captionPivot=caption.pivot;
        titleSize=title.rectTransform.sizeDelta;controlsSize=controls.rectTransform.sizeDelta;
        BuildFilters();
        monitorTexture = new RenderTexture(monitorWidth, monitorWidth, 24, RenderTextureFormat.ARGB32)
            { name = "Spectator alternate perspective", filterMode = FilterMode.Bilinear };
        monitorTexture.Create();
        monitorImage.texture = monitorTexture;
        spectator.SetInsetTexture(monitorTexture);
    }
    void BuildFilters()
    {
        // Shared runtime HUD construction also covers scenes whose spectator canvas
        // was unpacked from the prefab, without maintaining another set of bindings.
        var bar = new GameObject("Spectator follow filters", typeof(RectTransform)).GetComponent<RectTransform>();
        filterBar=bar;
        bar.SetParent(title.transform.parent, false);
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, .5f);
        bar.anchoredPosition = new Vector2(0, 62); bar.sizeDelta = new Vector2(820, 30);
        var layout = bar.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 14; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        filterButtons = new UnityEngine.UI.Button[4]; filterLabels = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var label = Instantiate(title, bar); label.name = FilterNames[i]; label.text = FilterNames[i];
            label.fontSize = 17; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = true;
            var button = label.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = label;
            button.onClick.AddListener(() => { if(!spectator.ControlsBlocked)spectator.SetFilter((ArenaAISpectator.FollowFilter)index); });
            filterButtons[i] = button; filterLabels[i] = label;
        }
        var canvas = GetComponentInParent<Canvas>();
        if (!canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>()) canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
    }
    void LayoutCaption(bool mounted)
    {
        if(vehicleCaption==mounted)return;vehicleCaption=mounted;
        if(mounted)
        {
            caption.anchorMin=caption.anchorMax=caption.pivot=new Vector2(1,0);
            caption.anchoredPosition=new Vector2(-28,220);caption.sizeDelta=new Vector2(580,captionSize.y);
        }
        else
        {
            caption.anchorMin=captionMin;caption.anchorMax=captionMax;caption.pivot=captionPivot;
            caption.anchoredPosition=captionPosition;caption.sizeDelta=captionSize;
        }
        title.rectTransform.sizeDelta=mounted?new Vector2(560,titleSize.y):titleSize;
        controls.rectTransform.sizeDelta=mounted?new Vector2(560,controlsSize.y):controlsSize;
        filterBar.sizeDelta=new Vector2(mounted?560:820,30);
        foreach(var label in filterLabels)label.fontSize=mounted?14:17;
    }
    void Update()
    {
        bool visible = spectator.isActiveAndEnabled && spectator.IsSpectating;
        visibility.alpha = visible ? 1f : 0f;
        visibility.interactable = visibility.blocksRaycasts = visible && !spectator.ControlsBlocked;
        monitor.SetActive(visible && spectator.FollowedCharacter);
        if (!visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .1f;
        var target = spectator.FollowedCharacter;
        var occupant=target?target.GetComponent<VehicleOccupant>():null;
        bool mounted=occupant&&occupant.IsMounted;
        LayoutCaption(mounted);
        for(int i=0;i<filterButtons.Length;i++)
        {
            bool selected = (int)spectator.Filter == i;
            filterLabels[i].text = selected ? $"<color=#{accentHex}>[</color> {FilterNames[i]} <color=#{accentHex}>]</color>" : FilterNames[i];
            var colors = filterButtons[i].colors;
            colors.normalColor = selected ? Color.white : new Color(.65f,.65f,.65f);
            colors.highlightedColor = new Color(.95f,.95f,.95f); colors.pressedColor = Color.white;
            colors.selectedColor = colors.normalColor; filterButtons[i].colors = colors;
        }
        string view = !target ? "FREE CAMERA" : spectator.FirstPersonSpectating ? "FIRST PERSON" : "ORBIT";
        monitorLabel.text = spectator.FirstPersonSpectating ? "THIRD PERSON  /  C SWAP" : "FIRST PERSON  /  C SWAP";
        title.text = $"<color=#{accentHex}>{view}</color>"
            + (target ? "  /  " + target.name.Replace("<","").Replace(">","") : "  /  SPECTATOR")
            + (spectator.VehicleRespawnRemaining>0?$" / VEHICLE RESPAWN {Mathf.CeilToInt(spectator.VehicleRespawnRemaining)}s":"")
            + (spectator.WaitingForFilteredTarget ? " / NO MATCHING TARGET" : "");
        controls.text = !spectator.ShowControls ? "" : !target
            ? "RMB + WASD FLY   Q/E HEIGHT   SHIFT FAST   F FOLLOW   Z/X PLAYER   TAB SCOREBOARD   L HELP"
            : spectator.FirstPersonSpectating
                ? "Z/X PLAYER   C SWAP VIEWS   F FREE CAMERA   TAB SCOREBOARD   L HELP"
                : "RMB ORBIT   WHEEL DISTANCE   Z/X PLAYER   C SWAP VIEWS   F FREE CAMERA   TAB SCOREBOARD   L HELP";
        if(spectator.ShowControls)controls.text += "   V FILTER";
        if(spectator.ShowControls && mounted)controls.text="Z/X PLAYER   C SWAP   F FREE CAMERA   V FILTER";
        // The shared gameplay HUD already owns the score header while following.
        scorePanel.SetActive(!target && !spectator.localPlayer);
        var match = spectator.match;
        redScore.text = match.RedScore.ToString("000"); blueScore.text = match.BlueScore.ToString("000");
        timer.text = match.ClockText;
        mode.text = match.IsRunning ? $"{match.DisplayName} / {match.TeamSize(ArenaParticipant.Team.Red)} v {match.TeamSize(ArenaParticipant.Team.Blue)}" : match.Result.ToUpperInvariant();
        redProgress.fillAmount = Mathf.Clamp01((float)match.RedScore / match.ScoreLimit);
        blueProgress.fillAmount = Mathf.Clamp01((float)match.BlueScore / match.ScoreLimit);
    }

    void OnDestroy()
    {
        if (spectator) spectator.SetInsetTexture(null);
        monitorTexture.Release();
        Destroy(monitorTexture);
    }
}
