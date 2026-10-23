using UnityEngine;

// Keeps Haru's right arm exactly as posed in the HaruWithCrayon prefab (shoulder, arm,
// fingers and crayon) while the Animator plays whole-body clips (idle, walk, jump).
// To change the held pose, rotate the right-arm bones in the prefab and save it;
// there is nothing else to update.
//
// Runs after the Animator and before anything that copies Haru's bones (Dudu's
// silhouette view), so every view shows the held pose.
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class HaruArmHoldPose : MonoBehaviour
{
    [Tooltip("This bone and everything under it keep the prefab's pose.")]
    [SerializeField] private string armRootBone = "R_Clavicle";
    [Tooltip("1 = always the prefab's arm pose, 0 = the animation's own arm.")]
    [SerializeField, Range(0f, 1f)] private float weight = 1f;

    private Animator animator;
    private Transform[] bones;
    private Quaternion[] heldRotations;

    public float Weight
    {
        get => weight;
        set => weight = Mathf.Clamp01(value);
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        Transform root = FindBone(transform, armRootBone);
        if (root == null)
        {
            Debug.LogError($"HaruArmHoldPose: bone '{armRootBone}' was not found.", this);
            enabled = false;
            return;
        }
        // Before the Animator first runs, these are the prefab's authored rotations.
        bones = root.GetComponentsInChildren<Transform>(true);
        heldRotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++) heldRotations[i] = bones[i].localRotation;
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
                ? heldRotations[i]
                : Quaternion.Slerp(bones[i].localRotation, heldRotations[i], weight);
        }
    }

    // Copies of Haru made mid-game inherit the animated arm. Code that builds its own
    // arm pose from a copy (the first-person arm) restores the prefab's pose first.
    public void ApplyRestPose(Transform copyRoot)
    {
        if (bones == null || copyRoot == null) return;
        Transform copyArm = FindBone(copyRoot, armRootBone);
        if (copyArm == null) return;
        Transform[] copyBones = copyArm.GetComponentsInChildren<Transform>(true);
        // Instantiate preserves hierarchy order, so the chains line up one to one.
        int count = Mathf.Min(copyBones.Length, bones.Length);
        for (int i = 0; i < count; i++)
            if (copyBones[i].name == bones[i].name) copyBones[i].localRotation = heldRotations[i];
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == boneName) return child;
        return null;
    }
}
