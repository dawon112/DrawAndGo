using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class Player3DMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float gravity = 20f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
    [SerializeField] private GameObject visualPrefab;
    [SerializeField, Min(0.1f)] private float visualHeight = 1.8f;

    private CharacterController controller;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        EnsureVisual();
    }

    private void EnsureVisual()
    {
        Transform capsule = transform.Find("Capsule Visual");
        if (capsule != null) capsule.gameObject.SetActive(false);
        if (visualPrefab == null || transform.Find("Haru Visual") != null) return;

        GameObject visual = Instantiate(visualPrefab, transform);
        visual.name = "Haru Visual";
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        visual.transform.localScale = Vector3.one;
        foreach (Camera modelCamera in visual.GetComponentsInChildren<Camera>(true)) modelCamera.enabled = false;
        foreach (Light modelLight in visual.GetComponentsInChildren<Light>(true)) modelLight.enabled = false;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.y <= 0.001f) return;
        visual.transform.localScale *= visualHeight / bounds.size.y;
        bounds = visual.GetComponentsInChildren<Renderer>(true)[0].bounds;
        Renderer[] scaledRenderers = visual.GetComponentsInChildren<Renderer>(true);
        for (int i = 1; i < scaledRenderers.Length; i++) bounds.Encapsulate(scaledRenderers[i].bounds);
        visual.transform.position += Vector3.up * (transform.position.y - bounds.min.y);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        if (keyboard != null)
        {
            input.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            input.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        }

        input = Vector2.ClampMagnitude(input, 1f);
        Vector3 horizontalMotion = (transform.right * input.x + transform.forward * input.y) * moveSpeed;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                verticalVelocity = Mathf.Sqrt(jumpHeight * 2f * gravity);
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }

        Vector3 motion = horizontalMotion + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }
}
