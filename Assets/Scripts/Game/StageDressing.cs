using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Gives a stage's 3D room its light and depth, from StageLook's values, without touching the
// scene files. Ceiling lamps become pools of light that are bright at the start and the goal and
// dimmer between; lamps and spots light Haru's room only, so Dudu's page keeps its own even light.
[DisallowMultipleComponent]
public sealed class StageDressing : MonoBehaviour
{
    private const int UnwrappedLayer = 30;
    private static StageDressing active;

    private StageLook look;
    private readonly List<Light> roomLights = new List<Light>();
    private Color ambientSky, ambientEquator, ambientGround;
    private bool haruView = true;

    public static void Dress(StageLook stageLook)
    {
        GameObject holder = new GameObject("Stage Dressing");
        active = holder.AddComponent<StageDressing>();
        active.look = stageLook;
    }

    // The room's mood belongs to Haru's view. Dudu's page stays evenly lit, as paper should.
    public static void SetHaruView(bool isHaruView)
    {
        if (active != null) active.ApplyView(isHaruView);
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
    }

    private IEnumerator Start()
    {
        // Stage scripts place hazards and course pieces in their own Start.
        yield return null;
        yield return null;

        Vector3 start = FindStart();
        GameObject door = FindDoor();
        Vector3 goal = door != null ? DoorCenter(door) : start;

        SetUpLamps(start, goal, door != null);
        SetUpSunPatch();
        AddHazardSpots();
        DressFloors();
        if (door != null) door.AddComponent<GoalDoorFx>().Bind(this, IsDoorOpen);
        ApplyView(haruView);
    }

    private void ApplyView(bool isHaruView)
    {
        haruView = isHaruView;
        foreach (Light light in roomLights)
            if (light != null) light.enabled = isHaruView;
        if (look == null) return;
        float strength = isHaruView ? look.ambientStrength : 1f;
        RenderSettings.ambientSkyColor = look.ambientSky * strength;
        RenderSettings.ambientEquatorColor = look.ambientEquator * strength;
        RenderSettings.ambientGroundColor = look.ambientGround * strength;
        RenderSettings.fogDensity = isHaruView ? look.fogDensity : look.fogDensity * 0.5f;
    }

    public void RegisterRoomLight(Light light)
    {
        roomLights.Add(light);
        // The 2D view's copies of the walls sit near the world origin, away from their room.
        light.cullingMask &= ~(1 << UnwrappedLayer);
        light.enabled = haruView;
    }

    private static Vector3 FindStart()
    {
        Player3DMovement haru = FindAnyObjectByType<Player3DMovement>(FindObjectsInactive.Include);
        return haru != null ? haru.transform.position : Vector3.zero;
    }

    private static GameObject FindDoor()
    {
        TutorialRoomLevel room = FindAnyObjectByType<TutorialRoomLevel>();
        if (room != null && room.Door != null) return room.Door;
        CornerRoomLevel corner = FindAnyObjectByType<CornerRoomLevel>();
        return corner != null ? corner.door : null;
    }

    private static bool IsDoorOpen()
    {
        TutorialRoomLevel room = FindAnyObjectByType<TutorialRoomLevel>();
        if (room != null) return room.DoorOpened;
        CornerRoomLevel corner = FindAnyObjectByType<CornerRoomLevel>();
        return corner != null && corner.IsOpen;
    }

    public static Vector3 DoorCenter(GameObject door)
    {
        Bounds bounds = new Bounds(door.transform.position, Vector3.zero);
        bool found = false;
        foreach (Renderer part in door.GetComponentsInChildren<Renderer>(true))
        {
            if (!found) bounds = part.bounds; else bounds.Encapsulate(part.bounds);
            found = true;
        }
        return bounds.center;
    }

