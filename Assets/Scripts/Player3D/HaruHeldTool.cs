using UnityEngine;

// Shows the crayon while Haru draws and the eraser while Haru erases. Both models sit in
// the right hand of the HaruWithCrayon prefab; only one is active at a time.
[DisallowMultipleComponent]
public sealed class HaruHeldTool : MonoBehaviour
{
    [SerializeField] private string crayonName = "Crayon Grip";
    [SerializeField] private string eraserName = "Eraser Grip";

    private GameObject crayon, eraser;
    private DrawingTool tool = DrawingTool.Pen;
    private bool resolved;

    public DrawingTool Tool
    {
        get => tool;
        set
        {
            tool = value;
            Apply();
        }
    }

    public GameObject Crayon { get { Resolve(); return crayon; } }
    public GameObject Eraser { get { Resolve(); return eraser; } }

    private void Awake() => Apply();

    private void Resolve()
    {
        if (resolved) return;
        resolved = true;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (crayon == null && child.name == crayonName) crayon = child.gameObject;
            else if (eraser == null && child.name == eraserName) eraser = child.gameObject;
        }
    }

    private void Apply()
    {
        Resolve();
        // Without an eraser model, keep the crayon rather than an empty hand.
        bool erasing = tool == DrawingTool.Eraser && eraser != null;
        if (crayon != null && crayon.activeSelf == erasing) crayon.SetActive(!erasing);
        if (eraser != null && eraser.activeSelf != erasing) eraser.SetActive(erasing);
    }
}
