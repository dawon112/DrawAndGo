using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The ranking screens, in the title's sketchbook style: name entry, waiting for the friend,
// the result, the title's ranking view and a notice. Layout and art live in
// Resources/UI/ExhibitionRanking.prefab (Tools > Draw And Go to rebuild); this fills it in.
[DisallowMultipleComponent]
public sealed class ExhibitionRankingUI : MonoBehaviour
{
    public enum Role { Haru, Dudu }

    private static readonly Color HaruColor = new Color(0.91f, 0.5f, 0.25f);
    private static readonly Color DuduColor = new Color(0.25f, 0.56f, 0.83f);
    private static readonly Color Ink = new Color(0.82f, 0.388f, 0.247f);
    private const string HaruHex = "#E07A3A";
    private const string DuduHex = "#3F8FD4";

    [Serializable]
    public sealed class Row
    {
        public GameObject root;
        public Image highlight;
        public TMP_Text rank;
        public Image star;
        public TMP_Text names;
        public TMP_Text time;
        public GameObject newMark;
    }

    [Header("Backdrop")]
    [SerializeField] private Image backdrop;

    [Header("Name entry")]
    [SerializeField] private RectTransform namePanel;
    [SerializeField] private Image nameIcon;
    [SerializeField] private TMP_Text nameRoleLabel;
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_Text nameGuide;
    [SerializeField] private Button confirmButton;
    [SerializeField] private GameObject submittedMark;

    [Header("Waiting")]
    [SerializeField] private RectTransform waitPanel;
    [SerializeField] private TMP_Text haruStatus;
    [SerializeField] private TMP_Text duduStatus;
    [SerializeField] private Image haruCheck;
    [SerializeField] private Image duduCheck;
    [SerializeField] private RectTransform[] dots;

    [Header("Result")]
    [SerializeField] private RectTransform resultPanel;
    [SerializeField] private TMP_Text resultTitle;
    [SerializeField] private Row[] rows;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private GameObject ourRecordBox;
    [SerializeField] private TMP_Text ourRank;
    [SerializeField] private TMP_Text ourNames;
    [SerializeField] private TMP_Text ourTime;
    [SerializeField] private TMP_Text ourMessage;
    [SerializeField] private Button leaveButton;
    [SerializeField] private TMP_Text leaveLabel;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text resultNote;

    [Header("Notice")]
    [SerializeField] private RectTransform noticePanel;
    [SerializeField] private TMP_Text noticeText;
    [SerializeField] private Button noticeButton;
    [SerializeField] private TMP_Text noticeButtonLabel;

    [Header("Celebration (left of the final clear screen)")]
    [SerializeField] private RectTransform celebrationPanel;
    [SerializeField] private TMP_Text celebrationTime;
    [SerializeField] private Button rankingButton;
    [SerializeField] private TMP_Text rankingButtonLabel;
    [SerializeField] private Button titleButton;
    [SerializeField] private Image screenFade;
    [SerializeField, Min(0.05f)] private float fadeSeconds = 0.7f;
    [Tooltip("Share of the screen's width the left panel covers; the party is drawn beside it.")]
    [SerializeField, Range(0.1f, 0.6f)] private float celebrationWidth = 0.32f;

    [Header("Art")]
    [SerializeField] private Sprite haruIcon;
    [SerializeField] private Sprite duduIcon;

    private Action<string> submit;
    private Action leave;
    private Action close;
    private Action noticeAction;
    private Action onRanking;
    private Action onTitle;
    private float shownAt;
    private float fadeStarted = -1f;

    public float CelebrationWidth => celebrationWidth;
    private bool Celebrating => celebrationPanel != null && celebrationPanel.gameObject.activeSelf;

    public bool IsTyping => namePanel.gameObject.activeInHierarchy && nameInput.isFocused;

    private void Awake()
    {
        nameInput.characterLimit = ExhibitionRanking.MaxNameLength;
        nameInput.lineType = TMP_InputField.LineType.SingleLine;
        nameInput.onValidateInput = (text, index, added) => ExhibitionRanking.IsAllowed(added) ? added : '\0';
        nameInput.onSubmit.AddListener(_ => OnConfirm());
        nameInput.onValueChanged.AddListener(_ => RefreshGuide());
        confirmButton.onClick.AddListener(OnConfirm);
        leaveButton.onClick.AddListener(() => leave?.Invoke());
        closeButton.onClick.AddListener(() => close?.Invoke());
        noticeButton.onClick.AddListener(() => noticeAction?.Invoke());
        rankingButton.onClick.AddListener(() => onRanking?.Invoke());
        titleButton.onClick.AddListener(() => onTitle?.Invoke());
        celebrationPanel.gameObject.SetActive(false);
        screenFade.gameObject.SetActive(false);
        HideAll();
    }