    // Each lamp lights a pool below it, brighter near where the players start and where they
    // finish; between those, every other lamp is dimmer, so the corridor reads as near, middle, far.
    private void SetUpLamps(Vector3 start, Vector3 goal, bool hasGoal)
    {
        List<Light> lamps = new List<Light>();
        foreach (Light light in FindObjectsByType<Light>())
            if (light.type == LightType.Point && light.transform.parent != null && light.transform.parent.name.StartsWith("Lamp"))
                lamps.Add(light);
        lamps.Sort((a, b) => Vector3.Distance(a.transform.position, start).CompareTo(Vector3.Distance(b.transform.position, start)));

        Sprite halo = Resources.Load<Sprite>("UI/Hud/glow");
        for (int i = 0; i < lamps.Count; i++)
        {
            Light light = lamps[i];
            Vector3 position = light.transform.position;
            float fromStart = Flat(position - start);
            float fromGoal = hasGoal ? Flat(position - goal) : float.PositiveInfinity;
            float level = look.hallwayLevel;
            if (i % 2 == 1) level *= look.alternateLampLevel;
            level = Mathf.Max(level, Mathf.Lerp(1f, 0f, Mathf.InverseLerp(look.startRoomRadius * 0.6f, look.startRoomRadius, fromStart)));
            level = Mathf.Max(level, Mathf.Lerp(look.goalLevel, 0f, Mathf.InverseLerp(look.goalRadius * 0.6f, look.goalRadius, fromGoal)));

            light.type = LightType.Spot;
            light.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            light.spotAngle = look.lampOuterAngle;
            light.innerSpotAngle = look.lampInnerAngle;
            light.range = look.lampRange;
            light.intensity = look.lampIntensity * level;
            light.color = look.lampColor;
            light.shadows = look.lampShadows;
            light.shadowStrength = 0.8f;
            light.shadowNearPlane = 0.2f;
            RegisterRoomLight(light);
            DressLampFixture(light, level, halo);
        }
    }

    // The fixture looks as lit as the pool under it, with a soft glow on the ceiling around it.
    private void DressLampFixture(Light light, float level, Sprite halo)
    {
        Renderer fixture = light.transform.parent.GetComponent<Renderer>();
        if (fixture == null) return;
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Color glow = look.lampFixtureColor * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(level));
        block.SetColor("_BaseColor", glow);
        fixture.SetPropertyBlock(block);

