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
    [Min(0f)] public float sunIntensity = 1.35f;
    public Vector3 sunRotation = new Vector3(35f, 330f, 0f);
    // The rooms are closed boxes; with shadows on, the walls would block the sun.
    public LightShadows sunShadows = LightShadows.None;
    [Range(0f, 1f)] public float sunShadowStrength = 0.7f;

    [Header("Ambient light (the shade: soft lavender, as on the key visual's left wall)")]
    public Color ambientSky = new Color(0.9f, 0.86f, 0.88f);
    public Color ambientEquator = new Color(0.86f, 0.78f, 0.76f);
    public Color ambientGround = new Color(0.58f, 0.46f, 0.4f);

    [Header("Dudu's 2D view")]
    [Tooltip("Fills the view around the sunlit page; above 1 to match the lit paper.")]
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
        foreach (Light light in FindObjectsByType<Light>())
        {
            if (light.type != LightType.Directional) continue;
            light.color = sunColor;
            light.intensity = sunIntensity;
            light.transform.rotation = Quaternion.Euler(sunRotation);
            light.shadows = sunShadows;
            light.shadowStrength = sunShadowStrength;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
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

        foreach (DuduCameraController dudu in FindObjectsByType<DuduCameraController>(FindObjectsInactive.Include))
        {
            Camera duduCamera = dudu.GetComponent<Camera>();
            if (duduCamera != null && duduCamera.clearFlags == CameraClearFlags.SolidColor)
                duduCamera.backgroundColor = duduBackdrop;
        }

        // Every gameplay and cinematic camera shows the graded image.
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
    }
}
