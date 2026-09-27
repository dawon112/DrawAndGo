using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum CoopInputAction
{
    HaruForward,
    HaruLeft,
    HaruBackward,
    HaruRight,
    HaruJump,
    HaruToolSwitch,
    HaruCameraLock,
    DuduLeft,
    DuduRight,
    DuduJump
}

public static class CoopInputSettings
{
    private const string BindingPrefix = "DrawAndGo.Binding.";
    private const string SensitivityKey = "DrawAndGo.LookSensitivityMultiplier";

    private static readonly Key[] DefaultKeys =
    {
        Key.W, Key.A, Key.S, Key.D, Key.Space, Key.E, Key.Y,
        Key.A, Key.D, Key.Space
    };

    private static readonly Key[] Keys = (Key[])DefaultKeys.Clone();
    private static bool loaded;
    private static float lookSensitivityMultiplier = 1f;

    public static float LookSensitivityMultiplier
    {
        get
        {
            Load();
            return lookSensitivityMultiplier;
        }
        set
        {
            Load();
            lookSensitivityMultiplier = Mathf.Clamp(value, 0.2f, 2f);
            PlayerPrefs.SetFloat(SensitivityKey, lookSensitivityMultiplier);
        }
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }

    public static Key GetKey(CoopInputAction action)
    {
        Load();
        return Keys[(int)action];
    }

    public static string GetKeyLabel(CoopInputAction action)
    {
        Key key = GetKey(action);
        switch (key)
        {
            case Key.LeftArrow: return "Left";
            case Key.RightArrow: return "Right";
            case Key.UpArrow: return "Up";
            case Key.DownArrow: return "Down";
            default: return key.ToString();
        }
    }

    public static bool IsPressed(Keyboard keyboard, CoopInputAction action)
    {
        return !SettingsPopupController.IsAnyOpen &&
               keyboard != null && keyboard[GetKey(action)].isPressed;
    }

    public static bool WasPressed(Keyboard keyboard, CoopInputAction action)
    {
        return !SettingsPopupController.IsAnyOpen &&
               keyboard != null && keyboard[GetKey(action)].wasPressedThisFrame;
    }

    public static bool TryRebind(CoopInputAction action, Key newKey)
    {
        if (!CanBind(newKey)) return false;

        Load();
        int index = (int)action;
        int first = index < (int)CoopInputAction.DuduLeft ? 0 : (int)CoopInputAction.DuduLeft;
        int last = index < (int)CoopInputAction.DuduLeft ? (int)CoopInputAction.DuduLeft : Keys.Length;
        Key oldKey = Keys[index];

        for (int i = first; i < last; i++)
        {
            if (i == index || Keys[i] != newKey) continue;
            Keys[i] = oldKey;
            SaveKey(i);
            break;
        }

        Keys[index] = newKey;
        SaveKey(index);
        PlayerPrefs.Save();
        return true;
    }

    public static bool CanBind(Key key)
    {
        return key != Key.None && key != Key.Escape && key != Key.Tab &&
               Enum.IsDefined(typeof(Key), key);
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;

        for (int i = 0; i < Keys.Length; i++)
        {
            string saved = PlayerPrefs.GetString(BindingPrefix + (CoopInputAction)i, DefaultKeys[i].ToString());
            if (Enum.TryParse(saved, out Key key) && CanBind(key)) Keys[i] = key;
        }

        lookSensitivityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), 0.2f, 2f);
    }

    private static void SaveKey(int index)
    {
        PlayerPrefs.SetString(BindingPrefix + (CoopInputAction)index, Keys[index].ToString());
    }
}
