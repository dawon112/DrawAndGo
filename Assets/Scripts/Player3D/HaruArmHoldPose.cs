using System.Collections.Generic;
using UnityEngine;

// Keeps Haru's right arm raised in front, gripping the crayon, while the Animator plays
// whole-body clips (idle, walk, jump). The stored pose covers the arm root and every
// bone under it, fingers included, so downloaded clips cannot open the crayon grip.
//
// To change the pose: open the HaruWithCrayon prefab, rotate the right-arm bones,
// then use this component's context menu "Save current right-arm pose".
//
// Runs after the Animator and before anything that copies Haru's bones (Dudu's
// silhouette view), so every view shows the held pose.
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class HaruArmHoldPose : MonoBehaviour
{
    [Tooltip("This bone and everything under it keep the saved pose.")]
    [SerializeField] private string armRootBone = "R_Upperarm";
    [Tooltip("1 = always the saved pose, 0 = the animation's own arm.")]
    [SerializeField, Range(0f, 1f)] private float weight = 1f;
    [SerializeField, HideInInspector] private string[] boneNames = new string[0];
    [SerializeField, HideInInspector] private Quaternion[] localRotations = new Quaternion[0];

    private Animator animator;
    private Transform[] bones;
    private Quaternion[] restRotations;

    public float Weight
    {
        get => weight;
        set => weight = Mathf.Clamp01(value);
    }

    public bool HasPose => boneNames != null && boneNames.Length > 0 &&
        localRotations != null && localRotations.Length == boneNames.Length;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        ResolveBones();
        // Before the Animator first runs, these are the model's authored arm rotations.
        if (bones == null) return;
        restRotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) restRotations[i] = bones[i].localRotation;
    }

    // Copies of Haru made mid-game inherit the animated arm. Code that builds its own
    // arm pose from a copy (the first-person arm) restores the authored one first.
    public void ApplyRestPose(Transform copyRoot)
    {
        if (restRotations == null || copyRoot == null) return;
        var byName = new Dictionary<string, Transform>();
        foreach (Transform child in copyRoot.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(child.name)) byName.Add(child.name, child);
        for (int i = 0; i < boneNames.Length; i++)
            if (bones[i] != null && byName.TryGetValue(boneNames[i], out Transform target))
                target.localRotation = restRotations[i];
    }

    private void ResolveBones()
    {
        if (!HasPose) return;
        var byName = new Dictionary<string, Transform>();
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(child.name)) byName.Add(child.name, child);
        bones = new Transform[boneNames.Length];
        for (int i = 0; i < boneNames.Length; i++)
            byName.TryGetValue(boneNames[i], out bones[i]);
    }

    private void LateUpdate()
    {
        // Copies of Haru (the first-person arm, the silhouette) disable their Animator
        // and receive their bones elsewhere; leave those alone.
        if (bones == null || weight <= 0f || animator == null || !animator.isActiveAndEnabled) return;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            bones[i].localRotation = weight >= 1f
                ? localRotations[i]
                : Quaternion.Slerp(bones[i].localRotation, localRotations[i], weight);
        }
    }

    [ContextMenu("Save current right-arm pose")]
    public void SaveCurrentPose()
    {
        Transform root = null;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == armRootBone) { root = child; break; }
        if (root == null)
        {
            Debug.LogError($"HaruArmHoldPose: bone '{armRootBone}' was not found.", this);
            return;
        }
        Transform[] chain = root.GetComponentsInChildren<Transform>(true);
        boneNames = new string[chain.Length];
        localRotations = new Quaternion[chain.Length];
        for (int i = 0; i < chain.Length; i++)
        {
            boneNames[i] = chain[i].name;
            localRotations[i] = chain[i].localRotation;
        }
        ResolveBones();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Debug.Log($"HaruArmHoldPose: saved {chain.Length} bones under {armRootBone}.", this);
    }
}
