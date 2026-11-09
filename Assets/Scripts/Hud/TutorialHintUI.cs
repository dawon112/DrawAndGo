using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The control hint at the top middle of the screen. The tutorial keeps deciding what it says
// and when it shows (text, and its state object being active); this only shows that: it
// slides in, draws keys as key caps, and ticks itself off when the hint goes away or moves on.
// Layout and colours are on Resources/UI/TutorialHint.prefab.
[DisallowMultipleComponent]
public sealed class TutorialHintUI : MonoBehaviour
{
    private static readonly Regex SizeTag = new Regex(@"</?size[^>]*>", RegexOptions.IgnoreCase);
    private static readonly Regex Token = new Regex(@"\[\s*([^\]]+?)\s*\]|★");

    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_Text textTemplate;
    [SerializeField] private KeyPromptUI keyTemplate;
    [SerializeField] private Sprite starIcon;
    [SerializeField] private RectTransform checkMark;
    [Header("Text")]
    [SerializeField, Min(8f)] private float mainSize = 30f;
    [SerializeField, Min(8f)] private float smallSize = 22f;
    [SerializeField, Min(8f)] private float mainKeySize = 44f;
    [SerializeField, Min(8f)] private float smallKeySize = 32f;
    [Header("Animation")]
    [SerializeField, Min(0f)] private float restY = -28f;
    [SerializeField, Min(0f)] private float slideDistance = 26f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.2f;
    [SerializeField, Min(0f)] private float checkSeconds = 0.3f;

    private enum Phase { Hidden, Entering, Shown, Ticked, Leaving }

    private GameObject state;
    private string wanted = string.Empty;
    private string built;
    private Phase phase = Phase.Hidden;
    private float phaseTime;
    private bool quietHide;

    // The tutorial reads and writes this just like the label it used to be.
    public string text
    {
        get => wanted;
        set => wanted = value ?? string.Empty;
    }

    public void Bind(GameObject stateObject) => state = stateObject;

    // The hint goes away without having been done (nothing to tick off).
    public void QuietNextHide() => quietHide = true;

    private void Awake()
    {
        group.alpha = 0f;
        checkMark.gameObject.SetActive(false);
        textTemplate.gameObject.SetActive(false);
        keyTemplate.gameObject.SetActive(false);
    }

    private void Update()
    {
        bool show = state != null && state.activeSelf && wanted.Length > 0;
        phaseTime += Time.unscaledDeltaTime;
        switch (phase)
        {
            case Phase.Hidden:
                if (show) Enter();
                break;
            case Phase.Entering:
            case Phase.Shown:
                if (phase == Phase.Entering && phaseTime >= fadeSeconds) { phase = Phase.Shown; phaseTime = 0f; }
                if (!show || wanted != built)
                {
                    if (quietHide && !show) { SetPhase(Phase.Leaving); }
                    else
                    {
                        SetPhase(Phase.Ticked);
                        checkMark.gameObject.SetActive(true);
                    }
                    quietHide = false;
                }
                break;
            case Phase.Ticked:
                if (phaseTime >= checkSeconds) SetPhase(Phase.Leaving);
                break;
            case Phase.Leaving:
                if (phaseTime >= fadeSeconds)
                {
                    SetPhase(Phase.Hidden);
                    checkMark.gameObject.SetActive(false);
                    group.alpha = 0f;
                }
                break;
        }
        Animate();
    }

    private void Enter()
    {
        Rebuild(wanted);
        checkMark.gameObject.SetActive(false);
        SetPhase(Phase.Entering);
    }

    private void SetPhase(Phase next)
    {
        phase = next;
        phaseTime = 0f;
    }

    private void Animate()
    {
        float visible = 0f;
        switch (phase)
        {
            case Phase.Entering: visible = Mathf.Clamp01(phaseTime / fadeSeconds); break;
            case Phase.Shown:
            case Phase.Ticked: visible = 1f; break;
            case Phase.Leaving: visible = 1f - Mathf.Clamp01(phaseTime / fadeSeconds); break;
        }
        group.alpha = visible;
        float eased = 1f - (1f - visible) * (1f - visible);
        panel.anchoredPosition = new Vector2(0f, restY + (1f - eased) * slideDistance);
        if (phase == Phase.Ticked)
        {
            float t = Mathf.Clamp01(phaseTime / Mathf.Max(0.01f, checkSeconds));
            checkMark.localScale = Vector3.one * (t < 0.4f ? Mathf.Lerp(0.3f, 1.25f, t / 0.4f) : Mathf.Lerp(1.25f, 1f, (t - 0.4f) / 0.6f));
        }
    }

    // Lines of text and key caps, from a string like "[ E ] 키를 눌러...\n<size=21>작은 줄</size>".
    private void Rebuild(string source)
    {
        built = source;
        for (int i = panel.childCount - 1; i >= 0; i--)
        {
            Transform child = panel.GetChild(i);
            if (child == textTemplate.transform || child == keyTemplate.transform || child == checkMark) continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        foreach (string rawLine in source.Split('\n'))
        {
            bool small = SizeTag.IsMatch(rawLine);
            string line = SizeTag.Replace(rawLine, string.Empty).Trim();
            if (line.Length > 0) BuildRow(line, small);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    private void BuildRow(string line, bool small)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(panel, false);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        int at = 0;
        foreach (Match match in Token.Matches(line))
        {
            AddText(row.transform, line.Substring(at, match.Index - at), small);
            if (match.Value == "★") AddStar(row.transform, small);
            else AddKey(row.transform, match.Groups[1].Value, small);
            at = match.Index + match.Length;
        }
        AddText(row.transform, line.Substring(at), small);
    }

    private void AddText(Transform row, string value, bool small)
    {
        value = value.Trim();
        if (value.Length == 0) return;
        TMP_Text label = Instantiate(textTemplate, row, false);
        label.gameObject.SetActive(true);
        label.text = value;
        label.fontSize = small ? smallSize : mainSize;
    }

    private void AddKey(Transform row, string key, bool small)
    {
        KeyPromptUI cap = Instantiate(keyTemplate, row, false);
        cap.gameObject.SetActive(true);
        cap.SetSize(small ? smallKeySize : mainKeySize, small ? smallSize * 0.85f : mainSize * 0.8f);
        cap.SetKey(key);
    }

    private void AddStar(Transform row, bool small)
    {
        GameObject star = new GameObject("Star", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        star.transform.SetParent(row, false);
        Image image = star.GetComponent<Image>();
        image.sprite = starIcon;
        image.raycastTarget = false;
        LayoutElement layout = star.GetComponent<LayoutElement>();
        layout.preferredWidth = layout.preferredHeight = small ? smallKeySize : mainKeySize;
    }
}
