using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CoopVoiceChat : MonoBehaviour
{
    private const string MessageName = "CoopVoiceV1";
    private const int SampleRate = 16000;
    private const int PacketSamples = 320;
    private const int PlaybackSamples = SampleRate * 2;

    private readonly byte[] sendPacket = new byte[PacketSamples];
    private byte[] receivePacket = new byte[PacketSamples];
    private readonly float[] playbackBuffer = new float[PlaybackSamples];
    private readonly object playbackLock = new object();

    private NetworkManager manager;
    private AudioClip microphoneClip;
    private AudioClip playbackClip;
    private AudioSource playbackSource;
    private float[] microphoneBuffer;
    private int microphoneReadPosition;
    private int sendPacketCount;
    private int playbackReadPosition;
    private int playbackWritePosition;
    private int playbackCount;
    private bool playbackPrimed;
    private bool microphoneUnavailable;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (FindFirstObjectByType<CoopVoiceChat>() != null) return;
        GameObject holder = new GameObject("Voice Chat");
        holder.AddComponent<CoopVoiceChat>();
        DontDestroyOnLoad(holder);
    }

    private void Awake()
    {
        playbackSource = gameObject.AddComponent<AudioSource>();
        playbackSource.playOnAwake = false;
        playbackSource.loop = true;
        playbackSource.spatialBlend = 0f;
        playbackClip = AudioClip.Create("Remote Voice", SampleRate, 1, SampleRate, true, ReadPlayback);
        playbackSource.clip = playbackClip;
        playbackSource.Play();
    }

    private void Update()
    {
        NetworkManager active = NetworkManager.Singleton;
        if (active == null || !active.IsConnectedClient) active = null;
        if (manager != active)
        {
            StopMicrophone();
            UnregisterNetwork();
            if (active != null && active.CustomMessagingManager != null)
            {
                manager = active;
                manager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, ReceivePacket);
            }
            microphoneUnavailable = false;
        }

        playbackSource.volume = CoopAudioSettings.VoiceVolume;
        if (manager == null || !CoopAudioSettings.MicEnabled)
        {
            StopMicrophone();
            if (!CoopAudioSettings.MicEnabled) microphoneUnavailable = false;
            return;
        }

        if (microphoneClip == null) StartMicrophone();
        if (microphoneClip != null) CaptureMicrophone();
    }

    private void StartMicrophone()
    {
        if (microphoneUnavailable) return;
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("No microphone is available for voice chat.");
            microphoneUnavailable = true;
            return;
        }

        try
        {
            microphoneClip = Microphone.Start(null, true, 1, SampleRate);
            if (microphoneClip == null) throw new InvalidOperationException("Microphone.Start returned no clip.");
            microphoneBuffer = new float[microphoneClip.samples * microphoneClip.channels];
            microphoneReadPosition = Mathf.Max(0, Microphone.GetPosition(null));
            sendPacketCount = 0;
        }
        catch (Exception error)
        {
            Debug.LogWarning("Voice chat microphone could not start: " + error.Message);
            microphoneUnavailable = true;
            StopMicrophone();
        }
    }

    private void CaptureMicrophone()
    {
        int position = Microphone.GetPosition(null);
        if (position < 0 || position >= microphoneClip.samples ||
            !microphoneClip.GetData(microphoneBuffer, 0)) return;

        int available = (position - microphoneReadPosition + microphoneClip.samples) % microphoneClip.samples;
        if (available > microphoneClip.samples / 2)
        {
            microphoneReadPosition = (position - PacketSamples + microphoneClip.samples) % microphoneClip.samples;
            available = PacketSamples;
            sendPacketCount = 0;
        }

        for (int i = 0; i < available; i++)
        {
            float sample = microphoneBuffer[microphoneReadPosition * microphoneClip.channels];
            sendPacket[sendPacketCount++] = (byte)Mathf.Clamp(Mathf.RoundToInt(sample * 127f) + 128, 0, 255);
            microphoneReadPosition = (microphoneReadPosition + 1) % microphoneClip.samples;
            if (sendPacketCount != PacketSamples) continue;
            SendPacket();
            sendPacketCount = 0;
        }
    }

    private void SendPacket()
    {
        if (manager == null || !manager.IsConnectedClient) return;
        using var writer = new FastBufferWriter(PacketSamples, Allocator.Temp);
        writer.WriteBytesSafe(sendPacket, PacketSamples);

        if (!manager.IsHost)
        {
            manager.CustomMessagingManager.SendNamedMessage(
                MessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
            return;
        }

        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != manager.LocalClientId)
                manager.CustomMessagingManager.SendNamedMessage(
                    MessageName, clientId, writer, NetworkDelivery.UnreliableSequenced);
    }

    private void ReceivePacket(ulong sender, FastBufferReader reader)
    {
        if (manager == null || !reader.TryBeginRead(PacketSamples)) return;
        if (manager.IsHost)
        {
            if (sender == manager.LocalClientId || !manager.ConnectedClients.ContainsKey(sender)) return;
        }
        else if (sender != NetworkManager.ServerClientId) return;

        reader.ReadBytesSafe(ref receivePacket, PacketSamples);
        lock (playbackLock)
        {
            for (int i = 0; i < PacketSamples; i++)
            {
                if (playbackCount == PlaybackSamples)
                {
                    playbackReadPosition = (playbackReadPosition + 1) % PlaybackSamples;
                    playbackCount--;
                }
                playbackBuffer[playbackWritePosition] = (receivePacket[i] - 128) / 128f;
                playbackWritePosition = (playbackWritePosition + 1) % PlaybackSamples;
                playbackCount++;
            }

            if (playbackCount > SampleRate / 2)
            {
                int drop = playbackCount - SampleRate / 8;
                playbackReadPosition = (playbackReadPosition + drop) % PlaybackSamples;
                playbackCount -= drop;
            }
        }

        if (!manager.IsHost) return;
        using var writer = new FastBufferWriter(PacketSamples, Allocator.Temp);
        writer.WriteBytesSafe(receivePacket, PacketSamples);
        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != manager.LocalClientId && clientId != sender)
                manager.CustomMessagingManager.SendNamedMessage(
                    MessageName, clientId, writer, NetworkDelivery.UnreliableSequenced);
    }

    private void ReadPlayback(float[] data)
    {
        lock (playbackLock)
        {
            if (!playbackPrimed)
            {
                if (playbackCount < PacketSamples * 4)
                {
                    Array.Clear(data, 0, data.Length);
                    return;
                }
                playbackPrimed = true;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (playbackCount == 0)
                {
                    data[i] = 0f;
                    playbackPrimed = false;
                    continue;
                }

                data[i] = playbackBuffer[playbackReadPosition];
                playbackReadPosition = (playbackReadPosition + 1) % PlaybackSamples;
                playbackCount--;
            }
        }
    }

    private void StopMicrophone()
    {
        if (microphoneClip == null) return;
        if (Microphone.IsRecording(null)) Microphone.End(null);
        microphoneClip = null;
        microphoneBuffer = null;
        sendPacketCount = 0;
    }

    private void UnregisterNetwork()
    {
        if (manager != null && manager.CustomMessagingManager != null)
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
        manager = null;
        lock (playbackLock)
        {
            playbackReadPosition = 0;
            playbackWritePosition = 0;
            playbackCount = 0;
            playbackPrimed = false;
        }
    }

    private void OnDestroy()
    {
        StopMicrophone();
        UnregisterNetwork();
        if (playbackClip != null) Destroy(playbackClip);
    }
}
