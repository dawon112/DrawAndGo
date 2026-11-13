using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The final clear's party, on the right of the screen: Haru dances in front of a wall where
// Dudu dances on a crayon line, under sweeping stage lights, with fireworks of confetti. It is
// a small set built far below the level and seen by a camera of its own, so the stage itself
// is left untouched. Each PC builds its own; nothing here is networked.
public sealed class CelebrationStage : MonoBehaviour
{
    private const int UiLayer = 5;
    private const int UnwrappedLayer = 30;
    private static readonly Vector3 Origin = new Vector3(0f, -400f, 0f);
    // Haru up front, big; Dudu small on the wall behind him.
    private const float HaruScale = 1.55f;
    // The camera nods along to the music.
    private const float BeatsPerMinute = 116f;
    private const float FieldOfView = 38f;
    private static readonly Vector3 CameraSpot = new Vector3(0.45f, 1.45f, -5.1f);
    private static readonly Vector3 CameraLook = new Vector3(0.45f, 1.25f, 1f);

    private static readonly Color[] Confetti =
    {
        new Color(1f, 0.42f, 0.55f), new Color(1f, 0.82f, 0.2f), new Color(0.35f, 0.7f, 1f),
        new Color(0.45f, 0.85f, 0.45f), new Color(1f, 0.55f, 0.2f), new Color(0.75f, 0.5f, 1f)
    };
    private static readonly Color[] BeamColors =
    {
        new Color(1f, 0.9f, 0.35f), new Color(1f, 0.6f, 0.75f), new Color(0.55f, 0.8f, 1f), new Color(1f, 0.85f, 0.45f)
    };

    private Camera view;
    private Transform haru;
    private SpriteRenderer dudu;
    private Sprite[] duduFrames;
    private float duduFps = 8f;
    private ParticleSystem confetti;
    private ParticleSystem sparks;
    private readonly List<Beam> beams = new List<Beam>();
    private float nextBurst;
    private float started;
    private Material particleMaterial;

    private sealed class Beam
    {
        public LineRenderer line;
        public Light light;
        public Vector3 top;
        public float phase, speed, reach;
        public Color color;
    }

    // screenLeft: share of the screen's width the left panel covers; the party fills the rest.
    public static CelebrationStage Build(float screenLeft)
    {
        GameObject root = new GameObject("Celebration Stage");
        root.transform.position = Origin;
        CelebrationStage stage = root.AddComponent<CelebrationStage>();
        stage.Create(screenLeft);
        return stage;
    }

    private void Create(float screenLeft)
    {
        started = Time.unscaledTime;
        CelebrationArt art = CelebrationArt.Load();
        BuildRoom();
        BuildHaru(art);
        BuildDudu(art);
        BuildLights();
        BuildConfetti();
        BuildCamera(screenLeft);
        nextBurst = Time.unscaledTime + 0.15f;
        FxSound.Play(FxClip.Chime, FxView.Listener, 0.9f, 0f);
    }

    private Vector3 At(float x, float y, float z) => transform.position + new Vector3(x, y, z);

    // ---- The set ----------------------------------------------------------------------

    private void BuildRoom()
    {
        Material floorMaterial = null, wallMaterial = null;
        foreach (Renderer renderer in FindObjectsByType<Renderer>())
        {
            Material material = renderer.sharedMaterial;
            if (material == null) continue;
            if (floorMaterial == null && material.name.StartsWith("Chevron")) floorMaterial = material;
            if (wallMaterial == null && renderer.GetComponent<DrawingSurface>() != null) wallMaterial = material;
        }
        Quad("Floor", At(0f, 0f, 1f), Quaternion.Euler(90f, 0f, 0f), new Vector3(16f, 10f, 1f), floorMaterial, new Color(0.86f, 0.6f, 0.4f));
        Quad("Wall", At(0f, 3f, 1.7f), Quaternion.identity, new Vector3(16f, 6f, 1f), wallMaterial, new Color(0.96f, 0.9f, 0.82f));
        Quad("Ceiling", At(0f, 6f, 0f), Quaternion.Euler(-90f, 0f, 0f), new Vector3(16f, 10f, 1f), wallMaterial, new Color(0.8f, 0.72f, 0.7f));
    }

