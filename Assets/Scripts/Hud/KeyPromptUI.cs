using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One key cap: [ W ], [ Enter ]... The look lives in Resources/UI/KeyPrompt.prefab, and this
// only changes its label and widens it to fit.
[DisallowMultipleComponent]
public sealed class KeyPromptUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private LayoutElement layout;
    [SerializeField, Min(0f)] private float sidePadding = 14f;

    public void SetKey(string key)
    {
        label.text = key;
        float height = ((RectTransform)transform).rect.height;
        if (layout != null && layout.preferredHeight > 0f) height = layout.preferredHeight;
        float width = Mathf.Max(height, label.GetPreferredValues(key).x + sidePadding * 2f);
        RectTransform rect = (RectTransform)transform;
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        if (layout != null) layout.preferredWidth = width;
    }

    public void SetSize(float height, float fontSize)
    {
        RectTransform rect = (RectTransform)transform;
        rect.sizeDelta = new Vector2(height, height);
        if (layout != null)
        {
            layout.preferredHeight = height;
            layout.preferredWidth = height;
        }
        label.fontSize = fontSize;
    }
}
