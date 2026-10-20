using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerPoseSync : MonoBehaviour
{
    private const string HaruMessage = "HaruPoseV1";
    private const string DuduMessage = "DuduPoseV1";
    private const float SendInterval = 0.05f;

    private NetworkManager manager;
    private Transform haru;
    private DuduSurfaceMovement dudu;
    private DuduSurface[] surfaces;
    private bool isHost;
    private bool hasRemotePose;
    private Vector3 remotePosition;
    private Quaternion remoteRotation;
    private int remoteSurfaceIndex;
    private bool remoteFacingLeft;
    private float nextSendTime;

    public void Configure(Transform haruTransform, DuduSurfaceMovement duduMovement, DuduSurface[] mapSurfaces)
    {
        haru = haruTransform;
        dudu = duduMovement;
        surfaces = mapSurfaces;
    }

    private void Start()
    {
        manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsConnectedClient || haru == null || dudu == null)
        {
            Debug.LogError("Player pose sync needs an active network and both characters.", this);
            enabled = false;
            return;
        }

        isHost = manager.IsHost;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            isHost ? DuduMessage : HaruMessage,
            isHost ? OnDuduPose : OnHaruPose);
    }

    private void OnDestroy()
    {
        if (manager == null || manager.CustomMessagingManager == null) return;
        manager.CustomMessagingManager.UnregisterNamedMessageHandler(isHost ? DuduMessage : HaruMessage);
    }

    private void LateUpdate()
    {
        if (manager == null || !manager.IsConnectedClient) return;

        if (hasRemotePose)
        {
            float blend = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            if (isHost)
            {
                DuduSurface surface = surfaces != null &&
                    remoteSurfaceIndex >= 0 && remoteSurfaceIndex < surfaces.Length
                    ? surfaces[remoteSurfaceIndex] : null;
                dudu.ApplyRemotePose(surface,
                    Vector3.Distance(dudu.transform.position, remotePosition) > 2f
                        ? remotePosition
                        : Vector3.Lerp(dudu.transform.position, remotePosition, blend),
                    Quaternion.Slerp(dudu.transform.rotation, remoteRotation, blend),
                    remoteFacingLeft);
            }
            else
            {
                haru.SetPositionAndRotation(
                    Vector3.Lerp(haru.position, remotePosition, blend),
                    Quaternion.Slerp(haru.rotation, remoteRotation, blend));
            }
        }

        if (Time.unscaledTime < nextSendTime) return;
        nextSendTime = Time.unscaledTime + SendInterval;
        if (isHost) SendHaruPose();
        else SendDuduPose();
    }

    private void SendHaruPose()
    {
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(haru.position);
        writer.WriteValueSafe(haru.rotation);
        foreach (ulong clientId in manager.ConnectedClientsIds)
        {
            if (clientId != manager.LocalClientId)
                manager.CustomMessagingManager.SendNamedMessage(
                    HaruMessage, clientId, writer, NetworkDelivery.UnreliableSequenced);
        }
    }

    private void SendDuduPose()
    {
        int surfaceIndex = surfaces != null ? Array.IndexOf(surfaces, dudu.CurrentSurface) : -1;
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(dudu.transform.position);
        writer.WriteValueSafe(dudu.transform.rotation);
        writer.WriteValueSafe(surfaceIndex);
        writer.WriteValueSafe(dudu.FacingLeft);
        manager.CustomMessagingManager.SendNamedMessage(
            DuduMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
    }

    private void OnHaruPose(ulong sender, FastBufferReader reader)
    {
        if (isHost || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(28)) return;
        reader.ReadValueSafe(out remotePosition);
        reader.ReadValueSafe(out remoteRotation);
        hasRemotePose = true;
    }

    private void OnDuduPose(ulong sender, FastBufferReader reader)
    {
        if (!isHost || sender == NetworkManager.ServerClientId || !reader.TryBeginRead(33)) return;
        reader.ReadValueSafe(out remotePosition);
        reader.ReadValueSafe(out remoteRotation);
        reader.ReadValueSafe(out remoteSurfaceIndex);
        reader.ReadValueSafe(out remoteFacingLeft);
        hasRemotePose = true;
    }
}
