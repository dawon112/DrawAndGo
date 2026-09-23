using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

public class RoomSessionUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject roomSelectPanel;
    [SerializeField] private GameObject hostRoomPanel;
    [SerializeField] private GameObject joinRoomPanel;
    [SerializeField] private GameObject rolePanel;

    [Header("Buttons")]
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button openJoinButton;
    [SerializeField] private Button confirmJoinButton;
    [SerializeField] private Button hostCloseButton;
    [SerializeField] private Button joinCloseButton;

    [Header("Room")]
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text hostGuideText;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private TMP_Text joinGuideText;

    [Header("Roles")]
    [SerializeField] private TMP_Text friendNameText;
    [SerializeField] private TMP_Text myNameText;
    [SerializeField] private TMP_Text connectionText;
    [SerializeField] private Image friendIcon;
    [SerializeField] private Image myIcon;
    [SerializeField] private Sprite haruIcon;
    [SerializeField] private Sprite duduIcon;

    private ISession session;
    private bool operationInFlight;
    private int requestVersion;
    private float nextPlayerCheck;

    private void Awake()
    {
        createRoomButton.onClick.AddListener(OnCreateRoom);
        openJoinButton.onClick.AddListener(OnOpenJoin);
        confirmJoinButton.onClick.AddListener(OnConfirmJoin);
        hostCloseButton.onClick.AddListener(OnCloseRoom);
        joinCloseButton.onClick.AddListener(OnCloseRoom);
    }

    private void OnDestroy()
    {
        createRoomButton.onClick.RemoveListener(OnCreateRoom);
        openJoinButton.onClick.RemoveListener(OnOpenJoin);
        confirmJoinButton.onClick.RemoveListener(OnConfirmJoin);
        hostCloseButton.onClick.RemoveListener(OnCloseRoom);
        joinCloseButton.onClick.RemoveListener(OnCloseRoom);
        requestVersion++;
        if (session == null) return;
        ISession closingSession = session;
        DetachSession();
        _ = CloseSessionAsync(closingSession);
    }

    private void Update()
    {
        if (session == null || Time.unscaledTime < nextPlayerCheck) return;
        nextPlayerCheck = Time.unscaledTime + 1f;
        RefreshPlayerCount();
    }

    private void OnCreateRoom()
    {
        roomCodeText.text = "······";
        if (operationInFlight)
        {
            hostGuideText.text = "이전 요청 처리 중이에요. 잠시 후 다시 시도해 주세요.";
            return;
        }
        hostGuideText.text = "방을 만드는 중이에요...";
        _ = CreateRoomAsync(++requestVersion);
    }

    private async Task CreateRoomAsync(int version)
    {
        operationInFlight = true;
        try
        {
            await SignInAsync();
            if (version != requestVersion) return;
            ISession created = await MultiplayerService.Instance.CreateSessionAsync(
                new SessionOptions { MaxPlayers = 2, IsPrivate = true });
            if (version != requestVersion)
            {
                await CloseSessionAsync(created);
                return;
            }
            AttachSession(created);
            roomCodeText.text = created.Code;
            hostGuideText.text = "친구에게 코드를 알려주세요!";
            RefreshPlayerCount();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (version == requestVersion)
                hostGuideText.text = "방을 만들지 못했어요. 인터넷과 Unity Services 설정을 확인해 주세요.";
        }
        finally
        {
            operationInFlight = false;
        }
    }

    private void OnOpenJoin()
    {
        roomCodeInput.SetTextWithoutNotify(string.Empty);
        joinGuideText.text = "코드를 입력하세요!";
        confirmJoinButton.interactable = true;
    }

    private void OnConfirmJoin()
    {
        if (operationInFlight || session != null) return;
        string code = roomCodeInput.text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            joinGuideText.text = "방 코드를 입력해 주세요.";
            return;
        }
        roomCodeInput.SetTextWithoutNotify(code);
        joinGuideText.text = "방에 참가하는 중이에요...";
        confirmJoinButton.interactable = false;
        _ = JoinRoomAsync(code, ++requestVersion);
    }

    private async Task JoinRoomAsync(string code, int version)
    {
        operationInFlight = true;
        try
        {
            await SignInAsync();
            if (version != requestVersion) return;
            ISession joined = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            if (version != requestVersion)
            {
                await CloseSessionAsync(joined);
                return;
            }
            AttachSession(joined);
            joinGuideText.text = "방장과 연결 중이에요...";
            RefreshPlayerCount();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (version == requestVersion)
            {
                joinGuideText.text = "참가하지 못했어요. 코드와 인터넷 연결을 확인해 주세요.";
                confirmJoinButton.interactable = true;
            }
        }
        finally
        {
            operationInFlight = false;
        }
    }

    private static async Task SignInAsync()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void AttachSession(ISession joined)
    {
        session = joined;
        session.Changed += RefreshPlayerCount;
        session.PlayerJoined += OnPlayerChanged;
        session.PlayerHasLeft += OnPlayerChanged;
        session.Deleted += OnSessionEnded;
        session.RemovedFromSession += OnSessionEnded;
    }

    private void DetachSession()
    {
        if (session == null) return;
        session.Changed -= RefreshPlayerCount;
        session.PlayerJoined -= OnPlayerChanged;
        session.PlayerHasLeft -= OnPlayerChanged;
        session.Deleted -= OnSessionEnded;
        session.RemovedFromSession -= OnSessionEnded;
        session = null;
    }

    private void OnPlayerChanged(string _) => RefreshPlayerCount();

    private void RefreshPlayerCount()
    {
        if (session == null) return;
        if (session.PlayerCount >= 2)
        {
            bool isHost = session.IsHost;
            friendNameText.text = isHost ? "두두" : "하루";
            myNameText.text = isHost ? "하루" : "두두";
            friendIcon.sprite = isHost ? duduIcon : haruIcon;
            myIcon.sprite = isHost ? haruIcon : duduIcon;
            friendIcon.gameObject.SetActive(true);
            myIcon.gameObject.SetActive(true);
            connectionText.text = "두 명이 연결됐어요";
            if (!rolePanel.activeSelf) rolePanel.SetActive(true);
            hostRoomPanel.SetActive(false);
            joinRoomPanel.SetActive(false);
        }
        else if (rolePanel.activeSelf)
        {
            rolePanel.SetActive(false);
            if (session.IsHost)
            {
                hostRoomPanel.SetActive(true);
                hostGuideText.text = "친구를 기다리는 중이에요...";
            }
            else
            {
                joinRoomPanel.SetActive(true);
                joinGuideText.text = "방장과 연결 중이에요...";
            }
        }
    }

    private void OnSessionEnded()
    {
        DetachSession();
        requestVersion++;
        confirmJoinButton.interactable = true;
        rolePanel.SetActive(false);
        hostRoomPanel.SetActive(false);
        joinRoomPanel.SetActive(true);
        roomSelectPanel.SetActive(false);
        joinGuideText.text = "방이 종료됐어요. 다시 참가해 주세요.";
    }

    private void OnCloseRoom()
    {
        requestVersion++;
        confirmJoinButton.interactable = true;
        if (session == null) return;
        ISession closingSession = session;
        DetachSession();
        _ = CloseSessionAsync(closingSession);
    }

    private static async Task CloseSessionAsync(ISession closingSession)
    {
        try
        {
            if (closingSession.IsHost)
                await closingSession.AsHost().DeleteAsync();
            else
                await closingSession.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
