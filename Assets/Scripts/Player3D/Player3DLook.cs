using UnityEngine;
using UnityEngine.InputSystem;

public sealed class Player3DLook : MonoBehaviour
{
    [SerializeField] private Transform playerBody;
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;
    [SerializeField, Range(1f, 89f)] private float verticalLookLimit = 80f;

    private float pitch;

    public void SetPlayerBody(Transform body)
    {
        playerBody = body;
    }

    private void OnEnable()
    {
        LockCursor();
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
            LockCursor();

        if (mouse == null || Cursor.lockState != CursorLockMode.Locked || playerBody == null)
            return;

        Vector2 lookDelta = mouse.delta.ReadValue() * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - lookDelta.y, -verticalLookLimit, verticalLookLimit);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        playerBody.Rotate(Vector3.up, lookDelta.x, Space.World);
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
