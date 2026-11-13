#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds Resources/UI/ExhibitionRanking.prefab from the title's own sketchbook art (UI_makeroom,
// UI_Setting) and adds the "랭킹" button to the title menu. After a run the prefab is an
// ordinary asset to edit; running the menu again replaces it with these defaults.
public static class ExhibitionRankingBuilder
{
    private const string PrefabPath = "Assets/Resources/UI/ExhibitionRanking.prefab";
    private const string FontPath = "Assets/Resources/Fonts/HakgyoansimNadeuri-Bold SDF.asset";
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

    private static readonly Color Ink = new Color(0.82f, 0.388f, 0.247f);
    private static readonly Color DeepBlue = new Color(0.125f, 0.279f, 0.448f);
    private static readonly Color HaruColor = new Color(0.91f, 0.5f, 0.25f);
    private static readonly Color DuduColor = new Color(0.25f, 0.56f, 0.83f);
    private static readonly Color PageTint = new Color(0.875f, 0.875f, 0.875f);

    private static TMP_FontAsset font;

    [MenuItem("Tools/Draw And Go/전시 랭킹 UI 다시 만들기")]
    private static void BuildFromMenu()
    {
        BuildCelebrationArt();
        BuildPrefab();
        AddTitleButton();
        EditorUtility.DisplayDialog("전시 랭킹", "Resources/UI/ExhibitionRanking 프리팹을 새로 만들고\n타이틀에 '랭킹' 버튼을 넣었어요.", "확인");
    }

    [MenuItem("Tools/Draw And Go/전시 랭킹 초기화")]
    private static void ResetFromMenu()
    {
        if (!EditorUtility.DisplayDialog("전시 랭킹 초기화",
            "이 PC의 전시 랭킹 기록을 모두 지울까요?\n(지우기 전 파일은 같은 폴더에 백업돼요)", "지우기", "취소")) return;
        ExhibitionRanking.ResetAll();
    }

    [MenuItem("Tools/Draw And Go/전시 랭킹 파일 위치 열기")]
    private static void RevealFile()
    {
        EditorUtility.RevealInFinder(System.IO.File.Exists(ExhibitionRanking.FilePath)
            ? ExhibitionRanking.FilePath : Application.persistentDataPath);
    }

    private static Sprite Art(string folder, string name) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/{folder}/{name}.png");

    private static Sprite Room(string name) => Art("UI_makeroom", name);
    private static Sprite Setting(string name) => Art("UI_Setting", name);

    // ---- Building blocks ----------------------------------------------------------------

