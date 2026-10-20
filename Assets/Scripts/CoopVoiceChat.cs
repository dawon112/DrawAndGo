using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CoopVoiceChat : MonoBehaviour
{
    private const string MessageName = "CoopVoiceV2";
    private const int SampleRate = 24000;
    private const int PacketSamples = 480;
    private const int PacketBytes = PacketSamples * 2;
    private const int PlaybackSamples = SampleRate * 2;
    private const int MicrophoneReadFrames = 2048;
    private const float SilenceThreshold = 0.001f;
    private const int SilenceTailPackets = 10;

    private static CoopVoiceChat instance;
    public static bool IsMicrophoneRecording => instance != null &&
        instance.microphoneClip != null && Microphone.IsRecording(null);

    private readonly byte[] sendPacket = new byte[PacketBytes];
    private byte[] receivePacket = new byte[PacketBytes];
    private readonly float[] playbackBuffer = new float[PlaybackSamples];
    private readonly object playbackLock = new object();

    private NetworkManager manager;
    private AudioClip microphoneClip;
    private AudioClip playbackClip;
    private AudioSource playbackSource;
    private float[] microphoneBuffer;
    private int captureSampleRate;
    private int microphoneReadPosition;
    private int resampleAccumulator;
    private float resampleSum;
    private int resampleCount;
    private int sendPacketCount;
    private float sendPacketPeak;
    private int voiceTailPackets;
    private int playbackReadPosition;
    private int playbackWritePosition;
    private int playbackCount;
    private bool playbackPrimed;
    private bool microphoneUnavailable;
    private float microphoneStartedAt;
    private float microphoneReadFailureStart = -1f;

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
        instance = this;
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
            DisableMicrophoneSetting();
            return;
        }

        try
        {
            Microphone.GetDeviceCaps(null, out int minimumRate, out int maximumRate);
            int requestedRate = minimumRate == 0 && maximumRate == 0
                ? SampleRate : Mathf.Clamp(SampleRate, minimumRate, maximumRate);
            microphoneClip = Microphone.Start(null, true, 1, requestedRate);
            if (microphoneClip == null) throw new InvalidOperationException("Microphone.Start returned no clip.");
            captureSampleRate = microphoneClip.frequency;
            if (captureSampleRate <= 0) throw new InvalidOperationException("Microphone sample rate is invalid.");
            microphoneBuffer = new float[Mathf.Min(MicrophoneReadFrames, microphoneClip.samples) *
                microphoneClip.channels];
            microphoneReadPosition = Mathf.Max(0, Microphone.GetPosition(null));
            microphoneStartedAt = Time.unscaledTime;
            resampleAccumulator = 0;
            resampleSum = 0f;
            resampleCount = 0;
            sendPacketCount = 0;
            sendPacketPeak = 0f;
            voiceTailPackets = 0;
            microphoneReadFailureStart = -1f;
        }
        catch (Exception error)
        {
            Debug.LogWarning("Voice chat microphone could not start: " + error.Message);
            microphoneUnavailable = true;
            StopMicrophone();
            DisableMicrophoneSetting();
        }
    }

    private void CaptureMicrophone()
    {
        if (!Microphone.IsRecording(null))
        {
            if (Time.unscaledTime - microphoneStartedAt < 1f) return;
            Debug.LogWarning("Voice chat microphone stopped recording.");
            StopMicrophone();
            DisableMicrophoneSetting();
            return;
        }

        int position = Microphone.GetPosition(null);
        if (position < 0 || position >= microphoneClip.samples) return;
        int available = (position - microphoneReadPosition + microphoneClip.samples) % microphoneClip.samples;
        if (available == 0) return;
        if (available > microphoneClip.samples / 2)
        {
            available = Mathf.Min(microphoneClip.samples / 2,
                PacketSamples * captureSampleRate / SampleRate);
            microphoneReadPosition = (position - available + microphoneClip.samples) % microphoneClip.samples;
            sendPacketCount = 0;
            sendPacketPeak = 0f;
            resampleAccumulator = 0;
            resampleSum = 0f;
            resampleCount = 0;
        }

        int channels = microphoneClip.channels;
        while (available > 0)
        {
            int frames = Mathf.Min(available, microphoneBuffer.Length / channels);
            if (!microphoneClip.GetData(new Span<float>(microphoneBuffer, 0, frames * channels),
                    microphoneReadPosition))
            {
                if (microphoneReadFailureStart < 0f) microphoneReadFailureStart = Time.unscaledTime;
                if (Time.unscaledTime - microphoneReadFailureStart < 2f) return;
                Debug.LogWarning("Voice chat could not read microphone samples.");
                StopMicrophone();
                DisableMicrophoneSetting();
                return;
            }
            microphoneReadFailureStart = -1f;

            for (int i = 0; i < frames; i++)
            {
                float sample = microphoneBuffer[i * channels];
                resampleSum += sample;
                resampleCount++;
                resampleAccumulator += SampleRate;
                if (resampleAccumulator < captureSampleRate) continue;

                float output = resampleSum / resampleCount;
                resampleSum = 0f;
                resampleCount = 0;
                do
                {
                    resampleAccumulator -= captureSampleRate;
                    short encoded = (short)Mathf.Clamp(Mathf.RoundToInt(output * 32767f),
                        short.MinValue, short.MaxValue);
                    int byteIndex = sendPacketCount * 2;
                    sendPacket[byteIndex] = (byte)encoded;
                    sendPacket[byteIndex + 1] = (byte)(encoded >> 8);
                    sendPacketPeak = Mathf.Max(sendPacketPeak, Mathf.Abs(output));
                    sendPacketCount++;
                    if (sendPacketCount != PacketSamples) continue;
                    if (sendPacketPeak >= SilenceThreshold) voiceTailPackets = SilenceTailPackets;
                    else if (voiceTailPackets > 0) voiceTailPackets--;
                    if (voiceTailPackets > 0) SendPacket();
                    sendPacketCount = 0;
                    sendPacketPeak = 0f;
                } while (resampleAccumulator >= captureSampleRate);
            }

            microphoneReadPosition = (microphoneReadPosition + frames) % microphoneClip.samples;
            available -= frames;
        }
    }

    private static void DisableMicrophoneSetting()
    {
        CoopAudioSettings.MicEnabled = false;
        CoopAudioSettings.Save();
    }

    private void SendPacket()
    {
        if (manager == null || !manager.IsConnectedClient) return;
        using var writer = new FastBufferWriter(PacketBytes, Allocator.Temp);
        writer.WriteBytesSafe(sendPacket, PacketBytes);

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
        if (manager == null || !reader.TryBeginRead(PacketBytes)) return;
        if (manager.IsHost)
        {
            if (sender == manager.LocalClientId || !manager.ConnectedClients.ContainsKey(sender)) return;
        }
        else if (sender != NetworkManager.ServerClientId) return;

        reader.ReadBytesSafe(ref receivePacket, PacketBytes);
        lock (playbackLock)
        {
            for (int i = 0; i < PacketSamples; i++)
            {
                if (playbackCount == PlaybackSamples)
                {
                    playbackReadPosition = (playbackReadPosition + 1) % PlaybackSamples;
                    playbackCount--;
                }
                int byteIndex = i * 2;
                short encoded = (short)(receivePacket[byteIndex] | receivePacket[byteIndex + 1] << 8);
                playbackBuffer[playbackWritePosition] = encoded / 32768f;
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
        using var writer = new FastBufferWriter(PacketBytes, Allocator.Temp);
        writer.WriteBytesSafe(receivePacket, PacketBytes);
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
        sendPacketPeak = 0f;
        voiceTailPackets = 0;
        resampleAccumulator = 0;
        resampleSum = 0f;
        resampleCount = 0;
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
        if (instance == this) instance = null;
        StopMicrophone();
        UnregisterNetwork();
        if (playbackClip != null) Destroy(playbackClip);
    }
}