    private void HideAll()
    {
        namePanel.gameObject.SetActive(false);
        waitPanel.gameObject.SetActive(false);
        resultPanel.gameObject.SetActive(false);
        noticePanel.gameObject.SetActive(false);
    }

    private void Show(RectTransform panel, bool dropIn)
    {
        if (panel.gameObject.activeSelf) return;
        HideAll();
        PopupDropInAnimation drop = panel.GetComponent<PopupDropInAnimation>();
        if (drop != null && !dropIn) drop.SkipNextEntrance();
        panel.gameObject.SetActive(true);
        shownAt = Time.unscaledTime;
        // Over the party a popup gets a dim veil behind it.
        if (Celebrating)
        {
            backdrop.enabled = true;
            SetBackdrop(false);
        }
    }

    // Back to the party screen.
    public void ClosePopups()
    {
        HideAll();
        if (Celebrating) backdrop.enabled = false;
    }

    // ---- Celebration ------------------------------------------------------------------

    public void ShowCelebration(double seconds, Action ranking, Action title)
    {
        onRanking = ranking;
        onTitle = title;
        celebrationTime.text = ExhibitionRanking.FormatTime(seconds, false);
        RectTransform panel = celebrationPanel;
        panel.anchorMax = new Vector2(celebrationWidth, 1f);
        panel.gameObject.SetActive(true);
        backdrop.enabled = false;
        HideAll();
        // Out of the paper the clear faded to.
        screenFade.gameObject.SetActive(true);
        screenFade.color = new Color(0.98f, 0.94f, 0.85f, 1f);
        fadeStarted = Time.unscaledTime;
    }

    public void SetRankingButton(string label, bool interactable)
    {
        rankingButtonLabel.text = label;
        rankingButton.interactable = interactable;
    }

    // Paper all over (after a clear) or a dim veil over the title.
    public void SetBackdrop(bool paper)
    {
        backdrop.color = paper ? new Color(0.98f, 0.94f, 0.85f, 1f) : new Color(0.12f, 0.08f, 0.06f, 0.55f);
    }

    // ---- Name entry ------------------------------------------------------------------

    public void ShowNameEntry(Role role, Action<string> onSubmit)
    {
        submit = onSubmit;
        bool haru = role == Role.Haru;
        nameIcon.sprite = haru ? haruIcon : duduIcon;
        nameIcon.preserveAspect = true;
        nameRoleLabel.text = haru ? "하루 PLAYER · 1P" : "두두 PLAYER · 2P";
        nameRoleLabel.color = haru ? HaruColor : DuduColor;
        nameInput.textComponent.color = haru ? new Color(0.55f, 0.27f, 0.1f) : new Color(0.125f, 0.279f, 0.448f);
        nameInput.text = string.Empty;
        nameInput.interactable = true;
        confirmButton.interactable = true;
        submittedMark.SetActive(false);
        // Offline the same PC enters both names one after the other: the page turns.
        bool turning = namePanel.gameObject.activeSelf;
        if (turning) namePanel.gameObject.SetActive(false);
        Show(namePanel, !turning);
        RefreshGuide();
        nameInput.ActivateInputField();
        nameInput.Select();
    }

    private void RefreshGuide()
    {
        if (!nameInput.interactable) return;
        nameGuide.text = $"한글 · 영문 · 숫자 1~{ExhibitionRanking.MaxNameLength}자";
        nameGuide.color = Ink;
    }

    private void OnConfirm()
    {
        if (!nameInput.interactable || submit == null) return;
        string name = ExhibitionRanking.CleanName(nameInput.text);
        if (name.Length == 0)
        {
            nameGuide.text = "이름을 적어주세요!";
            nameGuide.color = new Color(0.85f, 0.25f, 0.2f);
            nameInput.ActivateInputField();
            return;
        }
        nameInput.text = name;
        nameInput.interactable = false;
        confirmButton.interactable = false;
        submittedMark.SetActive(true);
        nameGuide.text = string.Empty;
        Action<string> done = submit;
        submit = null;
        done(name);
    }

    // ---- Waiting ---------------------------------------------------------------------

    public void ShowWaiting(bool haruDone, bool duduDone)
    {
        Show(waitPanel, true);
        haruStatus.text = haruDone ? "제출 완료" : "이름 작성 중...";
        duduStatus.text = duduDone ? "제출 완료" : "이름 작성 중...";
        haruCheck.enabled = haruDone;
        duduCheck.enabled = duduDone;
    }

