using UnityEngine;
using UnityEngine.UI;

public static class CoopDisplaySettings
{
    private const string ModeKey = "DrawAndGo.DisplayMode";
    private const string ResolutionKey = "DrawAndGo.Resolution";
    private const string BrightnessKey = "DrawAndGo.Brightness";

    private static readonly Vector2Int[] Resolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080)
    };

    private static Image brightnessOverlay;

    public static int ModeIndex => PlayerPrefs.GetInt(ModeKey, Screen.fullScreen ? 0 : 1);

    public static int ResolutionIndex
    {
        get
        {
            int saved = PlayerPrefs.GetInt(ResolutionKey, -1);
            if (saved >= 0 && saved < Resolutions.Length) return saved;

            for (int i = 0; i < Resolutions.Length; i++)
                if (Screen.width == Resolutions[i].x && Screen.height == Resolutions[i].y) return i;

            return 2;
        }
    }

    public static float Brightness
    {
        get => PlayerPrefs.GetFloat(BrightnessKey, 0.5f);
        set
        {
            float brightness = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(BrightnessKey, brightness);
            UpdateOverlay(brightness);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!Application.isEditor && (PlayerPrefs.HasKey(ModeKey) || PlayerPrefs.HasKey(ResolutionKey)))
            ApplyResolution();

        CreateOverlay();
        UpdateOverlay(Brightness);
    }

    public static void SetMode(int index)
    {
        PlayerPrefs.SetInt(ModeKey, Mathf.Clamp(index, 0, 1));
        ApplyResolution();
        PlayerPrefs.Save();
    }

    public static void SetResolution(int index)
    {
        PlayerPrefs.SetInt(ResolutionKey, Mathf.Clamp(index, 0, Resolutions.Length - 1));
        ApplyResolution();
        PlayerPrefs.Save();
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }

    private static void ApplyResolution()
    {
        if (Application.isEditor) return;

        Vector2Int size = Resolutions[ResolutionIndex];
        FullScreenMode mode = ModeIndex == 0 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(size.x, size.y, mode);
    }

    private static void CreateOverlay()
    {
        if (brightnessOverlay != null) return;

        GameObject root = new GameObject("BrightnessOverlay", typeof(Canvas));
        Object.DontDestroyOnLoad(root);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;

        GameObject imageObject = new GameObject("Tint", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(root.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        brightnessOverlay = imageObject.GetComponent<Image>();
        brightnessOverlay.raycastTarget = false;
    }

    private static void UpdateOverlay(float brightness)
    {
        if (brightnessOverlay == null) return;

        if (brightness < 0.5f)
            brightnessOverlay.color = new Color(0f, 0f, 0f, (0.5f - brightness) * 1.4f);
        else
            brightnessOverlay.color = new Color(1f, 1f, 1f, (brightness - 0.5f) * 0.5f);
    }
}
