using UnityEngine;

// Owns only the prototype's three coins, door and finish state.
public sealed class CornerRoomLevel : MonoBehaviour
{
    public DuduSurface[] surfaces;
    public GameObject[] coins;
    public GameObject door;
    public DuduSurfaceMovement player;
    public Player3DMovement player3D;
    public ThreeDimensionalGoal goal3D;
    public TextMesh goalLabel;
    public bool IsOpen { get; private set; }
    public bool IsDoorVisuallyOpen { get; private set; }
    public bool IsClear { get; private set; }
    public int CollectedCount { get; private set; }

    private void Awake()
    {
        ApplyRequestedLayout();
        EnsureWoodDoorVisual();
        foreach (TextMesh text in GetComponentsInChildren<TextMesh>(true)) GameFont.Apply(text);
        RemoveGuideLabels();
    }

    private void EnsureWoodDoorVisual()
    {
        if (door == null || door.transform.Find("Free Wood Door - Door 1 Brown") != null)
            return;
        GameObject prefab = Resources.Load<GameObject>("FreeWoodDoor");
        Renderer targetRenderer = door.GetComponent<Renderer>();
        if (prefab == null || targetRenderer == null)
            return;

        Bounds targetBounds = targetRenderer.bounds;
        GameObject visual = Instantiate(prefab, door.transform);
        visual.name = "Free Wood Door - Door 1 Brown";
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        Vector3 parentScale = door.transform.lossyScale;
        visual.transform.localScale = new Vector3(
            1f / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
            1f / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)),
            1f / Mathf.Max(0.001f, Mathf.Abs(parentScale.z)));

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds visualBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) visualBounds.Encapsulate(renderers[i].bounds);
            Vector3 fit = new Vector3(
                targetBounds.size.x / Mathf.Max(0.001f, visualBounds.size.x),
                targetBounds.size.y / Mathf.Max(0.001f, visualBounds.size.y),
                targetBounds.size.z / Mathf.Max(0.001f, visualBounds.size.z));
            visual.transform.localScale = Vector3.Scale(visual.transform.localScale, fit);
            visualBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) visualBounds.Encapsulate(renderers[i].bounds);
            visual.transform.position += targetBounds.center - visualBounds.center;
        }
        foreach (Collider visualCollider in visual.GetComponentsInChildren<Collider>(true))
            visualCollider.enabled = false;
        foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        UpgradeWoodMaterials(renderers);
        targetRenderer.enabled = false;
    }

    private static void UpgradeWoodMaterials(Renderer[] renderers)
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) return;
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null || source.shader == litShader) continue;
                Material wood = new Material(litShader) { name = source.name + " URP Wood" };
                if (source.HasProperty("_MainTex")) wood.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                if (source.HasProperty("_Color"))
                {
                    Color tint = source.GetColor("_Color");
                    tint.a = 1f;
                    wood.SetColor("_BaseColor", tint);
                }
                if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
                {
                    wood.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                    wood.EnableKeyword("_NORMALMAP");
                }
                if (source.HasProperty("_OcclusionMap"))
                    wood.SetTexture("_OcclusionMap", source.GetTexture("_OcclusionMap"));
                if (source.HasProperty("_Glossiness"))
                    wood.SetFloat("_Smoothness", source.GetFloat("_Glossiness") * 0.55f);
                materials[i] = wood;
            }
            renderer.materials = materials;
        }
    }

    private void RemoveGuideLabels()
    {
        foreach (TextMesh label in GetComponentsInChildren<TextMesh>(true))
        {
            string text = label.text.TrimStart();
            if (text.StartsWith("START") || text.StartsWith("SECTION"))
            {
                label.gameObject.SetActive(false);
                Destroy(label.gameObject);
            }
        }
    }

    private void Start()
    {
        // Paper and doorway wall pieces stay solid for Haru, while Dudu moves on their plane.
        Collider playerCollider = player.GetComponent<Collider>();
        foreach (DrawingSurface drawingSurface in FindObjectsByType<DrawingSurface>())
        {
            Collider paperCollider = drawingSurface.GetComponent<Collider>();
            if (paperCollider != null)
                Physics.IgnoreCollision(playerCollider, paperCollider);
        }
    }

    private void ApplyRequestedLayout()
    {
        if (coins != null)
            foreach (GameObject coin in coins)
                if (coin != null)
                    coin.transform.localScale = Vector3.one * 0.96f;

        if (surfaces == null || surfaces.Length < 4 || surfaces[3] == null)
            return;
        Transform original = transform.Find("Ground - Section 4");
        if (original == null || transform.Find("Ground - Section 4 Left") != null)
            return;

        const float gapWidth = 8f;
        DuduSurface surface = surfaces[3];
        float sectionWidth = (surface.Width - gapWidth) * 0.5f;
        float centerOffset = (gapWidth + sectionWidth) * 0.5f;
        CreateSegment("Ground - Section 4 Left", -centerOffset);
        CreateSegment("Ground - Section 4 Right", centerOffset);
        original.gameObject.SetActive(false);
        Destroy(original.gameObject);

        void CreateSegment(string name, float x)
        {
            GameObject segment = Instantiate(original.gameObject, transform);
            segment.name = name;
            segment.transform.SetPositionAndRotation(
                surface.SurfaceToWorld(new Vector2(x, -2.4f)), surface.transform.rotation);
            segment.transform.localScale = new Vector3(sectionWidth, 0.14f, 0.16f);
        }
    }

    private void Update()
    {
        if (coins == null || coins.Length != 3 || surfaces == null || surfaces.Length != 5)
            return;
        CollectedCount = 0;
        foreach (GameObject coin in coins)
            if (coin == null || !coin.activeSelf) CollectedCount++;
        if (!IsOpen && goalLabel != null)
            goalLabel.text = $"COINS {CollectedCount} / 3";
        if (!IsOpen && CollectedCount == 3)
        {
            IsOpen = true;
            if (goalLabel != null)
                goalLabel.text = "ALL COINS!\nRETURN TO 3D";
            GameViewManager viewManager = FindAnyObjectByType<GameViewManager>();
            if (viewManager != null) viewManager.PlayAllCoinsCollectedSequence();
            Debug.Log("All 3 coins collected: door unlocked.", this);
        }
    }

    public void OpenDoorForCinematic()
    {
        if (IsDoorVisuallyOpen) return;
        IsDoorVisuallyOpen = true;
        if (door != null) door.SetActive(false);
        Debug.Log("Door opened during the stage clear cinematic.", this);
    }

    public void TryCompleteThreeDimensionalGoal(Player3DMovement candidate)
    {
        if (!IsClear && IsOpen && candidate != null && candidate == player3D)
        {
            IsClear = true;
            GameViewManager viewManager = FindAnyObjectByType<GameViewManager>();
            if (viewManager != null) viewManager.HideExitInstruction();
            Debug.Log("Clear: the 3D player passed through the opened wall.", this);
        }
    }
}

