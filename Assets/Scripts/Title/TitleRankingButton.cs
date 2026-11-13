using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The title menu's "랭킹" button: shows this PC's exhibition ranking. The host PC (PC1) holds
// the real one. Operators can clear it by holding Ctrl + Shift + Backspace for three seconds
// while it is open; nothing a visitor clicks can do that.
[RequireComponent(typeof(Button))]
public sealed class TitleRankingButton : MonoBehaviour
{
    private const float ResetHoldSeconds = 3f;

    private ExhibitionRankingUI ui;
    private float resetHeld;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Open);
    }

    private void Open()
    {
        if (ui == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            ui = Instantiate(Resources.Load<ExhibitionRankingUI>("UI/ExhibitionRanking"), canvas.rootCanvas.transform, false);
            // As a child of the title's canvas, not a canvas of its own.
            Destroy(ui.GetComponent<GraphicRaycaster>());
            Destroy(ui.GetComponent<CanvasScaler>());
            Destroy(ui.GetComponent<Canvas>());
            RectTransform rect = (RectTransform)ui.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            ui.SetBackdrop(false);
        }
        ui.gameObject.SetActive(true);
        ui.transform.SetAsLastSibling();
        Refresh();
    }

    private void Refresh() => ui.ShowRankingOnly(ExhibitionRanking.Top(), Close);

    private void Close()
    {
        if (ui != null) ui.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (ui == null || !ui.gameObject.activeSelf) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }
        bool holding = keyboard.ctrlKey.isPressed && keyboard.shiftKey.isPressed && keyboard.backspaceKey.isPressed;
        resetHeld = holding ? resetHeld + Time.unscaledDeltaTime : 0f;
        if (resetHeld < ResetHoldSeconds) return;
        resetHeld = float.NegativeInfinity;
        ExhibitionRanking.ResetAll();
        Refresh();
        ui.SetResultNote("랭킹을 초기화했어요.");
    }
}
