using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SettingsPopupController : MonoBehaviour
{
    public static bool IsAnyOpen { get; private set; }
    public static int LastClosedFrame { get; private set; } = -1;

    private enum Page
    {
        Sound,
        Display,
        ControlSelect,
        HaruControls,
        DuduControls
    }

    private readonly GameObject[] pages = new GameObject[5];
    private readonly Dictionary<CoopInputAction, TMP_Text> keyLabels = new Dictionary<CoopInputAction, TMP_Text>();
    private Page currentPage;
    private bool waitingForKey;
    private CoopInputAction waitingAction;
    private TMP_Text waitingLabel;

    private void Awake()
    {
        pages[(int)Page.Sound] = FindPage("SoundPage");
        pages[(int)Page.Display] = FindPage("DisplayPage");
        pages[(int)Page.ControlSelect] = FindPage("ControlSelectPage");
        pages[(int)Page.HaruControls] = FindPage("HaruControlsPage");
        pages[(int)Page.DuduControls] = FindPage("DuduControlsPage");

        foreach (GameObject page in pages)
        {
            if (page != null) continue;
            Debug.LogError("A settings page is missing from SettingsPopup.", this);
            enabled = false;
            return;
        }

        GameObject settingsButtonObject = GameObject.Find("SettingsButton");
        Button settingsButton = settingsButtonObject != null ? settingsButtonObject.GetComponent<Button>() : null;
        if (settingsButton != null) settingsButton.onClick.AddListener(Open);

        HookPage(Page.Sound, () => ShiftMain(-1), () => ShiftMain(1));
        HookPage(Page.Display, () => ShiftMain(-1), () => ShiftMain(1));
        HookPage(Page.ControlSelect, () => ShiftMain(-1), () => ShiftMain(1));
        HookPage(Page.HaruControls, () => ShowPage(Page.DuduControls), () => ShowPage(Page.DuduControls));
        HookPage(Page.DuduControls, () => ShowPage(Page.HaruControls), () => ShowPage(Page.HaruControls));

        HookButton(pages[(int)Page.ControlSelect].transform, "HaruControlButton", () => ShowPage(Page.HaruControls));
        HookButton(pages[(int)Page.ControlSelect].transform, "DuduControlButton", () => ShowPage(Page.DuduControls));

        Transform haru = pages[(int)Page.HaruControls].transform;
        BindKey(haru, "MoveJumpRow/setting_key3/WButton", CoopInputAction.HaruForward);
        BindKey(haru, "MoveJumpRow/setting_key3/AButton", CoopInputAction.HaruLeft);
        BindKey(haru, "MoveJumpRow/setting_key3/SButton", CoopInputAction.HaruBackward);
        BindKey(haru, "MoveJumpRow/setting_key3/DButton", CoopInputAction.HaruRight);
        BindKey(haru, "MoveJumpRow/setting_key2", CoopInputAction.HaruJump);
        BindKey(haru, "ToolSwitchRow/ToolSwitchKeyButton", CoopInputAction.HaruToolSwitch);
        BindKey(haru, "CameraLockRow/CameraLockKeyButton", CoopInputAction.HaruCameraLock);

        Transform dudu = pages[(int)Page.DuduControls].transform;
        BindKey(dudu, "MoveJumpRow/setting_key3/AButton", CoopInputAction.DuduLeft);
        BindKey(dudu, "MoveJumpRow/setting_key3/DButton", CoopInputAction.DuduRight);
        BindKey(dudu, "MoveJumpRow/setting_key2", CoopInputAction.DuduJump);

        Transform sliderObject = haru.Find("CameraSensitivityRow/CameraSensitivitySlider");
        Slider sensitivitySlider = sliderObject != null ? sliderObject.GetComponent<Slider>() : null;
        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = 0.2f;
            sensitivitySlider.maxValue = 2f;
            sensitivitySlider.wholeNumbers = false;
            sensitivitySlider.SetValueWithoutNotify(CoopInputSettings.LookSensitivityMultiplier);
            sensitivitySlider.onValueChanged.AddListener(value => CoopInputSettings.LookSensitivityMultiplier = value);
        }

        Transform sound = pages[(int)Page.Sound].transform;
        BindVolume(sound, "MasterRow/MasterSoundSlider", CoopAudioSettings.MasterVolume,
            value => CoopAudioSettings.MasterVolume = value);
        BindVolume(sound, "BgmRow/BgmSoundSlider", CoopAudioSettings.BgmVolume,
            value => CoopAudioSettings.BgmVolume = value);
        BindVolume(sound, "SfxRow/SfxSoundSlider", CoopAudioSettings.SfxVolume,
            value => CoopAudioSettings.SfxVolume = value);
        BindVolume(sound, "VoiceRow/VoiceSoundSlider", CoopAudioSettings.VoiceVolume,
            value => CoopAudioSettings.VoiceVolume = value);

        Transform micObject = sound.Find("MicRow/MicOnButton");
        Button micButton = micObject != null ? micObject.GetComponent<Button>() : null;
        TMP_Text micLabel = micButton != null ? micButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (micButton != null && micLabel != null)
        {
            micLabel.text = CoopAudioSettings.MicEnabled ? "ON" : "OFF";
            micButton.onClick.AddListener(() =>
            {
                CoopAudioSettings.MicEnabled = !CoopAudioSettings.MicEnabled;
                micLabel.text = CoopAudioSettings.MicEnabled ? "ON" : "OFF";
                CoopAudioSettings.Save();
            });
        }
        else Debug.LogWarning("Microphone button or label is missing.", sound);

        Transform display = pages[(int)Page.Display].transform;
        TMP_Dropdown modeDropdown = display.Find("ScreenModeRow/ScreenModeDropdown")?.GetComponent<TMP_Dropdown>();
        TMP_Dropdown resolutionDropdown = display.Find("ResolutionRow/ResolutionDropdown")?.GetComponent<TMP_Dropdown>();
        Slider brightnessSlider = display.Find("BrightnessRow/BrightnessSlider")?.GetComponent<Slider>();

        if (modeDropdown != null)
        {
            modeDropdown.SetValueWithoutNotify(CoopDisplaySettings.ModeIndex);
            modeDropdown.RefreshShownValue();
            modeDropdown.onValueChanged.AddListener(CoopDisplaySettings.SetMode);
        }
        else Debug.LogWarning("Screen mode dropdown is missing.", display);

        if (resolutionDropdown != null)
        {
            resolutionDropdown.SetValueWithoutNotify(CoopDisplaySettings.ResolutionIndex);
            resolutionDropdown.RefreshShownValue();
            resolutionDropdown.onValueChanged.AddListener(CoopDisplaySettings.SetResolution);
        }
        else Debug.LogWarning("Resolution dropdown is missing.", display);

        if (brightnessSlider != null)
        {
            brightnessSlider.minValue = 0f;
            brightnessSlider.maxValue = 1f;
            brightnessSlider.SetValueWithoutNotify(CoopDisplaySettings.Brightness);
            brightnessSlider.onValueChanged.AddListener(value => CoopDisplaySettings.Brightness = value);
        }
        else Debug.LogWarning("Brightness slider is missing.", display);

        foreach (GameObject page in pages) page.SetActive(false);
        gameObject.SetActive(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (waitingForKey)
        {
            foreach (KeyControl key in keyboard.allKeys)
            {
                if (!key.wasPressedThisFrame) continue;
                if (key.keyCode == Key.Escape)
                {
                    CancelRebind();
                    return;
                }
                if (!CoopInputSettings.TryRebind(waitingAction, key.keyCode)) return;
                waitingForKey = false;
                waitingLabel = null;
                RefreshKeyLabels();
                return;
            }
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame) Close();
    }

    public void Open()
    {
        CancelRebind();
        ShowPage(Page.Sound);
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        IsAnyOpen = true;
    }

    public void Close()
    {
        CancelRebind();
        CoopInputSettings.Save();
        CoopDisplaySettings.Save();
        CoopAudioSettings.Save();
        LastClosedFrame = Time.frameCount;
        IsAnyOpen = false;
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        IsAnyOpen = false;
    }

    private GameObject FindPage(string name)
    {
        Transform page = transform.Find(name);
        return page != null ? page.gameObject : null;
    }

    private void HookPage(Page page, UnityAction left, UnityAction right)
    {
        Transform pageTransform = pages[(int)page].transform;
        HookButton(pageTransform, "CloseButton", Close);
        HookButton(pageTransform, "LeftArrowButton", left);
        HookButton(pageTransform, "RightArrowButton", right);
    }

    private static void HookButton(Transform parent, string path, UnityAction action)
    {
        Transform target = parent.Find(path);
        Button button = target != null ? target.GetComponent<Button>() : null;
        if (button != null) button.onClick.AddListener(action);
        else Debug.LogWarning("Settings button is missing: " + parent.name + "/" + path, parent);
    }

    private void BindKey(Transform page, string path, CoopInputAction action)
    {
        Transform target = page.Find(path);
        Button button = target != null ? target.GetComponent<Button>() : null;
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (button == null || label == null)
        {
            Debug.LogWarning("Key button or label is missing: " + page.name + "/" + path, page);
            return;
        }

        keyLabels[action] = label;
        label.text = CoopInputSettings.GetKeyLabel(action);
        button.onClick.AddListener(() => BeginRebind(action, label));
    }

    private static void BindVolume(Transform page, string path, float value, UnityAction<float> onChange)
    {
        Transform target = page.Find(path);
        Slider slider = target != null ? target.GetComponent<Slider>() : null;
        if (slider == null)
        {
            Debug.LogWarning("Volume slider is missing: " + page.name + "/" + path, page);
            return;
        }

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(onChange);
    }

    private void BeginRebind(CoopInputAction action, TMP_Text label)
    {
        CancelRebind();
        waitingAction = action;
        waitingLabel = label;
        waitingForKey = true;
        label.text = "...";
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void CancelRebind()
    {
        if (!waitingForKey) return;
        waitingForKey = false;
        if (waitingLabel != null) waitingLabel.text = CoopInputSettings.GetKeyLabel(waitingAction);
        waitingLabel = null;
    }

    private void RefreshKeyLabels()
    {
        foreach (KeyValuePair<CoopInputAction, TMP_Text> pair in keyLabels)
            if (pair.Value != null) pair.Value.text = CoopInputSettings.GetKeyLabel(pair.Key);
    }

    private void ShiftMain(int direction)
    {
        int next = ((int)currentPage + direction + 3) % 3;
        ShowPage((Page)next);
    }

    private void ShowPage(Page page)
    {
        CancelRebind();
        currentPage = page;
        for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == (int)page);
    }
}
