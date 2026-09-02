using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class ViewSceneSwitcher : MonoBehaviour
{
    [SerializeField] private string twoDSceneName = "SideScrollerPrototype";
    [SerializeField] private string threeDSceneName = "Player3DScene";

    private bool isLoading;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (isLoading || keyboard == null || !keyboard.tabKey.wasPressedThisFrame)
            return;

        string currentScene = SceneManager.GetActiveScene().name;
        string targetScene = currentScene == threeDSceneName ? twoDSceneName : threeDSceneName;
        isLoading = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(targetScene);
    }
}
