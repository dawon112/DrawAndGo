using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class ButtonTextHoverOutline : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private static readonly Color32 HoverTextColor = new Color32(0x30, 0x94, 0xD6, 0xFF);

    [SerializeField] private TMP_Text label;
    [SerializeField, Range(0f, 1f)] private float hoverWidth = 0.3f;

    private Button button;
    private Material originalMaterial;
    private Material hoverMaterial;
    private Color originalTextColor;
    private bool highlighted;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
        if (label == null || label.fontSharedMaterial == null)
            return;

        originalMaterial = label.fontSharedMaterial;
        originalTextColor = label.color;
        // Never modify the shared font preset used by other UI labels.
        hoverMaterial = new Material(originalMaterial);
        hoverMaterial.name = originalMaterial.name + " (Hover)";
        hoverMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.white);
        hoverMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, hoverWidth);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isActiveAndEnabled && button != null && button.IsInteractable())
            SetHighlighted(true);
    }

    public void OnPointerExit(PointerEventData eventData) => SetHighlighted(false);

    private void Update()
    {
        if (highlighted && (button == null || !button.isActiveAndEnabled || !button.IsInteractable()))
            SetHighlighted(false);
    }

    private void OnDisable() => SetHighlighted(false);

    private void SetHighlighted(bool value)
    {
        if (highlighted == value || label == null || hoverMaterial == null)
            return;
        highlighted = value;
        label.fontSharedMaterial = value ? hoverMaterial : originalMaterial;
        label.color = value ? HoverTextColor : originalTextColor;
        label.UpdateMeshPadding();
        label.SetVerticesDirty();
    }

    private void OnDestroy()
    {
        SetHighlighted(false);
        if (hoverMaterial != null)
            Destroy(hoverMaterial);
    }
}
