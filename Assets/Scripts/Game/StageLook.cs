using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// The game's art direction (warm late-afternoon sun through a hallway, as in the key
// visual), applied to every stage when it loads. Designers tune it in
// Resources/StageLook; the scenes themselves are not edited, so team members can
// work on stage scenes without touching lighting.
[CreateAssetMenu(menuName = "Follow My Line/Stage Look", fileName = "StageLook")]
public sealed class StageLook : ScriptableObject
{
    [Header("Post-processing")]
    public VolumeProfile postProfile;

    [Header("Sun (the scene's directional light)")]
    // Warmth comes from the light, not a strong grade, so unlit crayon lines and
    // Dudu's drawings keep their true colours.
    public Color sunColor = new Color(1f, 0.83f, 0.64f);
    [Min(0f)] public float sunIntensity = 3.2f;
    public Vector3 sunRotation = new Vector3(30f, 70f, 0f);
    // Used only in stages with windows (RoomOnlyDecor.letsSunIn): elsewhere the rooms
    // are closed boxes and the walls would block the sun entirely.
    public LightShadows sunShadows = LightShadows.Soft;
    [Range(0f, 1f)] public float sunShadowStrength = 0.9f;

    [Header("Ambient light (the shade: soft lavender, as on the key visual's left wall)")]
    public Color ambientSky = new Color(0.72f, 0.65f, 0.7f);
    public Color ambientEquator = new Color(0.7f, 0.6f, 0.58f);
    public Color ambientGround = new Color(0.44f, 0.34f, 0.3f);
    [Tooltip("Ambient in Haru's room, as a share of the colours above. Lower means deeper shade between the lights. Dudu's page always uses the full colours.")]
    [Range(0f, 1.5f)] public float ambientStrength = 0.72f;

    [Header("Ceiling lamps (pools of light)")]
    public Color lampColor = new Color(1f, 0.8f, 0.58f);
    [Min(0f)] public float lampIntensity = 52f;
    [Min(0.1f)] public float lampRange = 9f;
    [Range(1f, 179f)] public float lampOuterAngle = 132f;
    [Range(0f, 179f)] public float lampInnerAngle = 60f;
    public LightShadows lampShadows = LightShadows.Soft;
    [Tooltip("How the fixture itself looks; above 1 so it catches a little bloom.")]
    [ColorUsage(false, true)] public Color lampFixtureColor = new Color(2.2f, 1.85f, 1.35f);

    [Header("Light by area")]
    [Tooltip("Lamps between the start and the goal, as a share of full brightness.")]
    [Range(0f, 1f)] public float hallwayLevel = 0.62f;
    [Tooltip("Every other hallway lamp is this much dimmer, for near / middle / far.")]
    [Range(0f, 1f)] public float alternateLampLevel = 0.72f;
    [Min(0f)] public float startRoomRadius = 15f;
    [Tooltip("The lamps nearest the door; above 1 makes the goal the brightest place.")]
    [Min(0f)] public float goalLevel = 1.25f;
    [Min(0f)] public float goalRadius = 13f;
    [Tooltip("Share of its own brightness for the scenes' Sun Patch spot.")]
    [Range(0f, 2f)] public float sunPatchLevel = 0.8f;
    public Color hazardSpotColor = new Color(1f, 0.86f, 0.66f);
    [Min(0f)] public float hazardSpotIntensity = 9f;

    [Header("Floor")]
    [Min(0f)] public float floorScribblesPerArea = 0.05f;
    public Color[] floorScribbleColors =
    {
        new Color(0.2f, 0.5f, 0.85f), new Color(0.9f, 0.4f, 0.25f), new Color(0.95f, 0.75f, 0.2f), new Color(0.3f, 0.65f, 0.4f)
    };

    [Header("Dudu's 2D view")]
    [Tooltip("Fills the view around the sunlit page; above 1 to match the lit paper. Read by DuduCameraController.")]
    [ColorUsage(false, true)] public Color duduBackdrop = new Color(1.12f, 1f, 0.88f);

    [Header("Air (soft warm haze for depth)")]
    public bool fog = true;
    public Color fogColor = new Color(0.98f, 0.82f, 0.64f);
    [Min(0f)] public float fogDensity = 0.012f;

    private static StageLook cached;

    public static StageLook Load()
    {
        if (cached == null) cached = Resources.Load<StageLook>("StageLook");
        return cached;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyTo(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyTo(scene);

    private static void ApplyTo(Scene scene)
    {
        StageLook look = Load();
        if (look == null || !StageSequence.Load().IsStage(scene.name)) return;
        look.Apply();
    }

    public void Apply()
    {
        bool hasWindows = false;
        foreach (RoomOnlyDecor decor in FindObjectsByType<RoomOnlyDecor>())
            hasWindows |= decor.letsSunIn;
        foreach (Light light in FindObjectsByType<Light>())
        {
            if (light.type != LightType.Directional) continue;
            light.color = sunColor;
            light.intensity = sunIntensity;
            light.transform.rotation = Quaternion.Euler(sunRotation);
            light.shadows = hasWindows ? sunShadows : LightShadows.None;
            light.shadowStrength = sunShadowStrength;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky * ambientStrength;
        RenderSettings.ambientEquatorColor = ambientEquator * ambientStrength;
        RenderSettings.ambientGroundColor = ambientGround * ambientStrength;
        RenderSettings.fog = fog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;

        if (postProfile != null)
        {
            var holder = new GameObject("Stage Look Volume");
            Volume volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = postProfile;
        }

        // Every gameplay and cinematic camera shows the graded image.
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        StageDressing.Dress(this);
    }
}
