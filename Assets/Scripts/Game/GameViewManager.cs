using UnityEngine;
using UnityEngine.InputSystem;

public sealed class GameViewManager : MonoBehaviour
{
    [SerializeField] private bool developmentViewSwitch = true;
    [SerializeField] private Camera haruCamera;
    [SerializeField] private Player3DMovement haruMovement;
    [SerializeField] private Player3DLook haruLook;
    [SerializeField] private Camera duduCamera;
    [SerializeField] private DuduSurfaceMovement duduMovement;
    [SerializeField] private HaruDrawingController haruDrawing;
    [SerializeField] private CrosshairController crosshair;

    private bool duduMode;

    public void Configure(
        Camera newHaruCamera,
        Player3DMovement newHaruMovement,
        Player3DLook newHaruLook,
        Camera newDuduCamera,
        DuduSurfaceMovement newDuduMovement)
    {
        haruCamera = newHaruCamera;
        haruMovement = newHaruMovement;
        haruLook = newHaruLook;
        duduCamera = newDuduCamera;
        duduMovement = newDuduMovement;
    }

    private void Start()
    {
        SetDuduMode(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (developmentViewSwitch && keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            SetDuduMode(!duduMode);
    }

    private void SetDuduMode(bool enabled)
    {
        duduMode = enabled;
        GameObject player3D = haruMovement != null
            ? haruMovement.transform.root.gameObject
            : null;

        if (!enabled && player3D != null)
            player3D.SetActive(true);

        if (enabled && haruDrawing != null)
            haruDrawing.ForceDisableCameraLock();
        if (haruCamera != null)
            haruCamera.gameObject.SetActive(!enabled);
        if (haruMovement != null)
            haruMovement.enabled = !enabled;
        if (haruLook != null)
            haruLook.enabled = !enabled;
        if (duduCamera != null)
            duduCamera.gameObject.SetActive(enabled);
        if (duduMovement != null)
            duduMovement.SetInputEnabled(enabled);
        if (haruDrawing != null)
            haruDrawing.enabled = !enabled;
        if (crosshair != null)
            crosshair.SetVisible(!enabled);

        if (enabled && player3D != null)
            player3D.SetActive(false);

        if (enabled)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