    private void Quad(string name, Vector3 position, Quaternion rotation, Vector3 size, Material material, Color fallback)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, true);
        quad.transform.SetPositionAndRotation(position, rotation);
        quad.transform.localScale = size;
        Renderer renderer = quad.GetComponent<Renderer>();
        if (material != null) renderer.sharedMaterial = material;
        else
        {
            Material plain = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            plain.SetColor("_BaseColor", fallback);
            renderer.sharedMaterial = plain;
        }
    }

    // A copy of the real Haru, so it looks exactly the same, playing the dance.
    private void BuildHaru(CelebrationArt art)
    {
        Player3DMovement player = FindAnyObjectByType<Player3DMovement>(FindObjectsInactive.Include);
        Transform source = player != null ? player.transform.Find("Haru Visual") : null;
        if (source == null) return;
        GameObject copy = Instantiate(source.gameObject);
        copy.name = "Dancing Haru";
        copy.SetActive(false);
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
        Transform eraser = FindDeep(copy.transform, "Eraser Grip");
        if (eraser != null) eraser.gameObject.SetActive(false);
        copy.transform.SetParent(transform, false);
        copy.transform.localScale = source.lossyScale * HaruScale;
        copy.transform.SetPositionAndRotation(At(-0.35f, 0f, 0.1f), Quaternion.Euler(0f, 180f, 0f));
        foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
        Animator animator = copy.GetComponent<Animator>();
        if (animator != null)
        {
            animator.runtimeAnimatorController = art != null ? art.haruDance : null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
        copy.SetActive(true);
        haru = copy.transform;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // Dudu dances on the wall behind, on a blue crayon line, as on his own page.
    private void BuildDudu(CelebrationArt art)
    {
        duduFrames = art != null ? art.duduDance : null;
        if (art != null) duduFps = art.duduFramesPerSecond;
        GameObject holder = new GameObject("Dancing Dudu");
        holder.transform.SetParent(transform, false);
        const float DuduX = 1.6f, DuduScale = 0.11f, LineLeft = 0.55f, LineRight = 3.2f;
        dudu = holder.AddComponent<SpriteRenderer>();
        DuduSurfaceMovement real = FindAnyObjectByType<DuduSurfaceMovement>(FindObjectsInactive.Include);
        SpriteRenderer realSprite = real != null ? real.Visual : null;
        if (realSprite != null) dudu.sharedMaterial = realSprite.sharedMaterial;
        if (duduFrames != null && duduFrames.Length > 0) dudu.sprite = duduFrames[0];

        // His feet stand on the line: lift him by however far the art reaches below its pivot.
        float feet = dudu.sprite != null ? dudu.sprite.bounds.min.y : -1f;
        float footT = Mathf.InverseLerp(LineLeft, LineRight, DuduX);
        holder.transform.localScale = Vector3.one * DuduScale;
        holder.transform.position = At(DuduX, LineHeight(footT) - feet * DuduScale - 0.19f, 1.66f);

        LineRenderer line = FxArt.NewLine("Dudu Line", 0.07f, 1, transform);
        line.enabled = true;
        const int Points = 12;
        line.positionCount = Points;
        for (int i = 0; i < Points; i++)
        {
            float t = i / (Points - 1f);
            line.SetPosition(i, At(Mathf.Lerp(LineLeft, LineRight, t), LineHeight(t), 1.68f));
        }
        line.startColor = line.endColor = new Color(0.2f, 0.55f, 0.9f);
    }

    private static float LineHeight(float t) => 0.86f + Mathf.Sin(t * 9f) * 0.015f + t * 0.1f;

    private void BuildLights()
    {
        Vector3[] tops = { At(-3.2f, 5.9f, -0.6f), At(-1.2f, 5.9f, -1.2f), At(1.4f, 5.9f, -1.2f), At(3.4f, 5.9f, -0.6f) };
        for (int i = 0; i < tops.Length; i++)
        {
            LineRenderer line = FxArt.NewLine("Beam " + (i + 1), 0.2f, 30, transform);
            line.enabled = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.widthCurve = AnimationCurve.Linear(0f, 0.12f, 1f, 1.1f);
            GameObject lamp = new GameObject("Beam Light " + (i + 1));
            lamp.transform.SetParent(transform, false);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 12f;
            light.spotAngle = 34f;
            light.innerSpotAngle = 12f;
            light.intensity = 26f;
            light.shadows = LightShadows.None;
            beams.Add(new Beam
            {
                line = line, light = light, top = tops[i], color = BeamColors[i % BeamColors.Length],
                phase = i * 1.7f, speed = 0.8f + i * 0.17f, reach = 1.4f
            });
        }

        // A warm key light so the dancers read well.
        GameObject key = new GameObject("Key Light");
        key.transform.SetParent(transform, false);
        key.transform.SetPositionAndRotation(At(0.2f, 3.4f, -3.2f), Quaternion.Euler(28f, 0f, 0f));
        Light keyLight = key.AddComponent<Light>();
        keyLight.type = LightType.Spot;
        keyLight.color = new Color(1f, 0.9f, 0.78f);
        keyLight.range = 14f;
        keyLight.spotAngle = 70f;
        keyLight.innerSpotAngle = 40f;
        keyLight.intensity = 42f;
        keyLight.shadows = LightShadows.Soft;
    }

    private void BuildConfetti()
    {
        particleMaterial = ParticleMaterial();
        confetti = NewParticles("Confetti", 0.9f, 2.6f, true);
        sparks = NewParticles("Sparks", 0.05f, 0.9f, false);
    }

    private ParticleSystem NewParticles(string name, float gravity, float drag, bool tumble)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(transform, false);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = 1500;
        main.gravityModifier = gravity;
        main.startSpeed = 0f;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        ParticleSystem.LimitVelocityOverLifetimeModule slow = system.limitVelocityOverLifetime;
        slow.enabled = true;
        slow.drag = drag;
        slow.multiplyDragByParticleSize = false;
        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        Gradient alpha = new Gradient();
        alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        fade.color = alpha;
        if (tumble)
        {
            ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        }
        ParticleSystemRenderer look = holder.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = particleMaterial;
        look.renderMode = ParticleSystemRenderMode.Billboard;
        look.sortingOrder = 40;
        system.Play();
        return system;
    }

    private static Material ParticleMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material material = new Material(shader) { name = "Celebration Confetti" };
        Texture texture = FxArt.Get(FxShape.Crumb).texture;
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        return material;
    }

    // Its own camera, drawn on the right of the screen; the left panel covers the rest.
    private void BuildCamera(float screenLeft)
    {
        GameObject holder = new GameObject("Celebration Camera");
        holder.transform.SetParent(transform, false);
        view = holder.AddComponent<Camera>();
        PlaceCamera(0f);
        view.nearClipPlane = 0.1f;
        view.farClipPlane = 30f;
        view.rect = new Rect(screenLeft, 0f, 1f - screenLeft, 1f);
        view.depth = 50f;
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.25f, 0.2f, 0.22f);
        view.cullingMask = ~((1 << UiLayer) | (1 << UnwrappedLayer));
        UniversalAdditionalCameraData data = view.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.renderShadows = true;

        // Only this view is needed now; the level's cameras can rest.
        foreach (Camera other in FindObjectsByType<Camera>())
            if (other != view) other.enabled = false;
    }

    // ---- Every frame ------------------------------------------------------------------

    // The level may switch its own camera back on as its clear sequence winds down; keep it resting.
    private void LateUpdate()
    {
        if (view == null || Camera.allCamerasCount <= 1) return;
        foreach (Camera other in Camera.allCameras)
            if (other != view) other.enabled = false;
    }

    // 둠칫둠칫: a little dip and zoom punch on every beat, swaying side to side and tilting
    // over every two beats, all small enough to keep both dancers in frame.
    private void PlaceCamera(float age)
    {
        float beat = age * BeatsPerMinute / 60f;
        float kick = Mathf.Exp(-(beat - Mathf.Floor(beat)) * 7f);
        float sway = Mathf.Sin(beat * Mathf.PI);
        Vector3 offset = new Vector3(sway * 0.09f, -kick * 0.07f + Mathf.Abs(sway) * 0.02f, kick * 0.08f);
        Vector3 spot = At(CameraSpot.x, CameraSpot.y, CameraSpot.z) + offset;
        Vector3 look = At(CameraLook.x, CameraLook.y, CameraLook.z) + new Vector3(sway * 0.03f, -kick * 0.03f, 0f);
        Quaternion facing = Quaternion.LookRotation(look - spot, Vector3.up);
        view.transform.SetPositionAndRotation(spot, facing * Quaternion.Euler(0f, 0f, sway * 1.4f));
        view.fieldOfView = FieldOfView - kick * 1.1f;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float age = now - started;
        if (view != null) PlaceCamera(age);

        if (dudu != null && duduFrames != null && duduFrames.Length > 0)
            dudu.sprite = duduFrames[Mathf.FloorToInt(age * duduFps) % duduFrames.Length];

        foreach (Beam beam in beams)
        {
            float sweep = Mathf.Sin(age * beam.speed + beam.phase);
            Vector3 floor = At(Mathf.Lerp(-1.6f, 2.4f, (sweep + 1f) * 0.5f) + Mathf.Sin(beam.phase) * 0.6f, 0f,
                0.3f + Mathf.Cos(age * beam.speed * 0.7f + beam.phase) * 0.6f);
            Color color = Color.Lerp(beam.color, BeamColors[(beams.IndexOf(beam) + 1) % BeamColors.Length],
                Mathf.Sin(age * 0.6f + beam.phase) * 0.5f + 0.5f);
            Color start = color; start.a = 0.38f;
            Color end = color; end.a = 0.04f;
            beam.line.SetPosition(0, beam.top);
            beam.line.SetPosition(1, floor);
            beam.line.startColor = start;
            beam.line.endColor = end;
            beam.light.transform.SetPositionAndRotation(beam.top, Quaternion.LookRotation(floor - beam.top, Vector3.forward));
            beam.light.color = color;
        }

        if (now >= nextBurst)
        {
            nextBurst = now + Random.Range(0.5f, 1.1f);
            Firework(At(Random.Range(-3f, 3.8f), Random.Range(3.4f, 4.8f), Random.Range(-0.6f, 1.2f)));
        }
        // A light, steady fall of confetti between the bursts.
        if (Random.value < 0.6f) Drop(At(Random.Range(-4f, 4.5f), 5.6f, Random.Range(-1f, 1.4f)));
    }

    private void Firework(Vector3 center)
    {
        Color main = Confetti[Random.Range(0, Confetti.Length)];
        int count = Random.Range(45, 70);
        ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector3 direction = Random.onUnitSphere;
            emit.position = center;
            emit.velocity = direction * Random.Range(2.6f, 5f);
            emit.startLifetime = Random.Range(1.6f, 2.8f);
            emit.startSize = Random.Range(0.07f, 0.15f);
            emit.rotation = Random.Range(0f, 360f);
            emit.startColor = Random.value < 0.6f ? main : Confetti[Random.Range(0, Confetti.Length)];
            confetti.Emit(emit, 1);
        }
        for (int i = 0; i < 26; i++)
        {
            emit.position = center;
            emit.velocity = Random.onUnitSphere * Random.Range(3f, 6.5f);
            emit.startLifetime = Random.Range(0.35f, 0.7f);
            emit.startSize = Random.Range(0.05f, 0.09f);
            emit.startColor = Color.Lerp(main, Color.white, 0.6f);
            sparks.Emit(emit, 1);
        }
        FxSound.Play(FxClip.Pop, FxView.Listener, 0.3f, 0.2f);
    }

    private void Drop(Vector3 position)
    {
        ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.4f, 0f), 0f),
            startLifetime = 4f,
            startSize = Random.Range(0.06f, 0.12f),
            rotation = Random.Range(0f, 360f),
            startColor = Confetti[Random.Range(0, Confetti.Length)]
        };
        confetti.Emit(emit, 1);
    }

    private void OnDestroy()
    {
        if (particleMaterial != null) Destroy(particleMaterial);
    }
}
