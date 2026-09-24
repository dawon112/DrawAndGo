using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Haru owns drawing. Dudu owns pickups and hazards so collisions use local physics.
// The host still owns the Relay session. This is a cooperative prototype, not anti-cheat.
[DisallowMultipleComponent]
public sealed class StageStateSync : MonoBehaviour
{
    private const string ReadyMessage = "StageReadyV1";
    private const string StrokeMessage = "StageStrokeV1";
    private const string WorldMessage = "StageWorldV1";
    private const int ChunkPoints = 16;
    private NetworkManager network;
    private CornerRoomLevel level;
    private DuduMovingObstacle[] movers;
    private MovingEraserObstacle[] erasers;
    private DuduHomingShooter[] shooters;
    private string layout;
    private bool ready;
    private ulong peer;
    private float nextSend;
    private Material remoteMaterial;
    private readonly Dictionary<int, int> sentPoints = new();
    private readonly Dictionary<int, DrawingStroke> remoteStrokes = new();

    [Serializable]
    private sealed class StrokeDelta
    {
        public int id, offset;
        public float width, thickness, depth;
        public Color color;
        public Vector3 normal;
        public Vector3[] points;
    }

    [Serializable]
    private struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool active;
        public static Pose Capture(Transform target) => target == null ? default : new Pose
        { position = target.position, rotation = target.rotation, active = target.gameObject.activeInHierarchy };
    }

    [Serializable]
    private sealed class WorldState
    {
        public int collected;
        public DuduSurfaceMovement.NetworkVisualState duduVisual;
        public Pose[] movers, erasers, projectiles;
        public bool doorOpen, clear;
    }

    private void Start()
    {
        network = NetworkManager.Singleton;
        level = FindAnyObjectByType<CornerRoomLevel>();
        if (network == null || !network.IsConnectedClient || level == null)
        { enabled = false; return; }
        movers = FindObjectsByType<DuduMovingObstacle>();
        erasers = FindObjectsByType<MovingEraserObstacle>();
        shooters = FindObjectsByType<DuduHomingShooter>();
        Array.Sort(movers, (a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
        Array.Sort(erasers, (a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
        Array.Sort(shooters, (a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
        layout = "stage-v1";
        foreach (var coin in level.coins) layout += "|coin:" + (coin != null ? Path(coin.transform) : "null");
        foreach (var mover in movers) layout += "|move:" + Path(mover.transform);
        foreach (var eraser in erasers) layout += "|eraser:" + Path(eraser.transform);
        foreach (var shooter in shooters) layout += "|shoot:" + Path(shooter.transform);
        network.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, OnReady);
        network.CustomMessagingManager.RegisterNamedMessageHandler(StrokeMessage, OnStroke);
        network.CustomMessagingManager.RegisterNamedMessageHandler(WorldMessage, OnWorld);
        if (!network.IsHost)
        {
            var drawing = FindAnyObjectByType<HaruDrawingController>(FindObjectsInactive.Include);
            if (drawing == null) { Debug.LogError("Stage sync cannot find Haru drawing.", this); enabled = false; return; }
            remoteMaterial = drawing.CreateStrokeMaterialCopy();
        }
    }

    private static string Path(Transform target) => target.parent == null
        ? target.name : Path(target.parent) + "/" + target.name;

    private void LateUpdate()
    {
        if (network == null || !network.IsConnectedClient || Time.unscaledTime < nextSend) return;
        nextSend = Time.unscaledTime + 0.1f;
        if (!ready)
        {
            if (!network.IsHost) Send(ReadyMessage, NetworkManager.ServerClientId, layout);
            return;
        }
        if (network.IsHost)
        {
            SendStrokes();
            Send(WorldMessage, peer, JsonUtility.ToJson(new WorldState
            { doorOpen = level.IsDoorVisuallyOpen, clear = level.IsClear }));
        }
        else
        {
            var state = new WorldState
            {
                movers = new Pose[movers.Length],
                erasers = new Pose[erasers.Length],
                projectiles = new Pose[shooters.Length]
            };
            if (level.player != null) state.duduVisual = level.player.CaptureNetworkVisual();
            for (int i = 0; i < level.coins.Length; i++)
                if (level.coins[i] == null || !level.coins[i].activeSelf) state.collected |= 1 << i;
            for (int i = 0; i < movers.Length; i++) state.movers[i] = Pose.Capture(movers[i].transform);
            for (int i = 0; i < erasers.Length; i++) state.erasers[i] = Pose.Capture(erasers[i] != null ? erasers[i].transform : null);
            for (int i = 0; i < shooters.Length; i++) state.projectiles[i] = Pose.Capture(shooters[i].ActiveProjectile);
            Send(WorldMessage, NetworkManager.ServerClientId, JsonUtility.ToJson(state));
        }
    }

    private void OnReady(ulong sender, FastBufferReader reader)
    {
        if (!ValidSender(sender)) return;
        reader.ReadValueSafe(out string remoteLayout);
        if (remoteLayout != layout)
        {
            Debug.LogError("Stage layouts differ. Both players must use the same build.", this);
            return;
        }
        if (network.IsHost)
        {
            peer = sender;
            if (!ready) sentPoints.Clear();
            Send(ReadyMessage, peer, layout);
        }
        ready = true;
    }

    private bool ValidSender(ulong sender)
    {
        if (network == null) return false;
        if (!network.IsHost) return sender == NetworkManager.ServerClientId;
        return sender != NetworkManager.ServerClientId && network.ConnectedClients.ContainsKey(sender);
    }

    private void SendStrokes()
    {
        var strokes = FindObjectsByType<DrawingStroke>();
        var alive = new HashSet<int>();
        foreach (var stroke in strokes) alive.Add(stroke.NetworkStrokeId);
        foreach (int id in new List<int>(sentPoints.Keys))
        {
            if (alive.Contains(id)) continue;
            Send(StrokeMessage, peer, JsonUtility.ToJson(new StrokeDelta { id = id, offset = -1 }));
            sentPoints.Remove(id);
        }
        foreach (var stroke in strokes)
        {
            int id = stroke.NetworkStrokeId;
            sentPoints.TryGetValue(id, out int offset);
            var line = stroke.GetComponent<LineRenderer>();
            while (offset < stroke.PointCount)
            {
                int count = Mathf.Min(ChunkPoints, stroke.PointCount - offset);
                var delta = new StrokeDelta
                {
                    id = id, offset = offset, width = line.startWidth, color = line.startColor,
                    thickness = stroke.ColliderThickness, depth = stroke.ColliderDepth,
                    normal = stroke.SurfaceNormal, points = new Vector3[count]
                };
                for (int i = 0; i < count; i++) delta.points[i] = line.GetPosition(offset + i);
                Send(StrokeMessage, peer, JsonUtility.ToJson(delta));
                offset += count;
            }
            sentPoints[id] = offset;
        }
    }

    private void OnStroke(ulong sender, FastBufferReader reader)
    {
        if (network.IsHost || !ready || !ValidSender(sender)) return;
        reader.ReadValueSafe(out string json);
        var delta = JsonUtility.FromJson<StrokeDelta>(json);
        if (delta == null) return;
        if (delta.offset == -1)
        {
            if (remoteStrokes.Remove(delta.id, out var removed) && removed != null)
            { removed.gameObject.SetActive(false); Destroy(removed.gameObject); }
            return;
        }
        if (delta.points == null || delta.points.Length == 0 || delta.points.Length > ChunkPoints) return;
        if (!remoteStrokes.TryGetValue(delta.id, out var stroke))
        {
            if (delta.offset != 0) return;
            var holder = new GameObject("Remote Drawing Stroke");
            holder.transform.SetParent(transform, false);
            stroke = holder.AddComponent<DrawingStroke>();
            int layer = LayerMask.NameToLayer("DrawnStroke");
            stroke.Initialize(remoteMaterial, delta.width, delta.color, delta.points[0],
                delta.thickness, delta.depth, delta.normal, layer >= 0 ? layer : 0);
            remoteStrokes.Add(delta.id, stroke);
            DuduCameraController.RegisterRuntimeRenderer(stroke.GetComponent<LineRenderer>());
        }
        // Reliable ordered deltas append only new points; existing colliders remain intact.
        if (delta.offset > stroke.PointCount) return;
        for (int i = Mathf.Max(0, stroke.PointCount - delta.offset); i < delta.points.Length; i++)
            stroke.AddPoint(delta.points[i]);
    }

    private void OnWorld(ulong sender, FastBufferReader reader)
    {
        if (!ready || !ValidSender(sender)) return;
        reader.ReadValueSafe(out string json);
        var state = JsonUtility.FromJson<WorldState>(json);
        if (state == null) return;
        if (!network.IsHost)
        {
            if (state.doorOpen) level.OpenDoorForCinematic();
            if (state.clear) level.ApplyRemoteClear();
            return;
        }
        if (state.movers == null || state.movers.Length != movers.Length ||
            state.erasers == null || state.erasers.Length != erasers.Length ||
            state.projectiles == null || state.projectiles.Length != shooters.Length) return;
        if (level.player != null) level.player.ApplyNetworkVisual(state.duduVisual);
        for (int i = 0; i < level.coins.Length; i++)
            if ((state.collected & (1 << i)) != 0 && level.coins[i] != null) level.coins[i].SetActive(false);
        for (int i = 0; i < movers.Length; i++)
            movers[i].ApplyRemotePose(state.movers[i].position, state.movers[i].rotation);
        for (int i = 0; i < erasers.Length; i++)
            erasers[i].ApplyRemotePose(state.erasers[i].active,
                state.erasers[i].position, state.erasers[i].rotation);
        for (int i = 0; i < shooters.Length; i++)
            shooters[i].ApplyRemoteProjectile(state.projectiles[i].active,
                state.projectiles[i].position, state.projectiles[i].rotation);
    }

    private void Send(string name, ulong destination, string payload)
    {
        using var writer = new FastBufferWriter(8 + payload.Length * 2, Allocator.Temp);
        writer.WriteValueSafe(payload);
        network.CustomMessagingManager.SendNamedMessage(name, destination, writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void OnDestroy()
    {
        if (network != null && network.CustomMessagingManager != null)
        {
            network.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(StrokeMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(WorldMessage);
        }
        if (remoteMaterial != null) Destroy(remoteMaterial);
    }
}
