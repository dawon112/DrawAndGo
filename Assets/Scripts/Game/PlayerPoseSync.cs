using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PlayerPoseSync : MonoBehaviour
{
    private const string HaruMessage = "HaruPoseV2";
    private const string DuduMessage = "DuduPoseV2";
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
    private int sceneIndex;

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
        sceneIndex = SceneManager.GetActiveScene().buildIndex;
        // Stages without CornerRoomLevel (the tutorial) index every Dudu surface by
        // hierarchy path, which is identical on both peers running the same build.
        if (surfaces == null || surfaces.Length == 0)
        {
            surfaces = FindObjectsByType<DuduSurface>(FindObjectsInactive.Include);
            Array.Sort(surfaces, (a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
        }
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

    private static string PathOf(Transform target) => target.parent == null
        ? target.name : PathOf(target.parent) + "/" + target.name;

    private void SendHaruPose()
    {
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(sceneIndex);
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
        writer.WriteValueSafe(sceneIndex);
        writer.WriteValueSafe(dudu.transform.position);
        writer.WriteValueSafe(dudu.transform.rotation);
        writer.WriteValueSafe(surfaceIndex);
        writer.WriteValueSafe(dudu.FacingLeft);
        manager.CustomMessagingManager.SendNamedMessage(
            DuduMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
    }

    private void OnHaruPose(ulong sender, FastBufferReader reader)
    {
        if (isHost || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(32)) return;
        // During a stage change the peer may still be in the previous scene.
        reader.ReadValueSafe(out int remoteScene);
        if (remoteScene != sceneIndex) return;
        reader.ReadValueSafe(out remotePosition);
        reader.ReadValueSafe(out remoteRotation);
        hasRemotePose = true;
    }

    private void OnDuduPose(ulong sender, FastBufferReader reader)
    {
        if (!isHost || sender == NetworkManager.ServerClientId || !reader.TryBeginRead(37)) return;
        reader.ReadValueSafe(out int remoteScene);
        if (remoteScene != sceneIndex) return;
        reader.ReadValueSafe(out remotePosition);
        reader.ReadValueSafe(out remoteRotation);
        reader.ReadValueSafe(out remoteSurfaceIndex);
        reader.ReadValueSafe(out remoteFacingLeft);
        hasRemotePose = true;
    }
}