    private static RectTransform Node(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject node = new GameObject(name, typeof(RectTransform));
        RectTransform rect = node.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    private static Image Picture(RectTransform rect, Sprite sprite, Color color, bool keepAspect = false)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.preserveAspect = keepAspect;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Label(RectTransform rect, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center, float minSize = 0f)
    {
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        if (minSize > 0f)
        {
            label.enableAutoSizing = true;
            label.fontSizeMin = minSize;
            label.fontSizeMax = size;
        }
        return label;
    }

    private static RectTransform Page(string name, Transform parent, Vector2 size)
    {
        RectTransform page = Node(name, parent, Vector2.zero, size);
        Image image = Picture(page, Room("sketchbook_1"), PageTint);
        image.raycastTarget = true;
        page.gameObject.AddComponent<PopupDropInAnimation>();
        return page;
    }

    // The blue crayon oval with orange writing, as the title's "참가하기".
    private static Button OvalButton(string name, Transform parent, Vector2 position, Vector2 size, string text, float fontSize, out TMP_Text label)
    {
        RectTransform rect = Node(name, parent, position, size);
        Image image = Picture(rect, Room("blue_circle"), Color.white);
        image.raycastTarget = true;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.86f, 0.93f, 1f);
        colors.pressedColor = new Color(0.72f, 0.82f, 0.95f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
        button.colors = colors;
        RectTransform labelRect = Node("Text (TMP)", rect, Vector2.zero, Vector2.zero);
        Stretch(labelRect, new Vector2(24f, 10f), new Vector2(-24f, -10f));
        label = Label(labelRect, text, fontSize, Ink, TextAlignmentOptions.Center, 18f);
        return button;
    }

    private static void Set(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null) { Debug.LogError($"{target.GetType().Name} has no field {field}."); return; }
        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- The prefab -----------------------------------------------------------------------

    public static void BuildPrefab()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        GameObject root = new GameObject("ExhibitionRanking", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform top = root.transform;
        ExhibitionRankingUI ui = root.AddComponent<ExhibitionRankingUI>();

        BuildCelebrationPanel(top, ui);

        RectTransform backdropRect = Node("Backdrop", top, Vector2.zero, Vector2.zero);
        Stretch(backdropRect, Vector2.zero, Vector2.zero);
        Image backdrop = Picture(backdropRect, null, new Color(0.98f, 0.94f, 0.85f));
        backdrop.raycastTarget = true;
        Set(ui, "backdrop", backdrop);

        BuildNamePanel(top, ui);
        BuildWaitPanel(top, ui);
        BuildResultPanel(top, ui);
        BuildNoticePanel(top, ui);

        RectTransform fadeRect = Node("ScreenFade", top, Vector2.zero, Vector2.zero);
        Stretch(fadeRect, Vector2.zero, Vector2.zero);
        Set(ui, "screenFade", Picture(fadeRect, null, new Color(0.98f, 0.94f, 0.85f)));
        Set(ui, "haruIcon", Room("icon_Haru"));
        Set(ui, "duduIcon", Room("icon_Dudu"));

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("Exhibition ranking prefab built.");
    }

    // The left of the final clear screen, as in the sketch: what was cleared, the time
    // together, the ranking button and a small way back to the title. A crayon line parts it
    // from the dance on the right.
    private static void BuildCelebrationPanel(Transform top, ExhibitionRankingUI ui)
    {
        Color brown = new Color(0.55f, 0.33f, 0.2f);
        RectTransform panel = Node("CelebrationPanel", top, Vector2.zero, Vector2.zero);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = new Vector2(0.32f, 1f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        Picture(panel, null, new Color(0.98f, 0.94f, 0.85f)).raycastTarget = true;

        RectTransform divider = Node("Divider", panel, Vector2.zero, new Vector2(1100f, 20f));
        divider.anchorMin = divider.anchorMax = new Vector2(1f, 0.5f);
        divider.anchoredPosition = new Vector2(-4f, 0f);
        divider.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Image line = Picture(divider, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Hud/line_crayon.png"), brown);
        line.type = Image.Type.Sliced;

        RectTransform content = Node("Content", panel, Vector2.zero, new Vector2(560f, 1000f));
        Label(Node("ClearText", content, new Vector2(0f, 300f), new Vector2(540f, 90f)), "모든 스테이지 클리어!", 56f, Ink, TextAlignmentOptions.Center, 34f);
        Label(Node("TimeLabel", content, new Vector2(0f, 120f), new Vector2(400f, 60f)), "함께한 시간", 46f, brown);
        TMP_Text time = Label(Node("TimeText", content, new Vector2(0f, 30f), new Vector2(460f, 110f)), "00:00", 92f, brown);

        RectTransform box = Node("RankingButton", content, new Vector2(0f, -150f), new Vector2(380f, 104f));
        Image boxImage = Picture(box, Room("blue_square"), Color.white);
        boxImage.raycastTarget = true;
        Button ranking = box.gameObject.AddComponent<Button>();
        ranking.targetGraphic = boxImage;
        ColorBlock colors = ranking.colors;
        colors.highlightedColor = new Color(0.86f, 0.93f, 1f);
        colors.pressedColor = new Color(0.72f, 0.82f, 0.95f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
        ranking.colors = colors;
        RectTransform rankingTextRect = Node("Text (TMP)", box, Vector2.zero, Vector2.zero);
        Stretch(rankingTextRect, new Vector2(20f, 8f), new Vector2(-20f, -8f));
        TMP_Text rankingLabel = Label(rankingTextRect, "랭킹 등록", 50f, Ink, TextAlignmentOptions.Center, 26f);

        RectTransform back = Node("TitleButton", content, new Vector2(0f, -248f), new Vector2(320f, 50f));
        TMP_Text backLabel = Label(back, "타이틀로 돌아가기", 32f, new Color(0.45f, 0.4f, 0.38f));
        backLabel.raycastTarget = true;
        Button title = back.gameObject.AddComponent<Button>();
        title.targetGraphic = backLabel;
        ColorBlock backColors = title.colors;
        backColors.highlightedColor = new Color(0.55f, 0.75f, 1f);
        backColors.pressedColor = new Color(0.4f, 0.6f, 0.95f);
        title.colors = backColors;

        Set(ui, "celebrationPanel", panel);
        Set(ui, "celebrationTime", time);
        Set(ui, "rankingButton", ranking);
        Set(ui, "rankingButtonLabel", rankingLabel);
        Set(ui, "titleButton", title);
    }

    // Haru_ani_dance on a loop, and dudu_dance's frames, for CelebrationStage.
    public static void BuildCelebrationArt()
    {
        const string Folder = "Assets/Resources/Celebration";
        const string Fbx = "Assets/Animations/Haru/Haru_ani_dance.fbx";
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Celebration");

        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            // The dance's own steps and turns are baked into the pose from where the clip has them,
            // so Haru moves about as animated yet comes back each loop instead of wandering off.
            bool ready = clip.loopTime && clip.name == "Haru_dance"
                && clip.lockRootRotation && clip.keepOriginalOrientation
                && clip.lockRootHeightY && clip.keepOriginalPositionY
                && clip.lockRootPositionXZ && clip.keepOriginalPositionXZ;
            if (ready) continue;
            clip.loopTime = true;
            clip.name = "Haru_dance";
            clip.lockRootRotation = clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = clip.keepOriginalPositionXZ = true;
            changed = true;
        }
        if (changed)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
        AnimationClip dance = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            if (asset is AnimationClip candidate && !candidate.name.StartsWith("__preview")) dance = candidate;

        string controllerPath = Folder + "/HaruDance.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        UnityEditor.Animations.AnimatorController controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, dance);

        System.Collections.Generic.List<Sprite> frames = new System.Collections.Generic.List<Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Characters/Dudu/dudu_dance.png"))
            if (asset is Sprite sprite) frames.Add(sprite);
        frames.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));

        string artPath = Folder + "/CelebrationArt.asset";
        CelebrationArt art = AssetDatabase.LoadAssetAtPath<CelebrationArt>(artPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<CelebrationArt>();
            AssetDatabase.CreateAsset(art, artPath);
        }
        art.haruDance = controller;
        art.duduDance = frames.ToArray();
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssets();
        Debug.Log($"Celebration art: dance clip {(dance != null ? dance.name : "missing")}, {frames.Count} Dudu frames.");
    }

    private static void BuildNamePanel(Transform top, ExhibitionRankingUI ui)
    {
        RectTransform page = Page("NameEntryPanel", top, new Vector2(824f, 601f));
        Label(Node("TitleText", page, new Vector2(20f, 132f), new Vector2(640f, 110f)),
            "랭킹에 기록을 등록해요.\n당신의 이름을 알려주세요.", 42f, Ink);

        Image icon = Picture(Node("PlayerIcon", page, new Vector2(-150f, 50f), new Vector2(82f, 60f)), Room("icon_Haru"), Color.white, true);
        TMP_Text role = Label(Node("PlayerLabel", page, new Vector2(70f, 50f), new Vector2(340f, 56f)),
            "하루 PLAYER · 1P", 40f, HaruColor, TextAlignmentOptions.MidlineLeft);

        // The blue crayon box is the field itself, as in the title's "방 코드 입력".
        RectTransform box = Node("NameBox", page, new Vector2(0f, -52f), new Vector2(563.5f, 128f));
        Image boxImage = Picture(box, Room("blue_square"), Color.white);
        boxImage.raycastTarget = true;
        RectTransform area = Node("Text Area", box, Vector2.zero, Vector2.zero);
        Stretch(area, new Vector2(48f, 22f), new Vector2(-48f, -22f));
        area.gameObject.AddComponent<RectMask2D>();
        RectTransform placeholderRect = Node("Placeholder", area, Vector2.zero, Vector2.zero);
        Stretch(placeholderRect, Vector2.zero, Vector2.zero);
        TMP_Text placeholder = Label(placeholderRect, "이름을 적어주세요", 42f, new Color(0.45f, 0.6f, 0.76f, 0.7f));
        RectTransform textRect = Node("Text", area, Vector2.zero, Vector2.zero);
        Stretch(textRect, Vector2.zero, Vector2.zero);
        TMP_Text text = Label(textRect, string.Empty, 52f, DeepBlue);
        TMP_InputField input = box.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = boxImage;
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.fontAsset = font;
        input.pointSize = 52f;
        input.caretWidth = 3;
        input.customCaretColor = true;
        input.caretColor = DeepBlue;
        input.selectionColor = new Color(0.55f, 0.75f, 0.95f, 0.5f);
        input.characterLimit = ExhibitionRanking.MaxNameLength;
        input.lineType = TMP_InputField.LineType.SingleLine;
        ColorBlock colors = input.colors;
        colors.highlightedColor = new Color(0.93f, 0.97f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        input.colors = colors;

        Image check = Picture(Node("SubmittedMark", page, new Vector2(232f, -52f), new Vector2(64f, 64f)),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Hud/check.png"), Color.white, true);
        TMP_Text guide = Label(Node("GuideText", page, new Vector2(0f, -140f), new Vector2(520f, 40f)), "한글 · 영문 · 숫자", 30f, Ink);
        Button confirm = OvalButton("ConfirmButton", page, new Vector2(0f, -212f), new Vector2(243.9f, 127.8f), "확인", 50f, out _);

        Set(ui, "namePanel", page);
        Set(ui, "nameIcon", icon);
        Set(ui, "nameRoleLabel", role);
        Set(ui, "nameInput", input);
        Set(ui, "nameGuide", guide);
        Set(ui, "confirmButton", confirm);
        Set(ui, "submittedMark", check.gameObject);
    }

    private static void BuildWaitPanel(Transform top, ExhibitionRankingUI ui)
    {
        RectTransform page = Page("WaitingPanel", top, new Vector2(824f, 601f));
        Label(Node("TitleText", page, new Vector2(20f, 128f), new Vector2(640f, 110f)),
            "기다려주세요.\n상대가 이름을 작성 중이에요.", 44f, Ink);
        Picture(Node("DividerImage", page, new Vector2(10f, -40f), new Vector2(26f, 220f)), Room("blue_line"), Color.white);

        TMP_Text haruStatus, duduStatus;
        Image haruCheck, duduCheck;
        Column(page, -170f, Room("icon_Haru"), "하루 PLAYER", HaruColor, out haruStatus, out haruCheck);
        Column(page, 190f, Room("icon_Dudu"), "두두 PLAYER", DuduColor, out duduStatus, out duduCheck);

        RectTransform[] dots = new RectTransform[3];
        for (int i = 0; i < 3; i++)
        {
            dots[i] = Node("Dot " + (i + 1), page, new Vector2(-40f + i * 40f, -215f), new Vector2(28f, 26f));
            Picture(dots[i], Setting("slider_dot"), Color.white, true);
        }

        Set(ui, "waitPanel", page);
        Set(ui, "haruStatus", haruStatus);
        Set(ui, "duduStatus", duduStatus);
        Set(ui, "haruCheck", haruCheck);
        Set(ui, "duduCheck", duduCheck);
        SerializedObject so = new SerializedObject(ui);
        SerializedProperty list = so.FindProperty("dots");
        list.arraySize = dots.Length;
        for (int i = 0; i < dots.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = dots[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Column(RectTransform page, float x, Sprite icon, string name, Color color, out TMP_Text status, out Image check)
    {
        Picture(Node(name + " Icon", page, new Vector2(x, 10f), new Vector2(120f, 90f)), icon, Color.white, true);
        Label(Node(name + " Label", page, new Vector2(x, -68f), new Vector2(260f, 48f)), name, 36f, color);
        check = Picture(Node(name + " Check", page, new Vector2(x - 78f, -120f), new Vector2(40f, 40f)),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Hud/check.png"), Color.white, true);
        status = Label(Node(name + " Status", page, new Vector2(x + 18f, -120f), new Vector2(200f, 44f)), "이름 작성 중...", 30f, Ink);
    }

    private static void BuildResultPanel(Transform top, ExhibitionRankingUI ui)
    {
        RectTransform page = Page("ResultPanel", top, new Vector2(1150f, 839f));
        TMP_Text title = Label(Node("TitleText", page, new Vector2(0f, 262f), new Vector2(500f, 80f)), "오늘의 랭킹", 66f, Ink);

        RectTransform close = Node("CloseButton", page, Vector2.zero, new Vector2(120f, 130f));
        close.anchorMin = close.anchorMax = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-140f, -181f);
        Image closeImage = Picture(close, Room("Button_X"), Color.white);
        closeImage.raycastTarget = true;
        Button closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;

        RectTransform list = Node("Rows", page, new Vector2(-175f, 0f), new Vector2(600f, 520f));
        SerializedObject so = new SerializedObject(ui);
        SerializedProperty rows = so.FindProperty("rows");
        rows.arraySize = ExhibitionRanking.Shown;
        Sprite star = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Hud/star.png");
        for (int i = 0; i < ExhibitionRanking.Shown; i++)
        {
            RectTransform row = Node("Row " + (i + 1), list, new Vector2(0f, 180f - i * 48f), new Vector2(600f, 46f));
            RectTransform highlightRect = Node("Highlight", row, Vector2.zero, Vector2.zero);
            Stretch(highlightRect, new Vector2(-8f, -3f), new Vector2(8f, 3f));
            Image highlight = Picture(highlightRect, Setting("setting_blue_square_select"), Color.white);
            TMP_Text rank = Label(Node("Rank", row, new Vector2(-275f, 0f), new Vector2(50f, 44f)), (i + 1).ToString(), 32f, Ink);
            Image rowStar = Picture(Node("Star", row, new Vector2(-235f, 0f), new Vector2(30f, 30f)), star, Color.white, true);
            TMP_Text names = Label(Node("Names", row, new Vector2(-60f, 0f), new Vector2(320f, 44f)), "하루 & 두두", 30f, Ink, TextAlignmentOptions.MidlineLeft, 18f);
            TMP_Text time = Label(Node("Time", row, new Vector2(165f, 0f), new Vector2(130f, 44f)), "00:00.00", 28f, DeepBlue);
            TMP_Text newMark = Label(Node("New", row, new Vector2(274f, 2f), new Vector2(70f, 40f)), "NEW!", 24f, new Color(0.9f, 0.3f, 0.2f));
            SerializedProperty entry = rows.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("root").objectReferenceValue = row.gameObject;
            entry.FindPropertyRelative("highlight").objectReferenceValue = highlight;
            entry.FindPropertyRelative("rank").objectReferenceValue = rank;
            entry.FindPropertyRelative("star").objectReferenceValue = rowStar;
            entry.FindPropertyRelative("names").objectReferenceValue = names;
            entry.FindPropertyRelative("time").objectReferenceValue = time;
            entry.FindPropertyRelative("newMark").objectReferenceValue = newMark.gameObject;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        TMP_Text empty = Label(Node("EmptyText", page, new Vector2(-175f, 20f), new Vector2(560f, 120f)),
            "아직 기록이 없어요.\n첫 번째 기록을 남겨보세요!", 36f, Ink);

        Picture(Node("DividerImage", page, new Vector2(142f, -20f), new Vector2(24f, 500f)), Room("blue_line"), Color.white);

        RectTransform box = Node("OurRecord", page, new Vector2(312f, 22f), new Vector2(300f, 380f));
        Picture(box, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Hud/panel_paper.png"), Color.white).type = Image.Type.Sliced;
        Label(Node("Heading", box, new Vector2(0f, 140f), new Vector2(260f, 44f)), "우리의 기록", 34f, Ink);
        TMP_Text ourRank = Label(Node("Rank", box, new Vector2(0f, 76f), new Vector2(260f, 80f)), "1위", 70f, Ink);
        TMP_Text ourNames = Label(Node("Names", box, new Vector2(0f, 10f), new Vector2(264f, 48f)), "하루 & 두두", 34f, Ink, TextAlignmentOptions.Center, 18f);
        TMP_Text ourTime = Label(Node("Time", box, new Vector2(0f, -46f), new Vector2(260f, 56f)), "00:00.00", 44f, DeepBlue);
        TMP_Text ourMessage = Label(Node("Message", box, new Vector2(0f, -124f), new Vector2(264f, 80f)), "현재 1위!", 26f, Ink, TextAlignmentOptions.Center, 16f);

        Button leave = OvalButton("LeaveButton", page, new Vector2(312f, -262f), new Vector2(243.9f, 127.8f), "처음 화면으로", 34f, out TMP_Text leaveLabel);
        TMP_Text note = Label(Node("Note", page, new Vector2(-175f, -312f), new Vector2(600f, 40f)), string.Empty, 26f, Ink);

        Set(ui, "resultPanel", page);
        Set(ui, "resultTitle", title);
        Set(ui, "emptyText", empty);
        Set(ui, "ourRecordBox", box.gameObject);
        Set(ui, "ourRank", ourRank);
        Set(ui, "ourNames", ourNames);
        Set(ui, "ourTime", ourTime);
        Set(ui, "ourMessage", ourMessage);
        Set(ui, "leaveButton", leave);
        Set(ui, "leaveLabel", leaveLabel);
        Set(ui, "closeButton", closeButton);
        Set(ui, "resultNote", note);
    }

    private static void BuildNoticePanel(Transform top, ExhibitionRankingUI ui)
    {
        RectTransform page = Page("NoticePanel", top, new Vector2(824f, 601f));
        TMP_Text text = Label(Node("NoticeText", page, new Vector2(20f, 50f), new Vector2(660f, 200f)), string.Empty, 40f, Ink);
        Button button = OvalButton("NoticeButton", page, new Vector2(0f, -190f), new Vector2(243.9f, 127.8f), "처음 화면으로", 34f, out TMP_Text label);
        Set(ui, "noticePanel", page);
        Set(ui, "noticeText", text);
        Set(ui, "noticeButton", button);
        Set(ui, "noticeButtonLabel", label);
    }

    // ---- The title's "랭킹" button ----------------------------------------------------------

    public static void AddTitleButton()
    {
        var scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
        GameObject start = GameObject.Find("StartButton");
        GameObject settings = GameObject.Find("SettingsButton");
        GameObject quit = GameObject.Find("QuitButton");
        if (start == null || settings == null || quit == null)
        {
            Debug.LogError("Title menu buttons were not found.");
            return;
        }
        GameObject ranking = GameObject.Find("RankingButton");
        if (ranking == null)
        {
            // A copy of "설정": the same text button, the same hover outline.
            ranking = Object.Instantiate(settings, settings.transform.parent);
            ranking.name = "RankingButton";
            SerializedObject button = new SerializedObject(ranking.GetComponent<Button>());
            button.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").arraySize = 0;
            button.ApplyModifiedPropertiesWithoutUndo();
            ranking.GetComponentInChildren<TMP_Text>().text = "랭킹";
            ranking.AddComponent<TitleRankingButton>();
        }
        ranking.transform.SetSiblingIndex(start.transform.GetSiblingIndex() + 1);
        // Game start, ranking, settings, quit: the same spacing as before, one step longer.
        Place(start, 0.39f);
        Place(ranking, 0.30f);
        Place(settings, 0.21f);
        Place(quit, 0.12f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void Place(GameObject button, float y)
    {
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = new Vector2(rect.anchorMin.x, y);
        rect.anchorMax = new Vector2(rect.anchorMax.x, y);
    }
}
#endif