    private void Update()
    {
        if (fadeStarted >= 0f)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - fadeStarted) / fadeSeconds);
            screenFade.color = new Color(0.98f, 0.94f, 0.85f, 1f - t * t * (3f - 2f * t));
            if (t >= 1f)
            {
                fadeStarted = -1f;
                screenFade.gameObject.SetActive(false);
            }
        }
        if (!waitPanel.gameObject.activeInHierarchy) return;
        float now = Time.unscaledTime - shownAt;
        for (int i = 0; i < dots.Length; i++)
        {
            float bounce = Mathf.Max(0f, Mathf.Sin((now * 4f - i * 0.6f)));
            dots[i].anchoredPosition = new Vector2(dots[i].anchoredPosition.x, -215f + bounce * 18f);
        }
    }

    // ---- Result ----------------------------------------------------------------------

    public struct Result
    {
        public List<ExhibitionRanking.Record> top;
        public string ourId;
        public ExhibitionRanking.Record ours;
        public int ourRank;
    }

    public void ShowResult(Result result, string leaveText, Action onLeave, Action onClose = null)
    {
        leave = onLeave;
        close = onClose;
        resultTitle.text = "오늘의 랭킹";
        FillRows(result.top, result.ourId);
        ourRecordBox.SetActive(result.ours != null);
        if (result.ours != null)
        {
            ourRank.text = $"{result.ourRank}위";
            ourRank.color = RankColor(result.ourRank, Ink);
            ourNames.text = Names(result.ours);
            ourTime.text = ExhibitionRanking.FormatTime(result.ours.clearTime);
            ourMessage.text = result.ourRank == 1 ? "새로운 최고 기록이에요!\n현재 1위!"
                : result.ourRank <= 3 ? $"대단해요!\n현재 {result.ourRank}위예요!"
                : $"현재 {result.ourRank}위예요!";
        }
        CenterList(false);
        leaveLabel.text = leaveText;
        leaveButton.gameObject.SetActive(true);
        closeButton.gameObject.SetActive(onClose != null);
        resultNote.text = string.Empty;
        Show(resultPanel, true);
    }

    // The title's view of the ranking: the list only, closed with the X.
    public void ShowRankingOnly(List<ExhibitionRanking.Record> top, Action onClose)
    {
        close = onClose;
        resultTitle.text = "오늘의 랭킹";
        FillRows(top, null);
        ourRecordBox.SetActive(false);
        CenterList(true);
        leaveButton.gameObject.SetActive(false);
        closeButton.gameObject.SetActive(true);
        resultNote.text = string.Empty;
        Show(resultPanel, true);
    }

    public void SetResultNote(string note) => resultNote.text = note;

    // With no "our record" beside it, the list moves to the middle of the page.
    private void CenterList(bool centered)
    {
        float x = centered ? 0f : -175f;
        foreach (string name in new[] { "Rows", "EmptyText", "Note" })
        {
            RectTransform part = resultPanel.Find(name) as RectTransform;
            if (part != null) part.anchoredPosition = new Vector2(x, part.anchoredPosition.y);
        }
        Transform divider = resultPanel.Find("DividerImage");
        if (divider != null) divider.gameObject.SetActive(!centered);
    }

    private void FillRows(List<ExhibitionRanking.Record> top, string ourId)
    {
        emptyText.gameObject.SetActive(top == null || top.Count == 0);
        for (int i = 0; i < rows.Length; i++)
        {
            Row row = rows[i];
            ExhibitionRanking.Record record = top != null && i < top.Count ? top[i] : null;
            row.root.SetActive(record != null);
            if (record == null) continue;
            bool ours = ourId != null && record.id == ourId;
            row.rank.text = $"{i + 1}";
            row.rank.color = RankColor(i + 1, Ink);
            row.names.text = Names(record);
            row.time.text = ExhibitionRanking.FormatTime(record.clearTime);
            row.highlight.enabled = ours;
            // A star for this team's row and for the podium.
            row.star.enabled = ours || i < 3;
            row.newMark.SetActive(ours);
        }
    }

    private static string Names(ExhibitionRanking.Record record) =>
        $"<color={HaruHex}>{Escape(record.haruName)}</color> & <color={DuduHex}>{Escape(record.duduName)}</color>";

    // Names are shown with rich text around them; a typed '<' must stay a character.
    private static string Escape(string name) => (name ?? string.Empty).Replace("<", "<noparse><</noparse>");

    private static Color RankColor(int rank, Color otherwise) =>
        rank == 1 ? new Color(0.93f, 0.66f, 0.05f) :
        rank == 2 ? new Color(0.55f, 0.6f, 0.68f) :
        rank == 3 ? new Color(0.78f, 0.47f, 0.25f) : otherwise;

    // ---- Notice ----------------------------------------------------------------------

    public void ShowNotice(string message, string buttonText, Action onButton)
    {
        noticeAction = onButton;
        noticeText.text = message;
        noticeButtonLabel.text = buttonText;
        Show(noticePanel, true);
    }
}
