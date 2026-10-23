using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// Builds a first-person view from Haru's real skinned right-arm mesh and rig.
[DefaultExecutionOrder(100)]
public sealed class HaruDrawingArm : MonoBehaviour
{
    [Tooltip("Screen point (0-1) where the shoulder sits; below the screen makes the arm rise from the bottom.")]
    [SerializeField] private Vector2 restShoulder = new Vector2(0.99f, -0.26f);
    [Tooltip("Screen point (0-1) where the hand holding the crayon rests.")]
    [SerializeField] private Vector2 restHand = new Vector2(0.82f, 0.2f);
    [Tooltip("Turns the hand around the forearm (degrees) so the gripping fingers face the camera.")]
    [SerializeField, Range(-180f, 180f)] private float handRoll = 180f;
    [Tooltip("Bends the hand at the wrist, in the screen plane (degrees), so the hand's edge runs on into the forearm.")]
    [SerializeField, Range(-60f, 60f)] private float wristStraighten;

    private HaruDrawingController drawing;
    private Camera view;
    private Transform armRoot, rigRoot, upperArm, forearm, hand, crayon;
    private Renderer[] crayonRenderers;
    private float crayonSourceLength = 1f;
    private bool crayonFollowsHand;
    private HaruHeldTool heldTool;
    // Shoulder rotation the first-person grip was tuned with (upper arm and forearm: identity).
    private static readonly Quaternion FirstPersonClavicle = new Quaternion(0f, -0.07280299f, 0f, 0.99734634f);
    private Quaternion upperRest, forearmRest, handRest;
    private Transform forearmTwist01, forearmTwist02;
    private Quaternion twist01Rest, twist02Rest;
    private Renderer[] worldRenderers, armRenderers;
    private readonly Dictionary<Renderer, bool> worldRendererStates = new Dictionary<Renderer, bool>();
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private float visibility;
    private Vector2 smoothedHandViewport;
    private Vector2 handVelocity;
    private bool cameraRendering;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RenderPipelineManager.endCameraRendering += AfterCamera;
    }

    private void Start()
    {
        drawing = GetComponent<HaruDrawingController>();
        view = GetComponent<Camera>();
        Player3DMovement player = GetComponentInParent<Player3DMovement>();
        Transform haruVisual = player != null ? player.transform.Find("Haru Visual") : null;
        if (drawing == null || view == null || haruVisual == null)
        {
            Debug.LogError("First-person Haru arm requires the Haru Visual rig.", this);
            enabled = false;
            return;
        }

        worldRenderers = haruVisual.GetComponentsInChildren<Renderer>(true);
        if (!CreateRiggedFirstPersonArm(haruVisual))
        {
            Debug.LogError("Haru right-arm bones or weighted mesh were not found.", this);
            enabled = false;
            return;
        }
        view.nearClipPlane = 0.08f;
        smoothedHandViewport = restHand;
    }

    private bool CreateRiggedFirstPersonArm(Transform haruVisual)
    {
        armRoot = new GameObject("FirstPersonRightArm").transform;
        armRoot.SetParent(transform, false);

        rigRoot = Instantiate(haruVisual.gameObject, armRoot).transform;
        rigRoot.name = "Haru Rigged Right Arm";
        rigRoot.SetParent(armRoot, false);
        rigRoot.localPosition = Vector3.zero;
        rigRoot.localRotation = Quaternion.identity;
        SetLayerRecursively(rigRoot, 0);
        foreach (Animator animator in rigRoot.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        // The world Haru may already be animating; start from the model's own arm pose.
        HaruArmHoldPose heldArm = haruVisual.GetComponent<HaruArmHoldPose>();
        if (heldArm != null) heldArm.ApplyRestPose(rigRoot);

        Transform[] bones = rigRoot.GetComponentsInChildren<Transform>(true);
        upperArm = bones.FirstOrDefault(item => item.name == "R_Upperarm");
        forearm = bones.FirstOrDefault(item => item.name == "R_Forearm");
        hand = bones.FirstOrDefault(item => item.name == "R_Hand");
        if (upperArm == null || forearm == null || hand == null) return false;
        // The prefab's shoulder/upper-arm/forearm hold the world (third-person) pose and may
        // change freely; this view keeps its own so the tuned grip angle never shifts.
        // Only the hand and fingers below it come from the prefab.
        Transform clavicle = bones.FirstOrDefault(item => item.name == "R_Clavicle");
        if (clavicle != null) clavicle.localRotation = FirstPersonClavicle;
        upperArm.localRotation = Quaternion.identity;
        forearm.localRotation = Quaternion.identity;
        forearmTwist01 = bones.FirstOrDefault(item => item.name == "R_ForearmTwist01");
        forearmTwist02 = bones.FirstOrDefault(item => item.name == "R_ForearmTwist02");
        if (forearmTwist01 != null) twist01Rest = forearmTwist01.localRotation;
        if (forearmTwist02 != null) twist02Rest = forearmTwist02.localRotation;

        List<Renderer> visibleRenderers = ExtractRightArmMeshes();
        if (visibleRenderers.Count == 0) return false;

        upperRest = upperArm.localRotation;
        forearmRest = forearm.localRotation;
        handRest = hand.localRotation;

        Vector3 desiredShoulder = ViewportPoint(restShoulder, 0.64f);
        Vector3 desiredHand = ViewportPoint(restHand, 0.72f);
        float sourceLength = Vector3.Distance(upperArm.position, forearm.position) +
            Vector3.Distance(forearm.position, hand.position);
        float desiredLength = Vector3.Distance(desiredShoulder, desiredHand);
        if (sourceLength > 0.0001f) rigRoot.localScale *= desiredLength / sourceLength;
        AlignShoulder(desiredShoulder);

        Color blue = new Color(22f / 255f, 127f / 255f, 195f / 255f, 1f);
        // Show the crayon the prefab's hand already grips, so the first-person view keeps
        // the authored grip. A free-floating crayon is only a fallback for rigs without one.
        Transform gripped = FindChild(hand, "Crayon Grip");
        crayonFollowsHand = gripped != null;
        crayon = crayonFollowsHand ? PrepareGrippedCrayon(gripped, blue) : CreateCrayon(blue);
        // The eraser model sits in the same hand; HaruHeldTool swaps it in for erasing.
        heldTool = rigRoot.GetComponent<HaruHeldTool>();
        Transform eraserGrip = FindChild(hand, "Eraser Grip");
        if (eraserGrip != null)
        {
            foreach (Collider collider in eraserGrip.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            Renderer[] eraserRenderers = eraserGrip.GetComponentsInChildren<Renderer>(true);
            ApplyOverlay(eraserRenderers, Color.white, "First Person Eraser");
            crayonRenderers = crayonRenderers.Concat(eraserRenderers).ToArray();
        }
        visibleRenderers.AddRange(crayonRenderers);
        armRenderers = visibleRenderers.ToArray();
        foreach (Renderer renderer in armRenderers)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Sprite collectibles use sorting orders, so force view-model parts
            // to the final renderer order as well as using an overlay depth test.
            renderer.sortingOrder = crayonRenderers.Contains(renderer) ? short.MaxValue - 2 : short.MaxValue - 1;
            renderer.enabled = false;
        }
        return true;
    }

    private List<Renderer> ExtractRightArmMeshes()
    {
        List<Renderer> visible = new List<Renderer>();
        foreach (Renderer renderer in rigRoot.GetComponentsInChildren<Renderer>(true))
        {
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned == null || skinned.sharedMesh == null)
            {
                renderer.enabled = false;
                continue;
            }

            HashSet<int> armBoneIndices = new HashSet<int>();
            for (int i = 0; i < skinned.bones.Length; i++)
                if (skinned.bones[i] == forearm || skinned.bones[i].IsChildOf(forearm)) armBoneIndices.Add(i);
            if (armBoneIndices.Count == 0)
            {
                renderer.enabled = false;
                continue;
            }

            Mesh mesh = Instantiate(skinned.sharedMesh);
            mesh.name = skinned.sharedMesh.name + " - Right Arm Only";
            BoneWeight[] weights = mesh.boneWeights;
            bool keptAny = false;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                int[] sourceTriangles = mesh.GetTriangles(subMesh);
                List<int> armTriangles = new List<int>();
                for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
                {
                    int a = sourceTriangles[i], b = sourceTriangles[i + 1], c = sourceTriangles[i + 2];
                    float wa = GetArmWeight(weights[a], armBoneIndices);
                    float wb = GetArmWeight(weights[b], armBoneIndices);
                    float wc = GetArmWeight(weights[c], armBoneIndices);
                    // Generous, because the elbow skin is re-bound to the forearm below; a longer
                    // sleeve puts the cut edge off the bottom of the screen.
                    if (wa < 0.02f || wb < 0.02f || wc < 0.02f || (wa + wb + wc) / 3f < 0.15f) continue;
                    armTriangles.Add(a); armTriangles.Add(b); armTriangles.Add(c);
                }
                mesh.SetTriangles(armTriangles, subMesh, false);
                keptAny |= armTriangles.Count > 0;
            }
            if (!keptAny)
            {
                Destroy(mesh);
                renderer.enabled = false;
                continue;
            }

            // Elbow skin is partly bound to the upper arm, which the view never shows and
            // poses on its own; that stretched the cut end into a spike. Bind it to the
            // forearm instead so the kept piece moves as one rigid sleeve.
            int forearmIndex = System.Array.IndexOf(skinned.bones, forearm);
            if (forearmIndex >= 0)
            {
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight weight = weights[i];
                    if (!armBoneIndices.Contains(weight.boneIndex0)) weight.boneIndex0 = forearmIndex;
                    if (!armBoneIndices.Contains(weight.boneIndex1)) weight.boneIndex1 = forearmIndex;
                    if (!armBoneIndices.Contains(weight.boneIndex2)) weight.boneIndex2 = forearmIndex;
                    if (!armBoneIndices.Contains(weight.boneIndex3)) weight.boneIndex3 = forearmIndex;
                    weights[i] = weight;
                }
                mesh.boneWeights = weights;
            }

            runtimeMeshes.Add(mesh);
            skinned.sharedMesh = mesh;
            skinned.updateWhenOffscreen = true;
            Material[] materials = skinned.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = CreateOverlayMaterial(materials[i], Color.white, "First Person Haru Skin");
            skinned.sharedMaterials = materials;
            visible.Add(skinned);
        }
        return visible;
    }

    private static float GetArmWeight(BoneWeight weight, HashSet<int> armBones)
    {
        float result = 0f;
        if (armBones.Contains(weight.boneIndex0)) result += weight.weight0;
        if (armBones.Contains(weight.boneIndex1)) result += weight.weight1;
        if (armBones.Contains(weight.boneIndex2)) result += weight.weight2;
        if (armBones.Contains(weight.boneIndex3)) result += weight.weight3;
        return result;
    }

    private void LateUpdate()
    {
        if (rigRoot == null || hand == null) return;

        float blend = 1f - Mathf.Exp(-12f * Time.deltaTime);
        visibility = Mathf.Lerp(visibility, view.enabled ? 1f : 0f, blend);
        float writingMotion = drawing.IsDrawing ? Mathf.Sin(Time.time * 18f) * 0.0025f : 0f;
        Vector2 desiredViewport = restHand + new Vector2(writingMotion, -writingMotion * 0.5f);
        smoothedHandViewport = Vector2.SmoothDamp(smoothedHandViewport, desiredViewport, ref handVelocity, 0.075f);

        float slideDown = Mathf.Lerp(0.24f, 0f, visibility);
        Vector3 shoulderTarget = ViewportPoint(restShoulder + Vector2.down * slideDown, 0.64f);
        Vector3 handTarget = ViewportPoint(smoothedHandViewport + Vector2.down * slideDown * 0.35f, 0.72f);
        upperArm.localRotation = upperRest;
        forearm.localRotation = forearmRest;
        hand.localRotation = handRest;
        AlignShoulder(shoulderTarget);

        SolveStableArm(transform.TransformPoint(handTarget));
        RollHand();
        if (heldTool != null) heldTool.Tool = drawing.CurrentTool;
        // The authored grip bends the wrist; seen from here that left a notch where the
        // hand's edge met the forearm. The hand pivots on the wrist, so it stays attached.
        if (wristStraighten != 0f)
            hand.rotation = Quaternion.AngleAxis(wristStraighten, transform.forward) * hand.rotation;

        // Match the requested silhouette: the crayon crosses the hand diagonally,
        // extending farther toward the lower-left and slightly behind the fingers.
        Vector2 gripViewport = smoothedHandViewport + new Vector2(-0.015f, 0.035f);
        Vector2 crayonDirection = new Vector2(-0.86f, 0.51f).normalized;
        float visibleLength = Mathf.Lerp(0.045f, 0.16f, drawing.CrayonNormalized);
        Vector3 crayonTip = ViewportPoint(gripViewport + crayonDirection * visibleLength, 0.70f);
        Vector3 crayonBack = ViewportPoint(gripViewport - crayonDirection * 0.035f, 0.70f);
        if (!crayonFollowsHand) PositionCrayon(crayonBack, crayonTip);
    }

    // Turns the whole arm, rigidly, about the shoulder-to-hand line. No bone twists
    // against its neighbour, so the forearm keeps the model's own smooth shape (twisting
    // the forearm or wrist folded the skin into bumps). The hand lies on the axis, so it
    // stays where the IK put it; the elbow is close to the axis and barely moves.
    private void RollHand()
    {
        if (forearmTwist01 != null) forearmTwist01.localRotation = twist01Rest;
        if (forearmTwist02 != null) forearmTwist02.localRotation = twist02Rest;
        Vector3 axis = hand.position - upperArm.position;
        if (handRoll == 0f || axis.sqrMagnitude <= 0.000001f) return;
        upperArm.rotation = Quaternion.AngleAxis(handRoll, axis.normalized) * upperArm.rotation;
    }

    private void SolveStableArm(Vector3 target)
    {
        Vector3 shoulder = upperArm.position;
        float upperLength = Vector3.Distance(shoulder, forearm.position);
        float lowerLength = Vector3.Distance(forearm.position, hand.position);
        Vector3 toTarget = target - shoulder;
        float distance = Mathf.Clamp(toTarget.magnitude,
            Mathf.Abs(upperLength - lowerLength) + 0.001f,
            upperLength + lowerLength - 0.001f);
        Vector3 direction = toTarget.sqrMagnitude > 0.000001f ? toTarget.normalized : transform.forward;

        // A camera-relative pole keeps the elbow down and to the screen edge,
        // preventing the unconstrained IK from flipping through the arm.
        Vector3 pole = Vector3.ProjectOnPlane(transform.right - transform.up * 0.65f, direction).normalized;
        if (pole.sqrMagnitude < 0.001f) pole = Vector3.ProjectOnPlane(transform.forward, direction).normalized;
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) /
            (2f * distance);
        float away = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 elbowTarget = shoulder + direction * along + pole * away;

        upperArm.rotation = Quaternion.FromToRotation(
            forearm.position - shoulder, elbowTarget - shoulder) * upperArm.rotation;
        forearm.rotation = Quaternion.FromToRotation(
            hand.position - forearm.position, target - forearm.position) * forearm.rotation;
    }

    private void AlignShoulder(Vector3 desiredLocalPosition)
    {
        rigRoot.localPosition += desiredLocalPosition - transform.InverseTransformPoint(upperArm.position);
    }

    private Vector3 ViewportPoint(Vector2 viewport, float depth)
    {
        return transform.InverseTransformPoint(view.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, depth)));
    }

    private Transform CreateCrayon(Color fallbackColor)
    {
        GameObject prefab = Resources.Load<GameObject>("FirstPersonCrayon");
        GameObject result = prefab != null
            ? Instantiate(prefab, armRoot)
            : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        result.name = "Haru Crayon";
        result.transform.SetParent(armRoot, false);
        SetLayerRecursively(result.transform, 0);
        foreach (Collider collider in result.GetComponentsInChildren<Collider>(true)) Destroy(collider);

        crayonRenderers = result.GetComponentsInChildren<Renderer>(true);
        ApplyCrayonOverlay(fallbackColor);
        if (crayonRenderers.Length > 0)
        {
            Bounds bounds = crayonRenderers[0].bounds;
            for (int i = 1; i < crayonRenderers.Length; i++) bounds.Encapsulate(crayonRenderers[i].bounds);
            crayonSourceLength = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
        }
        return result.transform;
    }

    private Transform PrepareGrippedCrayon(Transform gripped, Color fallbackColor)
    {
        foreach (Collider collider in gripped.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        crayonRenderers = gripped.GetComponentsInChildren<Renderer>(true);
        ApplyCrayonOverlay(fallbackColor);
        return gripped;
    }

    private void ApplyCrayonOverlay(Color fallbackColor) =>
        ApplyOverlay(crayonRenderers, fallbackColor, "First Person Crayon");

    private void ApplyOverlay(Renderer[] renderers, Color fallbackColor, string materialName)
    {
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = CreateOverlayMaterial(materials[i], fallbackColor, materialName);
                materials[i].renderQueue = 4997;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == childName) return child;
        return null;
    }

    private void PositionCrayon(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        crayon.localPosition = (from + to) * 0.5f;
        if (delta.sqrMagnitude > 0.000001f) crayon.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        crayon.localScale = Vector3.one * (delta.magnitude / crayonSourceLength);
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) SetLayerRecursively(child, layer);
    }

    private Material CreateOverlayMaterial(Material source, Color fallbackColor, string materialName)
    {
        Shader shader = Shader.Find("DrawAndGo/FirstPersonOverlay");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        Material material = new Material(shader) { name = materialName, renderQueue = 4998 };
        Color color = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : fallbackColor;
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (source != null)
        {
            Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
            if (texture != null && material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                // Keep the source's tiling/offset, or labels and stripes land in the wrong place.
                string scaleProperty = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                if (source.HasProperty(scaleProperty))
                {
                    material.SetTextureScale("_BaseMap", source.GetTextureScale(scaleProperty));
                    material.SetTextureOffset("_BaseMap", source.GetTextureOffset(scaleProperty));
                }
            }
        }
        runtimeMaterials.Add(material);
        return material;
    }

    private void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != view || worldRenderers == null) return;
        cameraRendering = true;
        worldRendererStates.Clear();
        foreach (Renderer renderer in worldRenderers)
        {
            if (renderer == null) continue;
            worldRendererStates[renderer] = renderer.enabled;
            renderer.enabled = false;
        }
        if (armRenderers != null)
            foreach (Renderer renderer in armRenderers)
                if (renderer != null) renderer.enabled = visibility > 0.02f;
    }

    private void AfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != view || !cameraRendering) return;
        foreach (KeyValuePair<Renderer, bool> state in worldRendererStates)
            if (state.Key != null) state.Key.enabled = state.Value;
        if (armRenderers != null)
            foreach (Renderer renderer in armRenderers)
                if (renderer != null) renderer.enabled = false;
        cameraRendering = false;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.endCameraRendering -= AfterCamera;
        if (cameraRendering) AfterCamera(default, view);
    }

    private void OnDestroy()
    {
        foreach (Material material in runtimeMaterials) if (material != null) Destroy(material);
        foreach (Mesh mesh in runtimeMeshes) if (mesh != null) Destroy(mesh);
        if (armRoot != null) Destroy(armRoot.gameObject);
    }
}
