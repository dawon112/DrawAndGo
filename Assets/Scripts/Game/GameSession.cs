using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps the room alive while the title scene is replaced by the game scene.
public sealed class GameSession : MonoBehaviour
{
    public const string MapSceneName = "Player3DScene";
    public static GameSession Current { get; private set; }
    public bool IsHost { get; private set; }

    private ISession session;

    public static void Keep(ISession activeSession)
    {
        GameObject holder = new GameObject("Game Session");
        GameSession keeper = holder.AddComponent<GameSession>();
        DontDestroyOnLoad(holder);
        keeper.session = activeSession;
        keeper.IsHost = activeSession.IsHost;
        activeSession.Deleted += keeper.OnSessionEnded;
        activeSession.RemovedFromSession += keeper.OnSessionEnded;
    }

    private void Awake()
    {
        Current = this;
    }

    private void OnSessionEnded()
    {
        if (session == null) return;
        session.Deleted -= OnSessionEnded;
        session.RemovedFromSession -= OnSessionEnded;
        session = null;
        SceneManager.LoadSceneAsync("TitleScene");
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        if (session == null) return;
        ISession closing = session;
        session = null;
        closing.Deleted -= OnSessionEnded;
        closing.RemovedFromSession -= OnSessionEnded;
        _ = CloseAsync(closing);
    }

    private static async Task CloseAsync(ISession closing)
    {
        try
        {
            if (closing.IsHost)
                await closing.AsHost().DeleteAsync();
            else
                await closing.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
