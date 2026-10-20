using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Online tutorial. The host (Haru) runs the step logic: Haru's input and strokes are
// local there, and Dudu's pose already arrives there. The client (Dudu) builds the same
// course but only mirrors the host's dialogue, instructions, highlights, stars and door.
// Either player may press Enter to advance a line; the host accepts one press per line.
public sealed partial class TutorialSequenceController
{
    private const string StateMessage = "TutorialStateV1";
    private const string AdvanceMessage = "TutorialAdvanceV1";
    private const float StateInterval = 0.1f;

    [Serializable]
    private sealed class TutorialNetState
    {
        public int step, erasePhase, serial, stars;
        public bool dialogue, instruction, highlight, eraseHighlight, replacementHighlight, counter, door;
        public string speaker, line, instructionText;
    }

    private NetworkManager tutorialNetwork;
    private int dialogueSerial;
    private int remoteSerial = -1;
    private float nextStateSend;
    private bool remoteClearPlayed;

    private static bool IsNetworked => GameSession.Current != null;
    // Offline (single PC with Tab view switching) behaves exactly as before.
    private static bool IsAuthority => GameSession.Current == null || GameSession.Current.IsHost;

    private void InitializeNetwork()
    {
        if (!IsNetworked) return;
        tutorialNetwork = NetworkManager.Singleton;
        if (tutorialNetwork == null || tutorialNetwork.CustomMessagingManager == null) return;
        if (IsAuthority)
            tutorialNetwork.CustomMessagingManager.RegisterNamedMessageHandler(AdvanceMessage, OnAdvanceRequest);
        else
            tutorialNetwork.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnTutorialState);
    }

    private void DisposeNetwork()
    {
        if (tutorialNetwork == null || tutorialNetwork.CustomMessagingManager == null) return;
        tutorialNetwork.CustomMessagingManager.UnregisterNamedMessageHandler(
            IsAuthority ? AdvanceMessage : StateMessage);
        tutorialNetwork = null;
    }

    private bool NetworkReady => tutorialNetwork != null && tutorialNetwork.IsConnectedClient;

    private void SendTutorialState()
    {
        if (!IsNetworked || !IsAuthority || !NetworkReady || Time.unscaledTime < nextStateSend) return;
        nextStateSend = Time.unscaledTime + StateInterval;
        var state = new TutorialNetState
        {
            step = (int)CurrentStep,
            erasePhase = (int)CurrentErasePhase,
            serial = dialogueSerial,
            dialogue = dialoguePanel.activeSelf,
            speaker = speaker.text,
            line = dialogue.text,
            instruction = instructionPanel.activeSelf,
            instructionText = instruction.text,
            highlight = highlight.activeSelf,
            eraseHighlight = eraseHighlight != null && eraseHighlight.activeSelf,
            replacementHighlight = replacementHighlight != null && replacementHighlight.activeSelf,
            stars = tutorialRoom != null ? tutorialRoom.CollectedStars : 0,
            counter = tutorialRoom != null && tutorialRoom.IsStarCounterVisible,
            door = tutorialRoom != null && tutorialRoom.DoorOpened
        };
        string json = JsonUtility.ToJson(state);
        using var writer = new FastBufferWriter(8 + json.Length * 2, Allocator.Temp);
        writer.WriteValueSafe(json);
        foreach (ulong clientId in tutorialNetwork.ConnectedClientsIds)
            if (clientId != tutorialNetwork.LocalClientId)
                tutorialNetwork.CustomMessagingManager.SendNamedMessage(
                    StateMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    // Client per-frame work in place of the step logic.
    private void UpdateTutorialClient()
    {
        if (!NetworkReady || !dialoguePanel.activeSelf || remoteSerial < 0 || Keyboard.current == null) return;
        if (!Keyboard.current.enterKey.wasPressedThisFrame && !Keyboard.current.numpadEnterKey.wasPressedThisFrame) return;
        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(remoteSerial);
        tutorialNetwork.CustomMessagingManager.SendNamedMessage(
            AdvanceMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private void OnAdvanceRequest(ulong sender, FastBufferReader reader)
    {
        if (!built || sender == NetworkManager.ServerClientId ||
            !tutorialNetwork.ConnectedClients.ContainsKey(sender) || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int serial);
        // Both players pressing Enter on the same line must advance it only once.
        if (dialoguePanel.activeSelf && serial == dialogueSerial) AdvanceDialogue();
    }

    private void OnTutorialState(ulong sender, FastBufferReader reader)
    {
        if (!built || sender != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out string json);
        TutorialNetState state = JsonUtility.FromJson<TutorialNetState>(json);
        if (state != null) ApplyTutorialState(state);
    }

    private void ApplyTutorialState(TutorialNetState state)
    {
        CurrentStep = (TutorialStep)state.step;
        CurrentErasePhase = (ErasePhase)state.erasePhase;
        remoteSerial = state.serial;
        if (dialoguePanel.activeSelf != state.dialogue)
        {
            dialoguePanel.SetActive(state.dialogue);
            dudu.SetDialogueInputLocked(state.dialogue);
        }
        speaker.text = state.speaker;
        dialogue.text = state.line;
        instruction.text = state.instructionText;
        if (instructionPanel.activeSelf != state.instruction) instructionPanel.SetActive(state.instruction);
        SetActiveIfChanged(highlight, state.highlight);
        SetActiveIfChanged(eraseHighlight, state.eraseHighlight);
        SetActiveIfChanged(replacementHighlight, state.replacementHighlight);
        if (tutorialRoom != null)
        {
            tutorialRoom.ApplyNetworkStars(state.stars, state.door);
            if (tutorialRoom.IsStarCounterVisible != state.counter) tutorialRoom.ShowStarCounter(state.counter);
        }
        if (CurrentStep == TutorialStep.Tutorial_Clear && !remoteClearPlayed)
        {
            remoteClearPlayed = true;
            GameViewManager view = FindAnyObjectByType<GameViewManager>();
            if (view != null) view.PlayAllStarsCollectedSequence(tutorialRoom);
        }
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }
}
