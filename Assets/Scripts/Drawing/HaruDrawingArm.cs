using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// Builds a first-person view from Haru's real skinned right-arm mesh and rig.
[DefaultExecutionOrder(100)]
public sealed class HaruDrawingArm : MonoBehaviour
{
    private static readonly Vector2 RestShoulder = new Vector2(0.94f, 0.08f);
    private static readonly Vector2 RestHand = new Vector2(0.72f, 0.51f);

    private HaruDrawingController drawing;
    private Camera view;
    private Transform armRoot, rigRoot, upperArm, forearm, hand, crayon;
    private Quaternion upperRest, forearmRest, handRest;
    private Renderer[] worldRenderers, armRenderers;
    private readonly Dictionary<Renderer, bool> worldRendererStates = new Dictionary<Renderer, bool>();
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private float visibility;
    private Vector2 smoothedHandViewport = RestHand;
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
        foreach (Animator animator in rigRoot.GetComponentsInChildren<Animator>(true)) animator.enabled = false;

        Transform[] bones = rigRoot.GetComponentsInChildren<Transform>(true);
        upperArm = bones.FirstOrDefault(item => item.name == "R_Upperarm");
        forearm = bones.FirstOrDefault(item => item.name == "R_Forearm");
        hand = bones.FirstOrDefault(item => item.name == "R_Hand");
        if (upperArm == null || forearm == null || hand == null) return false;

        List<Renderer> visibleRenderers = ExtractRightArmMeshes();
        if (visibleRenderers.Count == 0) return false;

        upperRest = upperArm.localRotation;
        forearmRest = forearm.localRotation;
        handRest = hand.localRotation;

        Vector3 desiredShoulder = ViewportPoint(RestShoulder, 0.64f);
        Vector3 desiredHand = ViewportPoint(RestHand, 0.72f);
        float sourceLength = Vector3.Distance(upperArm.position, forearm.position) +
            Vector3.Distance(forearm.position, hand.position);
        float desiredLength = Vector3.Distance(desiredShoulder, desiredHand);
        if (sourceLength > 0.0001f) rigRoot.localScale *= desiredLength / sourceLength;
        AlignShoulder(desiredShoulder);

        Color blue = new Color(22f / 255f, 127f / 255f, 195f / 255f, 1f);
        Material crayonMaterial = CreateOverlayMaterial(null, blue, "First Person Blue Crayon");
        // Draw the hand over the crayon where they overlap, so the crayon looks
        // enclosed by the grip instead of pasted in front of every finger.
        crayonMaterial.renderQueue = 4997;
        crayon = CreateCrayon(crayonMaterial);
        Renderer crayonRenderer = crayon.GetComponent<Renderer>();
        visibleRenderers.Add(crayonRenderer);
        armRenderers = visibleRenderers.ToArray();
        foreach (Renderer renderer in armRenderers)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Sprite collectibles use sorting orders, so force view-model parts
            // to the final renderer order as well as using an overlay depth test.
            renderer.sortingOrder = renderer == crayonRenderer ? short.MaxValue - 2 : short.MaxValue - 1;
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
                if (skinned.bones[i] == upperArm || skinned.bones[i].IsChildOf(upperArm)) armBoneIndices.Add(i);
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
                    if (wa < 0.12f || wb < 0.12f || wc < 0.12f || (wa + wb + wc) / 3f < 0.4f) continue;
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
        Vector2 desiredViewport = RestHand;
        if (drawing.HasValidSurfaceAim)
        {
            Vector3 aim = view.WorldToViewportPoint(drawing.CurrentSurfaceAimPosition);
            desiredViewport += new Vector2(
                Mathf.Clamp(aim.x - 0.5f, -0.5f, 0.5f) * 0.10f,
                Mathf.Clamp(aim.y - 0.5f, -0.5f, 0.5f) * 0.10f);
        }
        desiredViewport.x = Mathf.Clamp(desiredViewport.x, 0.66f, 0.77f);
        desiredViewport.y = Mathf.Clamp(desiredViewport.y, 0.46f, 0.55f);
        smoothedHandViewport = Vector2.SmoothDamp(smoothedHandViewport, desiredViewport, ref handVelocity, 0.075f);

        float slideDown = Mathf.Lerp(0.24f, 0f, visibility);
        Vector3 shoulderTarget = ViewportPoint(RestShoulder + Vector2.down * slideDown, 0.64f);
        Vector3 handTarget = ViewportPoint(smoothedHandViewport + Vector2.down * slideDown * 0.35f, 0.72f);
        upperArm.localRotation = upperRest;
        forearm.localRotation = forearmRest;
        hand.localRotation = handRest;
        AlignShoulder(shoulderTarget);

        SolveStableArm(transform.TransformPoint(handTarget));

        // Match the requested silhouette: the crayon crosses the hand diagonally,
        // extending farther toward the lower-left and slightly behind the fingers.
        Vector2 gripViewport = smoothedHandViewport + new Vector2(-0.07f, 0.03f);
        Vector2 crayonDirection = new Vector2(-0.42f, -0.91f).normalized;
        Vector3 crayonTip = ViewportPoint(gripViewport + crayonDirection * 0.18f, 0.70f);
        Vector3 crayonBack = ViewportPoint(gripViewport - crayonDirection * 0.16f, 0.70f);
        PositionCrayon(crayonBack, crayonTip);
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

    private Transform CreateCrayon(Material material)
    {
        GameObject result = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        result.name = "Blue Crayon";
        result.transform.SetParent(armRoot, false);
        Destroy(result.GetComponent<Collider>());
        result.GetComponent<Renderer>().sharedMaterial = material;
        return result.transform;
    }

    private void PositionCrayon(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        crayon.localPosition = (from + to) * 0.5f;
        if (delta.sqrMagnitude > 0.000001f) crayon.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        crayon.localScale = new Vector3(0.017f, delta.magnitude * 0.5f, 0.017f);
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
            if (texture != null && material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
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
