using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Keeps the room alive while the title scene is replaced by the stage scenes, and
// sends both players back to the title when the room ends or the friend drops out.
public sealed class GameSession : MonoBehaviour
{
    private const float NoticeDuration = 3f;

    public static string FirstSceneName => StageSequence.Load().FirstStage;
    public static GameSession Current { get; private set; }
    public bool IsHost { get; private set; }

    private ISession session;
    private NetworkManager network;
    private bool endExpected;
    private bool leaving;

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

    private void Start()
    {
        network = NetworkManager.Singleton;
        if (network == null) return;
        network.OnClientDisconnectCallback += OnNetworkDisconnect;
        network.OnTransportFailure += OnTransportFailure;
    }

    // The game is finishing on purpose; the room closing must not read as a dropout.
    public void ExpectEnd() => endExpected = true;

    // Ends the room for both players. The peer is sent to the title by OnSessionEnded.
    public void ReturnToTitle()
    {
        if (this == null) return;
        SceneManager.LoadSceneAsync(StageSequence.Load().titleScene);
        Destroy(gameObject);
    }

    private void OnNetworkDisconnect(ulong clientId)
    {
        // The host also reports its own shutdown; only a lost peer matters here.
        if (network != null && network.IsHost && clientId == NetworkManager.ServerClientId) return;
        LoseConnection();
    }

    private void OnTransportFailure() => LoseConnection();

    private void OnSessionEnded()
    {
        if (session == null) return;
        session.Deleted -= OnSessionEnded;
        session.RemovedFromSession -= OnSessionEnded;
        session = null;
        if (endExpected) ReturnToTitle();
        else LoseConnection();
    }

    private void LoseConnection()
    {
        if (endExpected || leaving) return;
        leaving = true;
        ShowNotice("친구와 연결이 끊겼어요.\n잠시 후 처음 화면으로 돌아갑니다.");
        StartCoroutine(ReturnAfterNotice());
    }

    private IEnumerator ReturnAfterNotice()
    {
        yield return new WaitForSecondsRealtime(NoticeDuration);
        ReturnToTitle();
    }

    private void ShowNotice(string message)
    {
        GameObject root = new GameObject("Connection Notice", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.SetParent(root.transform, false);
        panelRect.anchorMin = new Vector2(0.25f, 0.4f);
        panelRect.anchorMax = new Vector2(0.75f, 0.6f);
        panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.055f, 0.08f, 0.12f, 0.92f);

        GameObject label = new GameObject("Text", typeof(RectTransform), typeof(Text));
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.SetParent(panelRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        Text text = label.GetComponent<Text>();
        text.font = GameFont.Bold;
        text.fontSize = 36;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = message;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        if (network != null)
        {
            network.OnClientDisconnectCallback -= OnNetworkDisconnect;
            network.OnTransportFailure -= OnTransportFailure;
        }
        if (session == null)
        {
            ShutdownNetwork();
            return;
        }
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