        if (halo == null) return;
        GameObject holder = new GameObject("Lamp Ceiling Glow");
        holder.transform.SetParent(transform, false);
        holder.AddComponent<RoomOnlyDecor>();
        // Just under the fixture: above it would be inside the ceiling.
        holder.transform.SetPositionAndRotation(fixture.bounds.center - Vector3.up * (fixture.bounds.extents.y + 0.01f), Quaternion.Euler(90f, 0f, 0f));
        holder.transform.localScale = Vector3.one * (2.6f + 1.4f * Mathf.Clamp01(level));
        SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = FxArt.SolidMaterial;
        renderer.sprite = halo;
        Color tint = look.lampColor;
        tint.a = 0.55f * Mathf.Clamp01(level);
        renderer.color = tint;
    }

    private static float Flat(Vector3 offset) => new Vector2(offset.x, offset.z).magnitude;

    private void SetUpSunPatch()
    {
        foreach (Light light in FindObjectsByType<Light>())
        {
            if (!light.name.StartsWith("Sun Patch")) continue;
            light.intensity *= look.sunPatchLevel;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;
            RegisterRoomLight(light);
        }
    }

    // A soft pool of light on the wall around each obstacle: where the eye should go, and a hint
    // that something is going on there. Close ones share a light.
    private void AddHazardSpots()
    {
        List<KeyValuePair<Vector3, DuduSurface>> places = new List<KeyValuePair<Vector3, DuduSurface>>();
        void Add(Component hazard, DuduSurface surface)
        {
            if (hazard == null || surface == null) return;
            Vector3 position = hazard.transform.position;
            foreach (KeyValuePair<Vector3, DuduSurface> place in places)
                if (place.Value == surface && Vector3.Distance(place.Key, position) < 4f) return;
            places.Add(new KeyValuePair<Vector3, DuduSurface>(position, surface));
        }
        foreach (WindZone hazard in FindObjectsByType<WindZone>()) Add(hazard, hazard.Surface);
        foreach (HotZone hazard in FindObjectsByType<HotZone>()) Add(hazard, hazard.Surface);
        foreach (NoDrawZone hazard in FindObjectsByType<NoDrawZone>()) Add(hazard, hazard.Surface);
        foreach (LineMagnet hazard in FindObjectsByType<LineMagnet>()) Add(hazard, hazard.Surface);
        foreach (MovingEraserObstacle hazard in FindObjectsByType<MovingEraserObstacle>()) Add(hazard, hazard.Surface);
        foreach (DuduHomingShooter hazard in FindObjectsByType<DuduHomingShooter>()) Add(hazard, hazard.Surface);
        foreach (DuduMovingObstacle hazard in FindObjectsByType<DuduMovingObstacle>()) Add(hazard, hazard.Surface);
        foreach (DuduStainObstacle hazard in FindObjectsByType<DuduStainObstacle>()) Add(hazard, hazard.Surface);

        foreach (KeyValuePair<Vector3, DuduSurface> place in places)
        {
            Vector3 target = place.Key;
            Vector3 normal = place.Value.Normal.normalized;
            GameObject holder = new GameObject("Hazard Spot");
            holder.transform.SetParent(transform, false);
            Vector3 from = target + normal * 3f;
            from.y = Mathf.Max(target.y + 2f, 5.4f);
            holder.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from, Vector3.up));
            Light spot = holder.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = look.hazardSpotColor;
            spot.intensity = look.hazardSpotIntensity;
            spot.range = 9f;
            spot.spotAngle = 52f;
            spot.innerSpotAngle = 18f;
            spot.shadows = LightShadows.None;
            RegisterRoomLight(spot);
        }
    }

    // Large, faint patches break up the even floor tone; crayon scribbles say a child lives here.
    private void DressFloors()
    {
        Sprite soft = Resources.Load<Sprite>("UI/Hud/glow");
        FxShape[] scribbles = { FxShape.ScribbleA, FxShape.ScribbleB, FxShape.ScribbleC };
        foreach (Renderer floor in FindObjectsByType<Renderer>())
        {
            if (floor.sharedMaterial == null || !floor.sharedMaterial.name.StartsWith("Chevron")) continue;
            Bounds bounds = floor.bounds;
            // The same patches every time on both players' screens.
            Random.State saved = Random.state;
            Random.InitState(Mathf.RoundToInt(bounds.center.x * 13f) ^ Mathf.RoundToInt(bounds.center.z * 7f));
            float area = bounds.size.x * bounds.size.z;

            int patches = Mathf.CeilToInt(area / 9f);
            for (int i = 0; i < patches && soft != null; i++)
            {
                bool dark = Random.value < 0.6f;
                Color color = dark ? new Color(0.28f, 0.14f, 0.08f, Random.Range(0.1f, 0.2f)) : new Color(1f, 0.82f, 0.58f, Random.Range(0.05f, 0.11f));
                FloorSprite(soft, bounds, color, Random.Range(3f, 7f), Random.Range(1f, 1.6f), -20 + i % 4, 0.012f);
            }
            int marks = Mathf.CeilToInt(area * look.floorScribblesPerArea);
            for (int i = 0; i < marks; i++)
            {
                Color color = look.floorScribbleColors[Random.Range(0, look.floorScribbleColors.Length)];
                color.a = Random.Range(0.18f, 0.32f);
                FloorSprite(FxArt.Get(scribbles[Random.Range(0, scribbles.Length)]), bounds, color, Random.Range(0.5f, 1.15f), 1f, -12, 0.016f);
            }
            Random.state = saved;
        }
    }

    private void FloorSprite(Sprite sprite, Bounds floor, Color color, float size, float stretch, int order, float lift)
    {
        GameObject holder = new GameObject("Floor Mark");
        holder.transform.SetParent(transform, false);
        holder.AddComponent<RoomOnlyDecor>();
        Vector3 position = new Vector3(Random.Range(floor.min.x, floor.max.x), floor.max.y + lift, Random.Range(floor.min.z, floor.max.z));
        holder.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        holder.transform.localScale = new Vector3(size * stretch, size, 1f);
        SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = FxArt.SolidMaterial;
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        renderer.color = color;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }
}
