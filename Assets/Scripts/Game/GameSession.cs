using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps the room alive while the title scene is replaced by the stage scenes.
public sealed class GameSession : MonoBehaviour
{
    public static string FirstSceneName => StageSequence.Load().FirstStage;
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

    // Ends the room for both players. The peer is sent to the title by OnSessionEnded.
    public void ReturnToTitle()
    {
        SceneManager.LoadSceneAsync(StageSequence.Load().titleScene);
        Destroy(gameObject);
    }

    private void OnSessionEnded()
    {
        if (session == null) return;
        session.Deleted -= OnSessionEnded;
        session.RemovedFromSession -= OnSessionEnded;
        session = null;
        ShutdownNetwork();
        SceneManager.LoadSceneAsync(StageSequence.Load().titleScene);
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
        finally
        {
            ShutdownNetwork();
        }
    }

    // A rematch must start from a stopped NetworkManager, never a half-closed one.
    private static void ShutdownNetwork()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
            manager.Shutdown();
    }
}
